using UnityEditor;
using UnityEngine;
using System.Text.RegularExpressions;

public enum PackageType
{
    自定义 = 0,
    原提白包 = 1,
    更新加B面 = 10001
}

public class BuildSettingsWindow : ScriptableWizard
{
    [Header("一键导入解析 (支持表格制表符复制)")]
    public string rawImportText = "";

    [Header("快捷预设")]
    public PackageType packageType = PackageType.原提白包;

    [Header("基础打包设置")]
    public string packageName = "";
    public string appName = "";
    public string version = "1.0";
    public int versionCode = 1;

    [Header("高级配置")]
    public string customClassName = "";
    public bool isPortrait = true;

    private PackageType lastPackageType;
    [MenuItem("Build/Quick Android Settings...")]
    static void CreateWizard()
    {
        var wizard = DisplayWizard<BuildSettingsWindow>("Android 快速配置", "应用并保存", "仅保存参数");

        // 1. 恢复：从 EditorPrefs 加载上一次保存的数据
        wizard.packageName = EditorPrefs.GetString("BS_PkgName", PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android));
        wizard.appName = EditorPrefs.GetString("BS_AppName", PlayerSettings.productName);
        wizard.version = EditorPrefs.GetString("BS_Ver", PlayerSettings.bundleVersion);
        wizard.versionCode = EditorPrefs.GetInt("BS_VerCode", PlayerSettings.Android.bundleVersionCode);
        wizard.isPortrait = EditorPrefs.GetBool("BS_IsPortrait", true);
        wizard.packageType = (PackageType)EditorPrefs.GetInt("BS_PkgType", (int)PackageType.原提白包);

        // 只有 ClassName 我们需要根据模式谨慎加载
        string savedClass = EditorPrefs.GetString("BS_ClassName", "");
        wizard.customClassName = (wizard.packageType == PackageType.原提白包) ? "UnityPlayerActivity" : savedClass;

        // 清空解析框，避免干扰
        wizard.rawImportText = "";

        wizard.lastPackageType = wizard.packageType;

        // 同步给逻辑层
        wizard.SyncToLogic();
    }
    protected override bool DrawWizardGUI()
    {
        // 导入区域
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("📋 从表格复制整行数据粘贴到下方", EditorStyles.miniBoldLabel);
        rawImportText = EditorGUILayout.TextArea(rawImportText, GUILayout.Height(50));
        if (GUILayout.Button("⚡ 识别制表符并填充"))
        {
            ParseRawText();
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space();

        packageType = (PackageType)EditorGUILayout.EnumPopup("快捷预设", packageType);

        EditorGUILayout.Space();
        packageName = EditorGUILayout.TextField("Package Name", packageName);
        appName = EditorGUILayout.TextField("App Name", appName);
        version = EditorGUILayout.TextField("Version", version);
        versionCode = EditorGUILayout.IntField("Version Code", versionCode);

        EditorGUILayout.Space();
        bool isWhitePkg = (packageType == PackageType.原提白包);

        if (!isWhitePkg)
        {
            if (string.IsNullOrEmpty(customClassName))
            {
                EditorGUILayout.HelpBox("⚠️ B面模式必须填入 Custom Class Name", MessageType.Error);
            }
            customClassName = EditorGUILayout.TextField("Custom Class Name", customClassName);
        }
        else
        {
            GUI.enabled = false;
            customClassName = "UnityPlayerActivity";
            EditorGUILayout.TextField("Custom Class Name", customClassName);
            GUI.enabled = true;
        }

        isPortrait = EditorGUILayout.Toggle("Is Portrait (竖屏)", isPortrait);
        return true;
    }

    private void ParseRawText()
    {
        if (string.IsNullOrEmpty(rawImportText)) return;

        // 使用制表符拆分
        string[] parts = rawImportText.Split('\t');
        if (parts.Length < 2)
        {
            EditorUtility.DisplayDialog("解析失败", "未检测到制表符，请确保是从表格（Excel/在线文档）中复制的整行数据。", "确定");
            return;
        }

        // 1. 提取包名 (寻找 com. 开头的项)
        foreach (string p in parts)
        {
            string t = p.Trim();
            if (Regex.IsMatch(t, @"com\.[a-z0-9_]+\.[a-z0-9_]+"))
            {
                packageName = Regex.Match(t, @"com\.[a-z0-9_]+\.[a-z0-9_]+").Value;
                break;
            }
        }

        // 2. 提取应用名 (逻辑：包名之后紧跟的一列，且不含图片或jks后缀)
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (parts[i].Contains(packageName))
            {
                string candidate = parts[i + 1].Trim();
                if (!candidate.Contains("image.png") && !candidate.Contains(".jks") && !string.IsNullOrEmpty(candidate))
                {
                    appName = candidate;
                    break;
                }
                // 兼容某些格式：如果 i+1 是图片，尝试 i+2
                if (i + 2 < parts.Length)
                {
                    candidate = parts[i + 2].Trim();
                    if (!candidate.Contains("image.png") && !candidate.Contains(".jks"))
                    {
                        appName = candidate;
                        break;
                    }
                }
            }
        }

        // 3. 提取类型
        if (rawImportText.Contains("原提白包"))
        {
            packageType = PackageType.原提白包;
            versionCode = 1;
        }
        else if (rawImportText.Contains("B面") || rawImportText.Contains("B 面"))
        {
            packageType = PackageType.更新加B面;
            versionCode = 10001;
        }

        Repaint();
        EditorUtility.DisplayDialog("解析完成", $"已识别：\n包名: {packageName}\n应用名: {appName}\n模式: {packageType}", "OK");
    }

    void OnInspectorUpdate()
    {
        if (packageType != lastPackageType)
        {
            if (packageType == PackageType.原提白包) versionCode = 1;
            else if (packageType == PackageType.更新加B面) versionCode = 10001;
            lastPackageType = packageType;
            Repaint();
        }
    }

    void OnWizardCreate()
    {
        if (packageType != PackageType.原提白包 && string.IsNullOrEmpty(customClassName))
        {
            EditorUtility.DisplayDialog("错误", "B面模式下 Custom Class Name 不能为空！", "返回");
            CreateWizard();
            return;
        }
        SaveToPrefs();
        SyncToLogic();
        BuildSettingsEditor.UpdateAndroidSettings();
    }

    void OnWizardOtherButton()
    {
        SaveToPrefs();
        SyncToLogic();
    }

    void SaveToPrefs()
    {
        EditorPrefs.SetInt("BS_PkgType", (int)packageType);
        EditorPrefs.SetString("BS_PkgName", packageName);
        EditorPrefs.SetString("BS_AppName", appName);
        EditorPrefs.SetString("BS_Ver", version);
        EditorPrefs.SetInt("BS_VerCode", versionCode);
        EditorPrefs.SetString("BS_ClassName", customClassName);
        EditorPrefs.SetBool("BS_IsPortrait", isPortrait);
        EditorPrefs.SetBool("BS_IsWhitePackage", packageType == PackageType.原提白包);
    }

    void SyncToLogic()
    {
        BuildSettingsEditor.packageName = packageName;
        BuildSettingsEditor.appName = appName;
        BuildSettingsEditor.version = version;
        BuildSettingsEditor.versionCode = versionCode;
        BuildSettingsEditor.className = (packageType == PackageType.原提白包) ? "UnityPlayerActivity" : customClassName;
        BuildSettingsEditor.isPortrait = isPortrait;
        BuildSettingsEditor.isWhitePackage = (packageType == PackageType.原提白包);
    }
}