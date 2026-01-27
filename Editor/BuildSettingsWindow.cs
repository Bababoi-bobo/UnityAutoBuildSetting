using UnityEditor;
using UnityEngine;

public enum PackageType
{
    自定义 = 0,
    原提白包 = 1,
    更新加B面 = 10001
}

public class BuildSettingsWindow : ScriptableWizard
{
    [Header("快捷预设")]
    public PackageType packageType = PackageType.原提白包;

    [Header("基础打包设置")]
    public string packageName = "com.gsdfg.asdafg";
    public string appName = "fasd";
    public string version = "1.1";
    public int versionCode = 1;

    [Header("高级配置")]
    public string customClassName = "BiwzybaYxpkjgfyv";
    public bool isPortrait = true;

    private PackageType lastPackageType;

    [MenuItem("Build/Quick Android Settings...")]
    static void CreateWizard()
    {
        var wizard = DisplayWizard<BuildSettingsWindow>("Android 快速配置", "应用并保存", "仅保存参数");
        wizard.packageName = EditorPrefs.GetString("BS_PkgName", PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android));
        wizard.appName = EditorPrefs.GetString("BS_AppName", PlayerSettings.productName);
        wizard.version = EditorPrefs.GetString("BS_Ver", PlayerSettings.bundleVersion);
        wizard.versionCode = EditorPrefs.GetInt("BS_VerCode", PlayerSettings.Android.bundleVersionCode);
        wizard.customClassName = EditorPrefs.GetString("BS_ClassName", "BiwzybaYxpkjgfyv");
        wizard.isPortrait = EditorPrefs.GetBool("BS_IsPortrait", true);
        wizard.packageType = (PackageType)EditorPrefs.GetInt("BS_PkgType", (int)PackageType.原提白包);
        wizard.lastPackageType = wizard.packageType;
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

    protected override bool DrawWizardGUI()
    {
        EditorGUILayout.HelpBox("提示：VersionCode 为 1 时禁用宿主类名，不生成 Java 宿主文件。", MessageType.Info);
        packageType = (PackageType)EditorGUILayout.EnumPopup("快捷预设", packageType);

        EditorGUILayout.Space();
        packageName = EditorGUILayout.TextField("Package Name", packageName);
        appName = EditorGUILayout.TextField("App Name", appName);
        version = EditorGUILayout.TextField("Version", version);
        versionCode = EditorGUILayout.IntField("Version Code", versionCode);

        EditorGUILayout.Space();
        bool isWhitePkg = (packageType == PackageType.原提白包);

        // 如果不是白包，则尝试显示检测到的名字
        if (!isWhitePkg)
        {
            // 实时调用逻辑层的方法获取名字
            GUI.enabled = false; // 禁用输入框，改为自动读取
            string detected = "等待读取...";
            // 简单反射或直接调用静态方法
            // customClassName = BuildSettingsEditor.GetJavaClassNameFromBuildFolder(); 
            EditorGUILayout.TextField("Detected Class Name", BuildSettingsEditor.className);
            GUI.enabled = true;
        }
        else
        {
            GUI.enabled = false;
            // 在 BuildSettingsWindow.cs 的 DrawWizardGUI 中
            customClassName = EditorGUILayout.TextField("Custom Class Name", isWhitePkg ? "UnityPlayerActivity" : BuildSettingsEditor.className);
            GUI.enabled = true;
        }
        isPortrait = EditorGUILayout.Toggle("Is Portrait (竖屏)", isPortrait);
        return true;
    }

    void OnWizardCreate()
    {
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

        // 核心更改：通过枚举类型直接判断，而不是看版本号
        BuildSettingsEditor.isWhitePackage = (packageType == PackageType.原提白包);
    }
}