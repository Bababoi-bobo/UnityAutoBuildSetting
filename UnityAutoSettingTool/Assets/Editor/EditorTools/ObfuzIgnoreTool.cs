using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public class ObfuzIgnoreTool : EditorWindow
{
    private Vector2 scrollPos;
    private string logMessage = "等待操作...\n";
    private List<string> pendingFiles = new List<string>();

    [MenuItem("Tools/Obfuz 注入工具")]
    static void OpenWindow()
    {
        GetWindow<ObfuzIgnoreTool>("Obfuz 注入工具");
    }

    private void OnGUI()
    {
        GUILayout.Label("Obfuz.ObfuzIgnore 批量添加工具", EditorStyles.boldLabel);
        
        EditorGUILayout.HelpBox(
            "请将 .cs 脚本文件或文件夹拖入窗口任意区域。\n" +
            "工具将自动检测并在 class/struct 定义前添加 [Obfuz.ObfuzIgnore]。", 
            MessageType.Info);

        GUILayout.Space(10);

        GUILayout.Label("Bootstrap 生成:", EditorStyles.boldLabel);
        if (GUILayout.Button("在选中文件夹生成 Bootstrap.cs"))
        {
            CreateBootstrapScript();
        }

        GUILayout.Space(10);

        // 待处理文件确认区域
        if (pendingFiles.Count > 0)
        {
            EditorGUILayout.HelpBox($"已识别 {pendingFiles.Count} 个 .cs 文件等待处理。", MessageType.Warning);
            
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("确认注入标签", GUILayout.Height(30)))
            {
                ProcessPendingFiles();
            }
            if (GUILayout.Button("取消", GUILayout.Height(30)))
            {
                pendingFiles.Clear();
                Log("操作已取消。");
            }
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(10);
        }

        GUILayout.Label("处理日志:", EditorStyles.label);
        
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(300));
        EditorGUILayout.TextArea(logMessage);
        EditorGUILayout.EndScrollView();

        if (GUILayout.Button("清空日志"))
        {
            logMessage = "等待操作...\n";
        }

        HandleDragAndDrop();
    }

    private void HandleDragAndDrop()
    {
        Event evt = Event.current;
        Rect dropArea = new Rect(0, 0, position.width, position.height);

        switch (evt.type)
        {
            case EventType.DragUpdated:
            case EventType.DragPerform:
                if (!dropArea.Contains(evt.mousePosition))
                    return;

                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                if (evt.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    CollectDroppedFiles(DragAndDrop.paths);
                }
                break;
        }
    }

    private void CollectDroppedFiles(string[] paths)
    {
        pendingFiles.Clear();
        foreach (string path in paths)
        {
            if (Directory.Exists(path))
            {
                pendingFiles.AddRange(Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories));
            }
            else if (File.Exists(path) && Path.GetExtension(path).ToLower() == ".cs")
            {
                pendingFiles.Add(path);
            }
        }

        if (pendingFiles.Count == 0)
        {
            Log("拖入的内容中未找到 .cs 文件。");
        }
        else
        {
            Log($"找到 {pendingFiles.Count} 个文件，请点击确认按钮开始处理。");
        }
    }

    private void ProcessPendingFiles()
    {
        if (pendingFiles.Count == 0) return;

        Log($"开始处理 {pendingFiles.Count} 个文件...");
        int modifiedCount = 0;

        foreach (string file in pendingFiles)
        {
            if (ProcessFile(file))
            {
                modifiedCount++;
                Log($"[修改] {Path.GetFileName(file)}");
            }
        }

        Log($"处理完成。共修改 {modifiedCount} 个文件。");
        pendingFiles.Clear();

        if (modifiedCount > 0)
        {
            AssetDatabase.Refresh();
        }
    }

    private void CreateBootstrapScript()
    {
        string path = "Assets";
        if (Selection.activeObject != null)
        {
            path = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (File.Exists(path))
            {
                path = Path.GetDirectoryName(path);
            }
        }
        else
        {
             Log("未选中任何文件夹，默认生成在 Assets 根目录。");
        }

        string fullPath = Path.Combine(path, "Bootstrap.cs");
        
        string content = @"using Obfuz;
using Obfuz.EncryptionVM;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public class Bootstrap : MonoBehaviour
{
    // 初始化EncryptionService后被混淆的代码才能正常运行，
    // 因此尽可能地早地初始化它。
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    private static void SetUpStaticSecretKey()
    {
        Debug.Log(""SetUpStaticSecret begin"");
        EncryptionService<DefaultStaticEncryptionScope>.Encryptor = new GeneratedEncryptionVirtualMachine(Resources.Load<TextAsset>(""Obfuz/defaultStaticSecretKey"").bytes);
        Debug.Log(""SetUpStaticSecret end"");
    }

    int Add(int a, int b)
    {
        return a + b + 1;
    }

    // Start is called before the first frame update
    void Start()
    {
        Debug.Log(""Hello, Obfuz"");
        int a = Add(10, 20);
        Debug.Log($""a = {a}"");
    }
}";

        try 
        {
            File.WriteAllText(fullPath, content);
            AssetDatabase.Refresh();
            Log($"Bootstrap.cs 已生成至: {fullPath}");
            
            var obj = AssetDatabase.LoadAssetAtPath<Object>(fullPath);
            if (obj != null) Selection.activeObject = obj;
        }
        catch (System.Exception e)
        {
            Log($"生成失败: {e.Message}");
        }
    }

    private bool ProcessFile(string filePath)
    {
        var lines = new List<string>(File.ReadAllLines(filePath));
        bool fileModified = false;
        
        Regex classRegex = new Regex(@"\b(class|struct)\b\s+\w+");

        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];
            
            if (line.Trim().StartsWith("//")) continue;

            if (classRegex.IsMatch(line))
            {
                // 1. 检查当前行
                if (line.Contains("[Obfuz.ObfuzIgnore]")) continue;

                // 2. 向上扫描属性
                bool hasAttribute = false;
                int j = i - 1;
                while (j >= 0)
                {
                    string prevLine = lines[j].Trim();
                    if (string.IsNullOrEmpty(prevLine))
                    {
                        j--;
                        continue;
                    }

                    if (prevLine.Contains("[Obfuz.ObfuzIgnore]"))
                    {
                        hasAttribute = true;
                        break;
                    }

                    // 如果是其他属性
                    if (prevLine.StartsWith("[") && prevLine.EndsWith("]"))
                    {
                        j--;
                        continue;
                    }

                    // 遇到非属性行
                    if (!prevLine.StartsWith("["))
                    {
                        break;
                    }
                    
                    j--;
                }

                if (!hasAttribute)
                {
                    string indentation = "";
                    Match match = Regex.Match(line, @"^(\s*)");
                    if (match.Success)
                    {
                        indentation = match.Groups[1].Value;
                    }

                    lines.Insert(i, indentation + "[Obfuz.ObfuzIgnore]");
                    fileModified = true;
                    i++; 
                }
            }
        }

        if (fileModified)
        {
            File.WriteAllLines(filePath, lines);
            return true;
        }

        return false;
    }

    private void Log(string msg)
    {
        logMessage += msg + "\n";
        scrollPos.y = 100000;
        Repaint();
    }
}
