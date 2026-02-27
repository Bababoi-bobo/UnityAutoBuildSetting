using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using System.Collections.Generic;
using System.IO;

public class TextureReplacerWindow : EditorWindow
{
    private List<Texture2D> leftTextures = new List<Texture2D>();
    private List<string> rightFilePaths = new List<string>();
    private Dictionary<string, Texture2D> previewCache = new Dictionary<string, Texture2D>();

    private ReorderableList leftList;
    private ReorderableList rightList;
    private Vector2 scrollPos;

    [MenuItem("Tools/批量图片替换工具 (全能版)")]
    public static void ShowWindow() => GetWindow<TextureReplacerWindow>("素材对齐替换器").minSize = new Vector2(750, 600);

    private void OnEnable()
    {
        ClearPreviewCache();
        InitLeftList();
        InitRightList();
    }

    private void InitLeftList()
    {
        leftList = new ReorderableList(leftTextures, typeof(Texture2D), true, true, true, true);
        leftList.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "1. Unity 内原图 (Target) - 支持内部批量拖入");
        leftList.elementHeight = 60;
        leftList.drawElementCallback = (rect, index, isActive, isFocused) => {
            rect.y += 2;
            leftTextures[index] = (Texture2D)EditorGUI.ObjectField(new Rect(rect.x, rect.y, rect.width, 55), 
                leftTextures[index], typeof(Texture2D), false);
        };
        leftList.onAddCallback = list => leftTextures.Add(null);
    }

    private void InitRightList()
    {
        rightList = new ReorderableList(rightFilePaths, typeof(string), true, true, true, true);
        rightList.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "2. 外部新素材 (Source) - 支持系统批量拖入");
        rightList.elementHeight = 60;
        rightList.drawElementCallback = (rect, index, isActive, isFocused) => {
            string filePath = rightFilePaths[index];
            rect.y += 2;
            string fileName = string.IsNullOrEmpty(filePath) ? "Empty Slot" : Path.GetFileName(filePath);
            
            // 绘制预览图
            Texture2D previewTex = GetOrCreatePreview(filePath);
            if (previewTex != null) 
                EditorGUI.DrawPreviewTexture(new Rect(rect.x, rect.y, 55, 55), previewTex);

            // 绘制文件名和路径提示
            EditorGUI.LabelField(new Rect(rect.x + 60, rect.y + 18, rect.width - 80, 20), fileName, EditorStyles.boldLabel);
            GUI.Label(rect, new GUIContent("", filePath)); // Tooltip
        };
        rightList.onAddCallback = list => rightFilePaths.Add("");
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(10);
        
        // 核心：处理拖拽逻辑
        HandleDragAndDrop();

        EditorGUILayout.HelpBox("操作技巧：\n? 直接从 Project 窗口拖入多张图到左侧区域即可批量添加。\n? 直接从系统文件夹拖入多张图到右侧区域即可批量添加。", MessageType.Info);

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
        float colWidth = (position.width - 40) * 0.5f;

        EditorGUILayout.BeginHorizontal();
        
        // 左侧绘制
        GUILayout.BeginVertical(GUILayout.Width(colWidth));
        leftList.DoLayoutList();
        GUILayout.EndVertical();

        GUILayout.Space(10);

        // 右侧绘制
        GUILayout.BeginVertical(GUILayout.Width(colWidth));
        rightList.DoLayoutList();
        GUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndScrollView();

        DrawBottomAction();
    }

    private void HandleDragAndDrop()
    {
        Event evt = Event.current;
        // 定义两个区域的 Rect，大致对应左右两栏
        Rect leftRect = new Rect(0, 50, position.width / 2, position.height - 150);
        Rect rightRect = new Rect(position.width / 2, 50, position.width / 2, position.height - 150);

        if (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform)
        {
            bool isLeft = leftRect.Contains(evt.mousePosition);
            bool isRight = rightRect.Contains(evt.mousePosition);

            if (isLeft || isRight)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                if (evt.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();

                    // 处理左侧：Unity 内部资源
                    if (isLeft)
                    {
                        foreach (Object obj in DragAndDrop.objectReferences)
                        {
                            if (obj is Texture2D tex) leftTextures.Add(tex);
                        }
                    }
                    // 处理右侧：外部物理文件
                    else if (isRight)
                    {
                        foreach (string path in DragAndDrop.paths)
                        {
                            if (IsSupportedFormat(path)) rightFilePaths.Add(path);
                        }
                    }
                    evt.Use();
                }
            }
        }
    }

    private bool IsSupportedFormat(string path)
    {
        string ext = Path.GetExtension(path).ToLower();
        return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".tga" || ext == ".tiff";
    }

    private Texture2D GetOrCreatePreview(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return null;
        if (previewCache.TryGetValue(filePath, out Texture2D cachedTex)) return cachedTex;

        byte[] fileData = File.ReadAllBytes(filePath);
        Texture2D texture = new Texture2D(2, 2);
        if (texture.LoadImage(fileData))
        {
            previewCache[filePath] = texture;
            return texture;
        }
        return null;
    }

    private void ClearPreviewCache()
    {
        foreach (var kvp in previewCache) if (kvp.Value != null) DestroyImmediate(kvp.Value);
        previewCache.Clear();
    }

    private void DrawBottomAction()
    {
        EditorGUILayout.BeginVertical("box");
        int count = Mathf.Min(leftTextures.Count, rightFilePaths.Count);
        
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("清空全部")) { leftTextures.Clear(); rightFilePaths.Clear(); ClearPreviewCache(); }
        
        GUI.enabled = count > 0;
        GUI.backgroundColor = Color.green;
        if (GUILayout.Button($"一键替换 ({count}组对齐素材)", GUILayout.Height(40)))
        {
            if (EditorUtility.DisplayDialog("确认覆盖", "右侧外部文件将直接覆盖左侧工程文件，请确保备份！", "确定", "取消"))
                ExecuteReplace(count);
        }
        GUI.backgroundColor = Color.white;
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    private void ExecuteReplace(int count)
    {
        int success = 0;
        for (int i = 0; i < count; i++)
        {
            if (leftTextures[i] == null || string.IsNullOrEmpty(rightFilePaths[i])) continue;
            string oldPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../", AssetDatabase.GetAssetPath(leftTextures[i])));
            try {
                File.Copy(rightFilePaths[i], oldPath, true);
                success++;
            } catch (System.Exception e) { Debug.LogError("替换失败: " + e.Message); }
        }
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("替换完成", $"成功覆盖 {success} 张图片。", "OK");
    }

    private void OnDisable() => ClearPreviewCache();
}