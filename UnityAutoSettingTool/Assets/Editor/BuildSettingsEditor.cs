using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public class BuildSettingsEditor : IPostprocessBuildWithReport
{
    public static string packageName;
    public static string appName;
    public static int versionCode;
    public static string version;
    public static bool isPortrait;
    public static string className;
    public static bool isWhitePackage;

    public int callbackOrder => 100;

    // 定位到 Plugins/Android 目录，确保生成的 Java 与 UnityPlayerActivity 同级
    private static string AndroidPluginsPath => Path.Combine(Application.dataPath, "Plugins/Android");

    public static void UpdateAndroidSettings()
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        }

        LoadFromEditorPrefs();

        // 1. 生命周期管理：如果是白包则删除旧文件，如果是 B 面则生成新文件
        HandleJavaFileLifecycle();

        // 2. 修正 Entry Point (针对 Unity 6+ 强制设为 Activity)
        try
        {
            var prop = typeof(PlayerSettings.Android).GetProperty("applicationEntryPoint");
            if (prop != null) prop.SetValue(null, 0);
        }
        catch (Exception e) { Debug.LogWarning("⚠️ Entry Point 修正尝试跳过: " + e.Message); }

        // 3. 基础 PlayerSettings 写入
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, packageName);
        PlayerSettings.productName = appName;
        PlayerSettings.bundleVersion = version;
        PlayerSettings.Android.bundleVersionCode = versionCode;
        PlayerSettings.defaultInterfaceOrientation = isPortrait ? UIOrientation.Portrait : UIOrientation.LandscapeLeft;

        // 4. Android 核心配置
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel23;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel35;
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARMv7 | AndroidArchitecture.ARM64;

        // 5. 修改 Manifest 标签与混淆开关
        HandleManifestLogic();
        EnableProGuard();

        AssetDatabase.Refresh();
        Debug.Log($"✅ [BuildSettings] 配置同步完成。当前模式: {(isWhitePackage ? "原提白包 (已清理自定义类)" : "更新加B面 (" + className + ")")}");
    }

    private static void HandleJavaFileLifecycle()
    {
        if (!Directory.Exists(AndroidPluginsPath)) Directory.CreateDirectory(AndroidPluginsPath);

        // 扫描并清理本插件可能生成的旧 Java 文件
        string[] files = Directory.GetFiles(AndroidPluginsPath, "*.java");
        foreach (var file in files)
        {
            // 通过特征识别：包名为 com.unity3d.player 且不是 Unity 默认类名
            string content = File.ReadAllText(file);
            if (content.Contains("package com.unity3d.player") && !file.EndsWith("UnityPlayerActivity.java"))
            {
                File.Delete(file);
                if (File.Exists(file + ".meta")) File.Delete(file + ".meta");
                Debug.Log($"🗑️ [BuildSettings] 已自动删除旧 Java 文件: {Path.GetFileName(file)}");
            }
        }

        // 如果是 B 面模式，原地生成新类
        if (!isWhitePackage && !string.IsNullOrEmpty(className))
        {
            string newPath = Path.Combine(AndroidPluginsPath, className + ".java");
            string code = $@"package com.unity3d.player;

import android.app.Activity;

public class {className} extends Activity {{
}}";
            File.WriteAllText(newPath, code);
            Debug.Log($"📝 [BuildSettings] 已生成 B 面 Activity: {className}.java");
        }
    }

    private static void HandleManifestLogic()
    {
        string manifestPath = Path.Combine(AndroidPluginsPath, "AndroidManifest.xml");
        if (!File.Exists(manifestPath)) return;

        string text = File.ReadAllText(manifestPath);
        string bSidePattern = @"<activity\s+android:name=""com\.unity3d\.player\.[^""]+""[^>]+?@android:style/Theme\.Light\.NoTitleBar""[^>]*/>";

        if (isWhitePackage)
        {
            // 白包模式：移除 Manifest 中的 B 面 Activity 注册
            if (Regex.IsMatch(text, bSidePattern))
            {
                text = Regex.Replace(text, bSidePattern, "");
                Debug.Log("🗑️ [Manifest] 已移除 B 面 Activity 注册");
            }
        }
        else
        {
            // B 面模式：更新或插入 Activity 注册
            string tag = $@"<activity android:name=""com.unity3d.player.{className}"" android:configChanges=""mcc|mnc|locale|touchscreen|keyboard|keyboardHidden|navigation|orientation|screenLayout|uiMode|screenSize|smallestScreenSize|fontScale|layoutDirection|density"" android:exported=""false"" android:hardwareAccelerated=""true"" android:theme=""@android:style/Theme.Light.NoTitleBar"" />";

            if (Regex.IsMatch(text, bSidePattern))
                text = Regex.Replace(text, bSidePattern, tag);
            else
                text = text.Replace("</application>", "    " + tag + "\n  </application>");
        }

        File.WriteAllText(manifestPath, text);
    }

    private static void LoadFromEditorPrefs()
    {
        packageName = EditorPrefs.GetString("BS_PkgName", "");
        appName = EditorPrefs.GetString("BS_AppName", "");
        version = EditorPrefs.GetString("BS_Ver", "1.0");
        versionCode = EditorPrefs.GetInt("BS_VerCode", 1);
        isPortrait = EditorPrefs.GetBool("BS_IsPortrait", true);
        int typeInt = EditorPrefs.GetInt("BS_PkgType", (int)PackageType.原提白包);
        isWhitePackage = ((PackageType)typeInt == PackageType.原提白包);
        className = isWhitePackage ? "UnityPlayerActivity" : EditorPrefs.GetString("BS_ClassName", "");
    }

    public static void EnableProGuard()
    {
        string gradlePath = Path.Combine(AndroidPluginsPath, "launcherTemplate.gradle");
        if (File.Exists(gradlePath))
        {
            string text = File.ReadAllText(gradlePath);
            text = text.Replace("minifyEnabled **MINIFY_DEBUG**", "minifyEnabled true");
            text = text.Replace("minifyEnabled **MINIFY_RELEASE**", "minifyEnabled true");
            File.WriteAllText(gradlePath, text);
        }
    }

    public void OnPostprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.Android || isWhitePackage) return;
        ProcessManifestAfterBuild(report.summary.outputPath);
    }

    private static void ProcessManifestAfterBuild(string projectPath)
    {
        string manifestPath = Path.Combine(projectPath, "unityLibrary/src/main/AndroidManifest.xml");
        if (!File.Exists(manifestPath)) manifestPath = Path.Combine(projectPath, "unityLibrary/src/main/manifests/AndroidManifest.xml");

        if (File.Exists(manifestPath))
        {
            string text = File.ReadAllText(manifestPath);
            text = text.Replace("android:launchMode=\"singleTask\"", "android:launchMode=\"singleTop\"");

            // 强制设置 enableOnBackInvokedCallback 为 false
            text = Regex.Replace(text, @"\s*android:enableOnBackInvokedCallback=""(true|false)""", "");
            if (text.Contains("<application"))
                text = text.Replace("<application", "<application android:enableOnBackInvokedCallback=\"false\"");

            File.WriteAllText(manifestPath, text);
        }
    }
}