using UnityEngine;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using System.Collections.Generic;

public static class AutoInstallerPackages
{
    // ================= 配置区域 =================

    // 1. 基础包：项目刚创建时通常需要的
    static readonly string[] normalPackagesToInstall = new string[]
    {
        "com.unity.ide.visualstudio",
        "com.unity.ugui",
        "com.merry-yellow.code-assist",
        "https://github.com/WooshiiDev/HierarchyDecorator.git",
        "https://github.com/dbrizov/NaughtyAttributes.git#upm"
        //"com.unity.textmeshpro", // 建议加上 TMP
        // "com.unity.addressables", 
    };

    // 2. 进阶包/工具包：项目中期引入热更、混淆或特定库时需要的
    static readonly string[] BClassPackagesToInstall = new string[]
    {
        // Git URL 直接填入即可，Unity 支持
       // "https://github.com/focus-creative-games/hybridclr_unity.git",
        "https://github.com/focus-creative-games/obfuz.git",
        "com.unity.nuget.newtonsoft-json",
        // 如果有 OpenUPM 的包，建议尽量用 Scope Registry 配置，或者直接用 git url
    };

    // ================= 逻辑区域 =================

    static AddRequest request;
    static Queue<string> packagesQueue;
    static bool isInstalling = false;

    // --- 菜单入口 1 ---
    [MenuItem("Tools/包管理/1. 安装基础包 (Normal)")]
    public static void InstallNormalPackages()
    {
        StartBatchInstall(normalPackagesToInstall, "基础包");
    }

    // --- 菜单入口 2 ---
    [MenuItem("Tools/包管理/2. 安装进阶包 (B-Class)")]
    public static void InstallBClassPackages()
    {
        StartBatchInstall(BClassPackagesToInstall, "进阶包");
    }

    // --- 通用逻辑 ---

    static void StartBatchInstall(string[] packages, string batchName)
    {
        if (isInstalling)
        {
            Debug.LogWarning("当前正在进行安装任务，请等待完成后再操作。");
            return;
        }

        if (packages == null || packages.Length == 0)
        {
            Debug.LogWarning("没有配置需要安装的包。");
            return;
        }

        Debug.Log($"=== 开始批量安装 [{batchName}] ===");
        packagesQueue = new Queue<string>(packages);
        isInstalling = true;
        StartNextInstall();
    }

    static void StartNextInstall()
    {
        if (packagesQueue.Count > 0)
        {
            string packageId = packagesQueue.Dequeue();
            Debug.Log($"[安装中] 正在请求: {packageId} ...");

            // Client.Add 既支持 com.xxx 也支持 https://github...
            request = Client.Add(packageId);
            EditorApplication.update += Progress;
        }
        else
        {
            Debug.Log("=== ✅ 所有包安装流程结束 ===");
            isInstalling = false;
            // 可以在这里加一个 AssetDatabase.Refresh(); 强制刷新资源
        }
    }

    static void Progress()
    {
        if (request.IsCompleted)
        {
            if (request.Status == StatusCode.Success)
            {
                Debug.Log($"[成功] 已安装: {request.Result.packageId}");
            }
            else if (request.Status >= StatusCode.Failure)
            {
                // 打印详细错误，防止 Git 网络问题导致不知道发生了什么
                Debug.LogError($"[失败] 无法安装: {request.Error.message} (错误码: {request.Error.errorCode})");
            }

            EditorApplication.update -= Progress;
            StartNextInstall(); // 无论成功失败，尝试安装下一个
        }
    }
}