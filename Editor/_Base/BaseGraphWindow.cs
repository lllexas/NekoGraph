#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using NekoGraph;
using NekoGraph.Editor;

/// <summary>
/// 统一 Pack 编辑器窗口 - 非泛型，文件驱动喵~
/// 打开任意 Pack JSON，自动调用对应 SearchWindow，无需子类喵~
/// </summary>
public class PackWindow : EditorWindow, IPackWindowSaveable
{
        #region IPackWindowSaveable Implementation

        /// <summary>关联的资源路径（用于自动保存）</summary>
        public string AssetPath { get; private set; }

        /// <summary>是否有有效的资源路径</summary>
        public bool HasValidAssetPath => !string.IsNullOrEmpty(AssetPath);

        /// <summary>窗口标题</summary>
        public string Title => titleContent?.text ?? "Pack Editor";

        /// <summary>是否已修改（脏标记）</summary>
        public bool IsDirty { get; private set; }

        /// <summary>静默保存（用于自动保存）</summary>
        public void SilentSave()
        {
            if (!HasValidAssetPath || _currentPack == null) return;

            try
            {
                _graphView.FlushToPack(_currentPack);
                File.WriteAllText(AssetPath, _currentPack.ToJson());
                IsDirty = false;
                UpdateTitle();
                Debug.Log($"[PackWindow] 已自动保存: {AssetPath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[PackWindow] 自动保存失败: {e.Message}");
            }
        }

        /// <summary>标记为已修改</summary>
        private void MarkDirty()
        {
            if (!IsDirty)
            {
                IsDirty = true;
                UpdateTitle();
            }
        }

        /// <summary>更新窗口标题（显示脏标记）</summary>
        private void UpdateTitle()
        {
            string baseTitle = _currentPack?.PackID ?? "Pack Editor";
            string dirtyMark = IsDirty ? " *" : "";
            string newMark = HasValidAssetPath ? "" : " [New]";
            titleContent = new GUIContent($"{baseTitle}{dirtyMark}{newMark}");
        }

        #endregion

        /// <summary>
        /// 从资源路径打开 PackWindow
        /// </summary>
        public static void OpenWithAsset(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return;

            // 检查是否已经有打开的窗口编辑这个文件
            var existingWindows = Resources.FindObjectsOfTypeAll<PackWindow>();
            foreach (var window in existingWindows)
            {
                if (window.AssetPath == assetPath)
                {
                    window.Focus();
                    return;
                }
            }

            // 创建新窗口
            var newWindow = CreateWindow<PackWindow>();
            newWindow.AssetPath = assetPath;
            newWindow.LoadFromPath(assetPath);
            newWindow.Show();
        }

        /// <summary>
        /// 从指定路径加载 Pack
        /// </summary>
        public void LoadFromPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            // 确保是绝对路径
            string fullPath = path;
            if (!Path.IsPathRooted(path))
            {
                fullPath = Path.Combine(Application.dataPath, "..", path);
                fullPath = Path.GetFullPath(fullPath);
            }

            if (!File.Exists(fullPath))
            {
                Debug.LogError($"[PackWindow] 文件不存在: {fullPath}");
                return;
            }

            string json;
            try { json = File.ReadAllText(fullPath); }
            catch (Exception e) { EditorUtility.DisplayDialog("读取失败", e.Message, "确定"); return; }

            BasePackData pack;
            try { pack = BasePackData.FromJson(json); }
            catch (Exception e) { EditorUtility.DisplayDialog("读取失败", $"JSON 格式错误：{e.Message}", "确定"); return; }

            if (pack == null) { EditorUtility.DisplayDialog("读取失败", "文件内容为空喵~", "确定"); return; }

            _currentPack = pack;
            _currentFilePath = fullPath;
            AssetPath = path; // 保存相对路径

            _graphView.PopulateFromPack(pack);

            string id = !string.IsNullOrEmpty(pack.PackID) ? pack.PackID : Path.GetFileNameWithoutExtension(path);
            _packIDField.SetValueWithoutNotify(id);
            _graphView.SetPackID(id);
            _systemField.SetValueWithoutNotify(pack.System);

            // 同步滑块和标签喵~（使用反向映射）
            _readableFromSlider.SetValueWithoutNotify(ValueToSlider(pack.ReadableFrom));
            _writableFromSlider.SetValueWithoutNotify(ValueToSlider(pack.WritableFrom));
            _readableFromValueLabel.text = pack.ReadableFrom.ToString();
            _writableFromValueLabel.text = pack.WritableFrom.ToString();
            _readableFromSlider.tooltip = BuildAccessTooltip(pack.ReadableFrom, "可读");
            _writableFromSlider.tooltip = BuildAccessTooltip(pack.WritableFrom, "可写");
            ValidateAccessConfig();

            SetupSearchWindow(pack);

            IsDirty = false;
            UpdateTitle();

            Debug.Log($"[PackWindow] 已加载: {path}");
        }

        /// <summary>
        /// GraphView 内容变更回调
        /// </summary>
        private void OnGraphContentChanged()
        {
            MarkDirty();
        }

        /// <summary>
        /// 如果有未保存的更改，提示用户保存
        /// </summary>
        /// <param name="action">正在执行的操作名称</param>
        /// <returns>true 表示继续操作，false 表示取消</returns>
        private bool PromptSaveIfDirty(string action)
        {
            if (!IsDirty) return true;

            int result = EditorUtility.DisplayDialogComplex("未保存的更改",
                $"当前 Pack 有未保存的更改，是否先保存？\n\n操作：{action}",
                "保存",   // 0
                "不保存", // 1
                "取消");  // 2

            switch (result)
            {
                case 0: // 保存并继续
                    if (HasValidAssetPath)
                    {
                        SilentSave();
                        return true;
                    }
                    else
                    {
                        // 新文件从未保存过，走完整保存流程
                        SaveData();
                        // 如果保存成功（有路径了），继续；如果取消了保存对话框，取消操作
                        return HasValidAssetPath;
                    }
                case 1: // 不保存，继续
                    return true;
                default: // 取消
                    return false;
            }
        }

    private BaseGraphView _graphView;
    private VisualElement _viewContainer;
    private TextField _packIDField;
    private Slider _readableFromSlider;
    private Slider _writableFromSlider;
    private Label _readableFromValueLabel;
    private Label _writableFromValueLabel;
    private EnumField _systemField;
    private BasePackData _currentPack;
    private string _currentFilePath;

    // 对数滑块参数喵~（十进制，人类友好！）
    // 滑块范围 -0.2~3.4（直接就是 log₁₀ 的值）
    // 权限值 = Floor(10^sliderValue) 喵~
    private const float SliderMin = -0.2f;
    private const float SliderMax = 3.4f;

    /// <summary>
    /// 滑块值 → 权限值（十进制对数映射 + 向下取整）喵~
    /// </summary>
    private int SliderToValue(float sliderValue)
    {
        return Mathf.FloorToInt(Mathf.Pow(10f, sliderValue));
    }

    /// <summary>
    /// 权限值 → 滑块值（反向映射）喵~
    /// 权限 0 → 滑块 -0.1（中间位置）喵~
    /// </summary>
    private float ValueToSlider(int value)
    {
        if (value <= 0) return -0.1f;
        return Mathf.Log10(value);
    }

    /// <summary>
    /// 获取权限值的描述文本喵~
    /// </summary>
    private string GetAccessTickLabel(int value)
    {
        if (value == 0) return "无限制";
        if (value == 1) return "禁止玩家";
        if (value <= 10) return $"禁止玩家 (≥{value})";
        if (value <= 100) return "禁止玩家、AI";
        if (value <= 1000) return $"禁止玩家、AI (≥{value})";
        return "禁止玩家、AI、系统";
    }

    /// <summary>
    /// 构建权限 Tooltip 喵~
    /// </summary>
    private string BuildAccessTooltip(int value, string type)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"【{type}】当前值：{value}");
        sb.AppendLine();
        sb.AppendLine($"状态：{GetAccessTickLabel(value)}");
        sb.AppendLine();
        sb.AppendLine("刻度参考：");
        sb.AppendLine("  0       1       10      100     1000");
        sb.AppendLine("无限制   禁止玩家  禁止 AI  禁止系统");
        return sb.ToString();
    }

    [MenuItem("NekoGraph/✨ 打开 Pack 编辑器")]
    public static void Open()
    {
        var w = CreateWindow<PackWindow>();
        w.titleContent = new GUIContent("Pack Editor");
        w.Show();
    }

    private void OnEnable() => BuildLayout();

    private void OnDisable()
    {
        // 关闭窗口时自动保存（ShaderGraph 风格）
        if (IsDirty && HasValidAssetPath)
        {
            SilentSave();
            Debug.Log($"[PackWindow] 关闭时自动保存: {AssetPath}");
        }

        if (_graphView != null)
            _viewContainer?.Remove(_graphView);
    }

    private void BuildLayout()
    {
        rootVisualElement.Clear();
        GenerateToolbar();

        _viewContainer = new VisualElement { name = "ViewContainer" };
        _viewContainer.style.flexGrow = 1;
        rootVisualElement.Add(_viewContainer);

        _graphView = new BaseGraphView { name = "NekoGraph" };
        _graphView.StretchToParentSize();
        _graphView.OnContentChanged = OnGraphContentChanged;
        _viewContainer.Add(_graphView);

        // 立即初始化SearchWindow，确保右键菜单立即可用
        InitializeDefaultPack();
        SetupSearchWindow(_currentPack);

        // 设置拖拽支持
        SetupDragAndDrop();
    }

    /// <summary>
    /// 设置拖拽支持，接受 .nekograph 文件拖拽加载
    /// </summary>
    private void SetupDragAndDrop()
    {
        // 在 rootVisualElement 上监听拖拽事件
        rootVisualElement.RegisterCallback<DragEnterEvent>(OnDragEnter);
        rootVisualElement.RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
        rootVisualElement.RegisterCallback<DragPerformEvent>(OnDragPerform);
        rootVisualElement.RegisterCallback<DragLeaveEvent>(OnDragLeave);
    }

    private void OnDragEnter(DragEnterEvent evt)
    {
        if (IsNekoGraphDrag())
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            _viewContainer?.AddToClassList("drag-over");
        }
        else
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
        }
        evt.StopPropagation();
    }

    private void OnDragUpdated(DragUpdatedEvent evt)
    {
        if (IsNekoGraphDrag())
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        }
        else
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
        }
        evt.StopPropagation();
    }

    private void OnDragPerform(DragPerformEvent evt)
    {
        if (IsNekoGraphDrag())
        {
            DragAndDrop.AcceptDrag();

            var paths = DragAndDrop.paths;
            if (paths != null && paths.Length > 0)
            {
                string path = paths[0];
                if (path.EndsWith(".nekograph", StringComparison.OrdinalIgnoreCase))
                {
                    // 询问是否保存当前（如果有修改）
                    if (IsDirty)
                    {
                        int result = EditorUtility.DisplayDialogComplex("未保存的更改",
                            $"当前 Pack 有未保存的更改，是否先保存？",
                            "保存并打开", "不保存直接打开", "取消");

                        switch (result)
                        {
                            case 0: // 保存并打开
                                SilentSave();
                                break;
                            case 1: // 不保存
                                break;
                            default: // 取消
                                return;
                        }
                    }

                    // 加载新 Pack
                    LoadFromPath(path);
                    Debug.Log($"[PackWindow] 从拖拽加载: {path}");
                }
            }
        }

        _viewContainer?.RemoveFromClassList("drag-over");
        evt.StopPropagation();
    }

    private void OnDragLeave(DragLeaveEvent evt)
    {
        _viewContainer?.RemoveFromClassList("drag-over");
    }

    private bool IsNekoGraphDrag()
    {
        if (DragAndDrop.paths == null || DragAndDrop.paths.Length == 0)
            return false;

        foreach (var path in DragAndDrop.paths)
        {
            if (path.EndsWith(".nekograph", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
    
    /// <summary>
    /// 初始化默认Pack，窗口刚打开时使用
    /// </summary>
    private void InitializeDefaultPack()
    {
        _currentPack = new BasePackData
        {
            PackID = "new_pack",
            System = NodeSystem.Common,
            ReadableFrom = 100,
            WritableFrom = 1000
        };
        _currentPack.Initialize();
    }

    private void GenerateToolbar()
    {
        var toolbar = new Toolbar();

        // ===== 左侧区域喵~ =====
        var leftContainer = new VisualElement { name = "LeftContainer" };
        leftContainer.style.flexDirection = FlexDirection.Row;
        leftContainer.style.alignItems = Align.Center;

        // PackID 喵~（Label + TextField 分离，无间距）
        var packIDLabel = new Label("PackID:") { style = { marginRight = 2, alignSelf = Align.Center } };
        _packIDField = new TextField()
        {
            name = "PackIDField",
            tooltip = "Pack 的唯一 ID 喵~",
            maxLength = 64,
            value = "",
            style = { width = 100, unityTextAlign = TextAnchor.MiddleLeft }
        };
        _packIDField.RegisterValueChangedCallback(evt =>
        {
            _graphView?.SetPackID(evt.newValue);
            MarkDirty();
        });
        leftContainer.Add(packIDLabel);
        leftContainer.Add(_packIDField);

        // System 下拉框喵~（用 PopupField）
        var systemLabel = new Label("System:") { style = { marginLeft = 10, marginRight = 2, alignSelf = Align.Center } };
        _systemField = new EnumField(NodeSystem.Common)
        {
            tooltip = "Pack 类型，决定可用节点类型喵~",
            style = { width = 80 }
        };
        _systemField.RegisterValueChangedCallback(evt =>
        {
            if (_currentPack != null)
                _currentPack.System = (NodeSystem)evt.newValue;
            MarkDirty();
        });
        leftContainer.Add(systemLabel);
        leftContainer.Add(_systemField);

        // 读取/保存/新文件按钮喵~
        leftContainer.Add(new Button(NewFile) { text = "新文件", style = { marginLeft = 10, marginRight = 5 } });
        leftContainer.Add(new Button(LoadData) { text = "读取", style = { marginRight = 5 } });
        leftContainer.Add(new Button(SaveData) { text = "保存", style = { marginRight = 10 } });

        toolbar.Add(leftContainer);

        // ===== 右侧区域喵~ =====
        var rightContainer = new VisualElement { name = "RightContainer" };
        rightContainer.style.flexDirection = FlexDirection.Row;
        rightContainer.style.alignItems = Align.Center;
        rightContainer.style.flexGrow = 1;  // 占据剩余空间
        rightContainer.style.justifyContent = Justify.FlexEnd;  // 靠右对齐

        // 可读滑块喵~
        var readContainer = new VisualElement { name = "ReadableSliderContainer" };
        readContainer.style.flexDirection = FlexDirection.Row;
        readContainer.style.alignItems = Align.Center;
        readContainer.style.marginRight = 5;

        var readLabel = new Label("可读:") { style = { width = 30 } };
        _readableFromSlider = new Slider(SliderMin, SliderMax)
        {
            value = ValueToSlider(100),
            lowValue = SliderMin,
            highValue = SliderMax,
            pageSize = 0.01f
        };
        _readableFromSlider.style.width = 80;
        _readableFromSlider.tooltip = BuildAccessTooltip(100, "可读");
        _readableFromValueLabel = new Label("100") { style = { width = 30, marginLeft = 2, unityFontStyleAndWeight = FontStyle.Bold, unityTextAlign = TextAnchor.MiddleRight } };

        _readableFromSlider.RegisterValueChangedCallback(evt =>
        {
            int actualValue = SliderToValue(evt.newValue);
            _readableFromValueLabel.text = actualValue.ToString();
            _readableFromSlider.tooltip = BuildAccessTooltip(actualValue, "可读");
            if (_currentPack != null)
                _currentPack.ReadableFrom = actualValue;
            ValidateAccessConfig();
        });

        readContainer.Add(readLabel);
        readContainer.Add(_readableFromSlider);
        readContainer.Add(_readableFromValueLabel);
        rightContainer.Add(readContainer);

        // 可写滑块喵~
        var writeContainer = new VisualElement { name = "WritableSliderContainer" };
        writeContainer.style.flexDirection = FlexDirection.Row;
        writeContainer.style.alignItems = Align.Center;
        writeContainer.style.marginRight = 5;

        var writeLabel = new Label("可写:") { style = { width = 30 } };
        _writableFromSlider = new Slider(SliderMin, SliderMax)
        {
            value = ValueToSlider(1000),
            lowValue = SliderMin,
            highValue = SliderMax,
            pageSize = 0.01f
        };
        _writableFromSlider.style.width = 80;
        _writableFromSlider.tooltip = BuildAccessTooltip(1000, "可写");
        _writableFromValueLabel = new Label("1000") { style = { width = 30, marginLeft = 2, unityFontStyleAndWeight = FontStyle.Bold, unityTextAlign = TextAnchor.MiddleRight } };

        _writableFromSlider.RegisterValueChangedCallback(evt =>
        {
            int actualValue = SliderToValue(evt.newValue);
            _writableFromValueLabel.text = actualValue.ToString();
            _writableFromSlider.tooltip = BuildAccessTooltip(actualValue, "可写");
            if (_currentPack != null)
                _currentPack.WritableFrom = actualValue;
            ValidateAccessConfig();
        });

        writeContainer.Add(writeLabel);
        writeContainer.Add(_writableFromSlider);
        writeContainer.Add(_writableFromValueLabel);
        rightContainer.Add(writeContainer);

        // 预设按钮喵~
        var presetButton = new Button(() => ShowAccessPresetMenu())
        {
            text = "预设",
            tooltip = "快速选择权限预设喵~"
        };
        rightContainer.Add(presetButton);

        toolbar.Add(rightContainer);
        rootVisualElement.Add(toolbar);
    }

    #region New / Load / Save

    private void NewFile()
    {
        // 检查未保存的更改
        if (!PromptSaveIfDirty("创建新文件"))
            return;

        _currentPack = new BasePackData
        {
            PackID = "new_pack",
            System = NodeSystem.Common,
            ReadableFrom = 100,
            WritableFrom = 1000
        };
        _currentPack.Initialize();
        _currentFilePath = null;
        AssetPath = null;

        _graphView.PopulateFromPack(_currentPack);
        _packIDField.SetValueWithoutNotify(_currentPack.PackID);
        _graphView.SetPackID(_currentPack.PackID);
        _systemField.SetValueWithoutNotify(_currentPack.System);

        _readableFromSlider.SetValueWithoutNotify(ValueToSlider(_currentPack.ReadableFrom));
        _writableFromSlider.SetValueWithoutNotify(ValueToSlider(_currentPack.WritableFrom));
        _readableFromValueLabel.text = _currentPack.ReadableFrom.ToString();
        _writableFromValueLabel.text = _currentPack.WritableFrom.ToString();
        _readableFromSlider.tooltip = BuildAccessTooltip(_currentPack.ReadableFrom, "可读");
        _writableFromSlider.tooltip = BuildAccessTooltip(_currentPack.WritableFrom, "可写");
        ValidateAccessConfig();

        SetupSearchWindow(_currentPack);

        IsDirty = false;
        UpdateTitle();

        Debug.Log("[PackWindow] 创建新文件");
    }

    private void LoadData()
    {
        // 检查未保存的更改
        if (!PromptSaveIfDirty("加载文件"))
            return;

        string path = EditorUtility.OpenFilePanel("读取 Pack", "Assets/Resources", "nekograph");
        if (string.IsNullOrEmpty(path)) return;

        string json;
        try { json = File.ReadAllText(path); }
        catch (Exception e) { EditorUtility.DisplayDialog("读取失败", e.Message, "确定"); return; }

        BasePackData pack;
        try { pack = BasePackData.FromJson(json); }
        catch (Exception e) { EditorUtility.DisplayDialog("读取失败", $"JSON 格式错误：{e.Message}", "确定"); return; }

        if (pack == null) { EditorUtility.DisplayDialog("读取失败", "文件内容为空喵~", "确定"); return; }

        _currentPack = pack;
        _currentFilePath = path;

        _graphView.PopulateFromPack(pack);

        string id = !string.IsNullOrEmpty(pack.PackID) ? pack.PackID : Path.GetFileNameWithoutExtension(path);
        _packIDField.SetValueWithoutNotify(id);
        _graphView.SetPackID(id);
        _systemField.SetValueWithoutNotify(pack.System);
        
        // 同步滑块和标签喵~（使用反向映射）
        _readableFromSlider.SetValueWithoutNotify(ValueToSlider(pack.ReadableFrom));
        _writableFromSlider.SetValueWithoutNotify(ValueToSlider(pack.WritableFrom));
        _readableFromValueLabel.text = pack.ReadableFrom.ToString();
        _writableFromValueLabel.text = pack.WritableFrom.ToString();
        _readableFromSlider.tooltip = BuildAccessTooltip(pack.ReadableFrom, "可读");
        _writableFromSlider.tooltip = BuildAccessTooltip(pack.WritableFrom, "可写");
        ValidateAccessConfig();

        SetupSearchWindow(pack);

        titleContent = new GUIContent($"Pack [{id}]");
        Debug.Log($"[PackWindow] 已加载: {path}");
    }

    private void SaveData()
    {
        if (_currentPack == null)
        {
            EditorUtility.DisplayDialog("保存失败", "请先读取一个 Pack 喵~", "确定");
            return;
        }
        if (string.IsNullOrWhiteSpace(_packIDField.value))
        {
            EditorUtility.DisplayDialog("保存失败", "PackID 不能为空！", "确定");
            return;
        }

        // 如果有有效路径，直接保存；否则弹出保存对话框
        string path;
        if (HasValidAssetPath && !string.IsNullOrEmpty(_currentFilePath))
        {
            path = _currentFilePath;
        }
        else
        {
            string defaultDir = string.IsNullOrEmpty(_currentFilePath)
                ? "Assets/Resources"
                : Path.GetDirectoryName(_currentFilePath);
            string defaultName = $"{_packIDField.value}.nekograph";
            path = EditorUtility.SaveFilePanel("保存 Pack", defaultDir, defaultName, "nekograph");
            if (string.IsNullOrEmpty(path)) return;
        }

        _graphView.FlushToPack(_currentPack);
        File.WriteAllText(path, _currentPack.ToJson());
        _currentFilePath = path;

        // 更新资源路径（如果是项目内的文件）
        if (path.StartsWith(Application.dataPath) || path.Replace('\\', '/').Contains("/Assets/"))
        {
            AssetPath = path.Replace('\\', '/');
        }

        IsDirty = false;
        UpdateTitle();

        RegisterToMetaLib(path, _currentPack);
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("保存成功", $"已保存至：\n{path}", "确定");
        Debug.Log($"[PackWindow] 已保存: {path}");
    }

    #endregion

    #region SearchWindow

    private void SetupSearchWindow(BasePackData pack)
    {
        var provider = ScriptableObject.CreateInstance<NodeSearchWindow>();
        provider.Initialize(this, _graphView, pack);

        _graphView.nodeCreationRequest = context =>
        {
            var method = typeof(SearchWindow).GetMethod("Open", BindingFlags.Static | BindingFlags.Public);
            method?.MakeGenericMethod(typeof(NodeSearchWindow)).Invoke(null, new object[]
            {
                new SearchWindowContext(context.screenMousePosition), provider
            });
        };
    }

    #endregion

    #region MetaLib

    private void RegisterToMetaLib(string fullPath, BasePackData pack)
    {
        if (string.IsNullOrEmpty(pack.PackID))
        {
            EditorUtility.DisplayDialog("注册失败", "PackID 不能为空！", "好的");
            return;
        }
        string fileName = Path.GetFileNameWithoutExtension(fullPath);
        if (fileName != pack.PackID)
        {
            EditorUtility.DisplayDialog("文件名错误",
                $"文件名必须与 PackID 一致喵~\n\nPackID：'{pack.PackID}'\n文件名：'{fileName}'", "好的");
            return;
        }

        var (storageType, resourcePath) = GetStorageInfo(fullPath);
        if (MetaLib.HasMeta(pack.PackID))
        {
            var existing = MetaLib.GetMeta(pack.PackID);
            if (existing.ResourcePath != resourcePath || existing.Storage != storageType)
            {
                EditorUtility.DisplayDialog("PackID 已被占用",
                    $"PackID '{pack.PackID}' 已被 '{existing.ResourcePath}' 使用喵~", "好的");
                return;
            }
        }

        var meta = new MetaLib.MetaEntry
        {
            PackID = pack.PackID,
            Storage = storageType,
            ResourcePath = resourcePath,
            GraphType = pack.GetType().Name.Replace("PackData", ""),
            DisplayName = !string.IsNullOrEmpty(pack.DisplayName) ? pack.DisplayName : pack.PackID,
            Author = "NekoTeam",
            Version = "1.0.0"
        };
        MetaLib.Register(pack.PackID, meta);
        MetaLib.Save();
        Debug.Log($"[MetaLib] 已注册：{pack.PackID} -> {meta.ResourcePath}");
    }

    private static (MetaLib.StorageType, string) GetStorageInfo(string fullPath)
    {
        string assetsPath = Application.dataPath.Replace('\\', '/');
        fullPath = fullPath.Replace('\\', '/');
        if (fullPath.StartsWith(assetsPath))
        {
            if (fullPath.Contains("/Resources/"))
            {
                int idx = fullPath.IndexOf("/Resources/") + "/Resources/".Length;
                return (MetaLib.StorageType.Resources, Path.ChangeExtension(fullPath[idx..], null));
            }
            if (fullPath.Contains("/StreamingAssets/"))
            {
                int idx = fullPath.IndexOf("/StreamingAssets/") + "/StreamingAssets/".Length;
                return (MetaLib.StorageType.StreamingAssets, fullPath[idx..]);
            }
        }
        Debug.LogWarning($"[PackWindow] 文件不在 Resources 或 StreamingAssets 内：{fullPath}");
        return (MetaLib.StorageType.Resources, Path.GetFileNameWithoutExtension(fullPath));
    }

    #endregion

    #region Access Preset Menu

    private void ShowAccessPresetMenu()
    {
        var menu = new GenericMenu();
        
        menu.AddItem(new GUIContent("🌍 公开 (所有人可读可写)"), false, () => ApplyAccessPreset(0, 0));
        menu.AddItem(new GUIContent("👤 玩家级 (玩家可读，系统可写)"), false, () => ApplyAccessPreset(0, 1000));
        menu.AddItem(new GUIContent("🤖 AI 级 (AI 可读，系统可写)"), false, () => ApplyAccessPreset(100, 1000));
        menu.AddItem(new GUIContent("🔒 系统级 (仅系统可读写)"), false, () => ApplyAccessPreset(1000, 1000));
        menu.AddItem(new GUIContent("📖 只读玩家 (玩家只读，不可写)"), false, () => ApplyAccessPreset(0, 9999));
        
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("🔧 自定义 (手动调整滑块)"), false, null);
        
        menu.DropDown(new Rect(Event.current.mousePosition, Vector2.zero));
    }

    private void ApplyAccessPreset(int read, int write)
    {
        _readableFromSlider.SetValueWithoutNotify(ValueToSlider(read));
        _writableFromSlider.SetValueWithoutNotify(ValueToSlider(write));
        _readableFromValueLabel.text = read.ToString();
        _writableFromValueLabel.text = write.ToString();
        
        if (_currentPack != null)
        {
            _currentPack.ReadableFrom = read;
            _currentPack.WritableFrom = write;
        }
        
        // 更新 Tooltip 喵~
        _readableFromSlider.tooltip = BuildAccessTooltip(read, "可读");
        _writableFromSlider.tooltip = BuildAccessTooltip(write, "可写");
        
        ValidateAccessConfig();
    }

    /// <summary>
    /// 验证权限配置，如果可写 &lt; 可读，把数字标红喵~
    /// </summary>
    private void ValidateAccessConfig()
    {
        int read = SliderToValue(_readableFromSlider.value);
        int write = SliderToValue(_writableFromSlider.value);
        
        if (write < read)
        {
            // 标红警告喵~
            _readableFromValueLabel.style.color = new Color(1, 0.3f, 0.3f);
            _writableFromValueLabel.style.color = new Color(1, 0.3f, 0.3f);
        }
        else
        {
            // 恢复正常颜色喵~
            _readableFromValueLabel.style.color = Color.white;
            _writableFromValueLabel.style.color = Color.white;
        }
    }

    #endregion

    public BaseGraphView GetGraphView() => _graphView;

    public Vector2 ScreenToLocal(Vector2 screenPosition)
    {
        var local = rootVisualElement.ChangeCoordinatesTo(rootVisualElement.parent, screenPosition);
        return rootVisualElement.WorldToLocal(local);
    }
}
#endif
