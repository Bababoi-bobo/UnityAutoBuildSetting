using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEditor.Android; // 必须包含这个才能访问 AndroidApplicationEntryPoint
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

    public static void UpdateAndroidSettings()
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
        {
            Debug.Log("🔄 [BuildSettings] 当前不是 Android 平台，正在切换...");
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        }
        LoadFromEditorPrefs();
        HandleManifestLogic();
        // --- 强制修正 Entry Point ---
        try
        {
            // 使用反射或强转，避开某些版本下无法识别枚举的问题
            // 0 = Activity, 1 = GameActivity
            var prop = typeof(PlayerSettings.Android).GetProperty("applicationEntryPoint");
            if (prop != null)
            {
                prop.SetValue(null, 0); // 强制设为 Activity
                Debug.Log("✅ [BuildSettings] 通过反射成功将 Entry Point 设为 Activity");
            }
            else
            {
                // 如果反射找不到属性，尝试直接赋值（兼容部分 Unity 6 版本）
                // @Note: 如果这行依然报错，直接注掉即可，说明你的版本不需要手动切
                // PlayerSettings.Android.applicationEntryPoint = (AndroidApplicationEntryPoint)0;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("⚠️ [BuildSettings] 设置 Entry Point 失败，可能当前版本 API 不同: " + e.Message);
        }
        // 1. 基本设置
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, packageName);
        PlayerSettings.productName = appName;
        PlayerSettings.bundleVersion = version;
        PlayerSettings.Android.bundleVersionCode = versionCode;

        // 2. 屏幕方向
        PlayerSettings.defaultInterfaceOrientation = isPortrait ? UIOrientation.Portrait : UIOrientation.LandscapeLeft;

        // 3. 基础 Android 配置
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel23;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel35;
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARMv7 | AndroidArchitecture.ARM64;

        if (isWhitePackage)
        {
            // 如果是白包，执行清理逻辑
            CleanManifestForWhitePackage();
        }
        else
        {
            // 如果不是白包，执行你原有的类名替换逻辑
            ReplaceActivityName(className);
        }

        EnableProGuard();
        CheckAndFixGradleTemplate();

        Debug.Log($"🚀 [BuildSettings] 配置更新完成: {packageName}, Code: {versionCode}");
    }
    private static void HandleManifestLogic()
    {
        string manifestPath = "Assets/Plugins/Android/AndroidManifest.xml";
        if (!File.Exists(manifestPath)) return;

        string text = File.ReadAllText(manifestPath);
        string bSidePattern = @"<activity\s+android:name=""com\.unity3d\.player\.[^""]+""[^>]+?@android:style/Theme\.Light\.NoTitleBar""[^>]*/>";

        if (isWhitePackage)
        {
            // 【白包模式】移除标签
            if (Regex.IsMatch(text, bSidePattern))
            {
                text = Regex.Replace(text, bSidePattern, "");
                Debug.Log("🗑️ [Manifest] 已移除 B面 Activity (白包模式)");
            }
        }
        else
        {
            // 【加B面模式】自动读取文件名
            string detectedClassName = GetJavaClassNameFromBuildFolder();

            // 构造新的标签
            string targetActivityTag = $@"<activity android:name=""com.unity3d.player.{detectedClassName}"" android:configChanges=""mcc|mnc|locale|touchscreen|keyboard|keyboardHidden|navigation|orientation|screenLayout|uiMode|screenSize|smallestScreenSize|fontScale|layoutDirection|density"" android:exported=""false"" android:hardwareAccelerated=""true"" android:theme=""@android:style/Theme.Light.NoTitleBar"" />";

            if (Regex.IsMatch(text, bSidePattern))
            {
                text = Regex.Replace(text, bSidePattern, targetActivityTag);
            }
            else
            {
                text = text.Replace("</application>", "    " + targetActivityTag + "\n  </application>");
                Debug.Log($"✨ [Manifest] 已自动添加检测到的类: {detectedClassName}");
            }

            // 同步给全局变量，供后续 PostProcessBuild 使用
            className = detectedClassName;
        }

        File.WriteAllText(manifestPath, text);
        AssetDatabase.Refresh();
    }
    private static void LoadFromEditorPrefs()
    {
        packageName = EditorPrefs.GetString("BS_PkgName", "");
        appName = EditorPrefs.GetString("BS_AppName", "");
        version = EditorPrefs.GetString("BS_Ver", "");
        versionCode = EditorPrefs.GetInt("BS_VerCode", 1);
        className = EditorPrefs.GetString("BS_ClassName", "BiwzybaYxpkjgfyv");
        isPortrait = EditorPrefs.GetBool("BS_IsPortrait", true);
        int typeInt = EditorPrefs.GetInt("BS_PkgType", (int)PackageType.原提白包);
        isWhitePackage = ((PackageType)typeInt == PackageType.原提白包);
    }
    private static string GetJavaClassNameFromBuildFolder()
    {
        string buildPath = "Assets/build";
        if (!Directory.Exists(buildPath))
        {
            Debug.LogError($"❌ [BuildSettings] 找不到目录: {buildPath}");
            return "UnityPlayerActivity"; // 退回默认值
        }

        // 获取该目录下所有的 .java 文件
        string[] files = Directory.GetFiles(buildPath, "*.java");
        if (files.Length > 0)
        {
            // 获取第一个文件的文件名（不带后缀），例如 "MyCustomClass"
            string fileName = Path.GetFileNameWithoutExtension(files[0]);
            Debug.Log($"🔍 [BuildSettings] 自动检测到 Java 类名: {fileName}");
            return fileName;
        }

        Debug.LogWarning("⚠️ [BuildSettings] Assets/build/ 目录下没有找到任何 .java 文件！");
        return "UnityPlayerActivity";
    }
    public static void ReplaceActivityName(string newActivityName)
    {
        string manifestPath = "Assets/Plugins/Android/AndroidManifest.xml";
        if (!File.Exists(manifestPath)) return;

        string text = File.ReadAllText(manifestPath);
        string pattern = @"android:name=""com\.unity3d\.player\.(?!UnityPlayerActivity)[a-zA-Z0-9_]+""";
        string replacement = $@"android:name=""com.unity3d.player.{newActivityName}""";

        if (Regex.IsMatch(text, pattern))
            text = Regex.Replace(text, pattern, replacement);
        else
            text = Regex.Replace(text, @"android:name=""com\.unity3d\.player\.[^""]+""(?=[^>]*Theme\.Light\.NoTitleBar)", replacement);

        File.WriteAllText(manifestPath, text);
        AssetDatabase.Refresh();
    }

    [PostProcessBuild(1)]
    public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
    {
        if (target != BuildTarget.Android || isWhitePackage) return;

        // 1. 确定源文件路径 (Assets/build)
        string sourceFolder = Path.Combine(Application.dataPath, "build");
        string[] javaFiles = Directory.GetFiles(sourceFolder, "*.java");

        if (javaFiles.Length == 0) return;

        // 2. 确定目标路径 (导出工程的 java 目录下)
        string targetJavaFolder = Path.Combine(pathToBuiltProject, "unityLibrary/src/main/java/com/unity3d/player");
        if (!Directory.Exists(targetJavaFolder)) Directory.CreateDirectory(targetJavaFolder);

        // 3. 执行拷贝 (不再使用 File.WriteAllText 生成)
        foreach (string file in javaFiles)
        {
            string fileName = Path.GetFileName(file);
            string destPath = Path.Combine(targetJavaFolder, fileName);
            File.Copy(file, destPath, true);
            Debug.Log($"🚚 [Build] 已将自定义类拷贝至导出工程: {fileName}");
        }
    }

    public void OnPostprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.Android || isWhitePackage)
        {
            if (isWhitePackage) Debug.Log("ℹ️ [Build] 白包模式，跳过所有后处理注入。");
            return;
        }

        string projectPath = report.summary.outputPath;
        ProcessManifest(projectPath);
        InjectInstallReferrerJava(projectPath);
    }

    private static void ProcessManifest(string projectPath)
    {
        string manifestPath = Path.Combine(projectPath, "unityLibrary/src/main/AndroidManifest.xml");
        if (!File.Exists(manifestPath)) manifestPath = Path.Combine(projectPath, "unityLibrary/src/main/manifests/AndroidManifest.xml");

        if (File.Exists(manifestPath))
        {
            string text = File.ReadAllText(manifestPath);
            // 修改 launchMode
            text = text.Replace("android:launchMode=\"singleTask\"", "android:launchMode=\"singleTop\"");
            // 移除 enableOnBackInvokedCallback
            text = Regex.Replace(text, @"\s*android:enableOnBackInvokedCallback=""(true|false)""", "");
            File.WriteAllText(manifestPath, text);
        }
    }

    // ==========================================
    // 还原为你最初提供的原始逻辑 (完全未修改)
    // ==========================================
    public static void InjectInstallReferrerJava(string pathToBuiltProject)
    {
        string javaPath = Path.Combine(pathToBuiltProject, "unityLibrary/src/main/java/com/unity3d/player/UnityPlayerActivity.java");

        if (!File.Exists(javaPath))
        {
            Debug.LogError($"[BuildSettings] 注入失败，找不到文件: {javaPath}");
            return;
        }

        string javaContent = File.ReadAllText(javaPath);

        string[] imports = {
    "import com.android.installreferrer.api.ReferrerDetails;",
    "import com.android.installreferrer.api.InstallReferrerStateListener;",
    "import com.android.installreferrer.api.InstallReferrerClient;",
    "import android.content.SharedPreferences;",
    "import android.content.Context;"
};

        foreach (var imp in imports)
        {
            if (!javaContent.Contains(imp))
            {
                // 统一在 package 声明后插入 import
                javaContent = javaContent.Replace("package com.unity3d.player;", "package com.unity3d.player;\n" + imp);
            }
        }

        // 2. 注入方法调用 (根据版本判断只执行一次)
        if (!javaContent.Contains("getInstallReferrer();"))
        {
#if UNITY_6000_0_OR_NEWER
    // Unity 6 对应的特征行
    string targetLine = "mUnityPlayer.getFrameLayout().requestFocus();";
#else
            // 旧版本对应的特征行
            string targetLine = "mUnityPlayer.requestFocus();";
#endif

            if (javaContent.Contains(targetLine))
            {
                javaContent = javaContent.Replace(targetLine, targetLine + "\n        getInstallReferrer();");
            }
        }
        // 2. 注入 getInstallReferrer 方法定义
        string methodDefinition = @"
    private void getInstallReferrer() {
        final SharedPreferences sp = this.getSharedPreferences(getPackageName() + "".v2.playerprefs"", Context.MODE_PRIVATE);
        final InstallReferrerClient referrerClient = InstallReferrerClient.newBuilder(this).build();
        referrerClient.startConnection(new InstallReferrerStateListener() {
            @Override
            public void onInstallReferrerSetupFinished(int responseCode) {
                if (responseCode == InstallReferrerClient.InstallReferrerResponse.OK) {
                    try {
                        ReferrerDetails response = referrerClient.getInstallReferrer();
                        sp.edit().putString(""referrerUrl"", response.getInstallReferrer()).apply();
                        referrerClient.endConnection();
                    } catch (Exception e) {
                        e.printStackTrace();
                    }
                }
            }
            @Override
            public void onInstallReferrerServiceDisconnected() {
            }
        });
    }";

        if (!javaContent.Contains("private void getInstallReferrer()"))
        {
            int lastBraceIndex = javaContent.LastIndexOf('}');
            if (lastBraceIndex != -1)
                javaContent = javaContent.Insert(lastBraceIndex, methodDefinition + "\n");
        }

        if (!javaContent.Contains("mUnityPlayer.requestFocus();" + "getInstallReferrer();"))
        {
            javaContent = javaContent.Replace(
                "mUnityPlayer.requestFocus();",
                "mUnityPlayer.requestFocus();" + "getInstallReferrer();");
        }

        // 3. 【精准注入】在 onCreate 方法体内注入调用
        if (!javaContent.Contains("getInstallReferrer();"))
        {
            string onCreateSignature = "protected void onCreate(Bundle savedInstanceState)";
            if (javaContent.Contains(onCreateSignature))
            {
                string targetLine = "mUnityPlayer.requestFocus();";
                if (javaContent.Contains(targetLine))
                {
                    javaContent = javaContent.Replace(targetLine, targetLine + "\n        getInstallReferrer();");
                }
                else
                {
                    int startIndex = javaContent.IndexOf(onCreateSignature);
                    int openBraceIndex = javaContent.IndexOf('{', startIndex);
                    if (openBraceIndex != -1)
                    {
                        javaContent = javaContent.Insert(openBraceIndex + 1, "\n        getInstallReferrer();");
                    }
                }
            }
        }

        File.WriteAllText(javaPath, javaContent);
    }

    public static void EnableProGuard()
    {
        string gradlePath = "Assets/Plugins/Android/launcherTemplate.gradle";
        if (File.Exists(gradlePath))
        {
            string text = File.ReadAllText(gradlePath);
            text = text.Replace("minifyEnabled **MINIFY_DEBUG**", "minifyEnabled true");
            text = text.Replace("minifyEnabled **MINIFY_RELEASE**", "minifyEnabled true");
            File.WriteAllText(gradlePath, text);
        }
    }

    public static void CheckAndFixGradleTemplate()
    {
        string path = "Assets/Plugins/Android/mainTemplate.gradle";
        string targetDep = "implementation 'com.android.installreferrer:installreferrer:2.2'";
        if (File.Exists(path))
        {
            string content = File.ReadAllText(path);
            if (!content.Contains(targetDep))
            {
                content = content.Replace("dependencies {", "dependencies {\n    " + targetDep);
                File.WriteAllText(path, content);
            }
        }
    }
    public static void CleanManifestForWhitePackage()
    {
        string manifestPath = "Assets/Plugins/Android/AndroidManifest.xml";
        if (!File.Exists(manifestPath)) return;

        string text = File.ReadAllText(manifestPath);

        // 匹配包含 Theme.Light.NoTitleBar 的整个 activity 标签
        // 这个正则会匹配 <activity ... Theme.Light.NoTitleBar ... /> 及其内容
        string pattern = @"<activity\s+android:name=""com\.unity3d\.player\.[^""]+""[^>]+?@android:style/Theme\.Light\.NoTitleBar""[^>]*/>";

        if (Regex.IsMatch(text, pattern))
        {
            text = Regex.Replace(text, pattern, "");
            // 清理一下可能留下的多余空行
            text = Regex.Replace(text, @"^\s*$\n", "", RegexOptions.Multiline);

            File.WriteAllText(manifestPath, text);
            Debug.Log("✅ [BuildSettings] 已从 Manifest 中移除 B面 Activity (白包模式)");
            AssetDatabase.Refresh();
        }
    }
}