#if UNITY_EDITOR
using System.IO;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using NekoGraph;

/// <summary>
/// VFSNode - VFS 节点编辑器 UI 喵~
/// 端口会自动根据 Data 类的 [InPort]/[OutPort] 标签生成喵~
/// </summary>
[NodeMenuItem("📁 VFS/VFS 节点", typeof(VFSNodeData))]
[NodeType(NodeSystem.VFS)]
public class VFSNode : BaseNode<VFSNodeData>
{
    private TextField _nameField;
    private TextField _extensionField;
    private TextField _dataJsonField;
    private Label _dataTypeHintLabel;
    private Label _syncStatusLabel;
    private TextField _descriptionField;
    private Toggle _enabledToggle;

    // 外联编辑器 buffer 路径
    private string _tempFilePath;
    private long _lastWriteTimeTicks;

    public VFSNode() : base() => InitializeUI();
    public VFSNode(VFSNodeData data) : base(data) => InitializeUI();

    private void InitializeUI()
    {
        UpdateTitle();
        style.width = 260;

        var foldout = new Foldout() { text = "节点配置", value = true };

        // 节点名称
        _nameField = new TextField("名称");
        _nameField.value = TypedData.Name;
        _nameField.RegisterValueChangedCallback(evt => { TypedData.Name = evt.newValue; UpdateTitle(); });
        foldout.Add(_nameField);

        // 扩展名（空=目录）
        _extensionField = new TextField("扩展名");
        _extensionField.value = TypedData.Extension;
        _extensionField.RegisterValueChangedCallback(evt =>
        {
            TypedData.Extension = evt.newValue;
            _extensionField.SetValueWithoutNotify(TypedData.Extension);
            UpdateTitle();
            RefreshDataTypeHint();
        });
        foldout.Add(_extensionField);

        // 类型提示 + DataJson 区域（只在文件节点时显示）
        _dataTypeHintLabel = new Label();
        _dataTypeHintLabel.style.color = new StyleColor(new Color(0.5f, 0.8f, 1f));
        _dataTypeHintLabel.style.fontSize = 10;
        _dataTypeHintLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
        foldout.Add(_dataTypeHintLabel);

        _dataJsonField = new TextField("DataJson") { multiline = true };
        _dataJsonField.style.minHeight = 60;
        _dataJsonField.value = TypedData.DataJson;
        _dataJsonField.RegisterValueChangedCallback(evt => TypedData.DataJson = evt.newValue);
        foldout.Add(_dataJsonField);

        // 外联编辑器按钮行
        var buttonRow = new VisualElement();
        buttonRow.style.flexDirection = FlexDirection.Row;
        buttonRow.style.marginTop = 2;

        var openBtn = new Button(OpenInExternalEditor) { text = "↗ 外部编辑器" };
        openBtn.style.flexGrow = 1;
        buttonRow.Add(openBtn);

        var reloadBtn = new Button(ReloadFromTempFile) { text = "↺ 同步" };
        reloadBtn.style.width = 50;
        buttonRow.Add(reloadBtn);

        foldout.Add(buttonRow);

        // 同步状态提示
        _syncStatusLabel = new Label();
        _syncStatusLabel.style.fontSize = 9;
        _syncStatusLabel.style.color = new StyleColor(new Color(0.6f, 0.6f, 0.6f));
        foldout.Add(_syncStatusLabel);

        RefreshDataTypeHint();

        // 描述
        _descriptionField = new TextField("描述") { multiline = true };
        _descriptionField.value = TypedData.Description;
        _descriptionField.RegisterValueChangedCallback(evt => TypedData.Description = evt.newValue);
        foldout.Add(_descriptionField);

        // 启用状态
        _enabledToggle = new Toggle("已启用");
        _enabledToggle.value = TypedData.IsEnabled;
        _enabledToggle.RegisterValueChangedCallback(evt => TypedData.IsEnabled = evt.newValue);
        foldout.Add(_enabledToggle);

        extensionContainer.Add(foldout);
        RefreshExpandedState();

        // 注册轮询（检测外部文件变化）
        EditorApplication.update += PollTempFile;
    }

    // ─────────────────────────────────────────────────────────
    //  外联编辑器
    // ─────────────────────────────────────────────────────────

    private void OpenInExternalEditor()
    {
        // 写临时文件到项目 Temp 目录
        string tempDir = Path.Combine(Application.dataPath, "..", "Temp", "VFSEdit");
        Directory.CreateDirectory(tempDir);
        _tempFilePath = Path.Combine(tempDir, $"VFSNode_{TypedData.NodeID}.json");

        // 若 DataJson 为空，写入类型模板（如有注册）
        string content = TypedData.DataJson;
        if (string.IsNullOrWhiteSpace(content))
            content = BuildTemplate();

        File.WriteAllText(_tempFilePath, content, System.Text.Encoding.UTF8);
        _lastWriteTimeTicks = File.GetLastWriteTime(_tempFilePath).Ticks;

        // 打开系统默认 .json 编辑器
        Process.Start(new ProcessStartInfo(_tempFilePath) { UseShellExecute = true });
        _syncStatusLabel.text = $"已打开：{Path.GetFileName(_tempFilePath)}（等待编辑）";
    }

    private void ReloadFromTempFile()
    {
        if (string.IsNullOrEmpty(_tempFilePath) || !File.Exists(_tempFilePath))
        {
            _syncStatusLabel.text = "尚未打开外部编辑器";
            return;
        }
        string json = File.ReadAllText(_tempFilePath, System.Text.Encoding.UTF8);
        TypedData.DataJson = json;
        _dataJsonField.SetValueWithoutNotify(json);
        _lastWriteTimeTicks = File.GetLastWriteTime(_tempFilePath).Ticks;
        _syncStatusLabel.text = $"已同步 {System.DateTime.Now:HH:mm:ss}";
    }

    /// <summary>
    /// 每帧轮询临时文件是否被外部编辑器修改，有变化则自动回写喵~
    /// </summary>
    private void PollTempFile()
    {
        if (string.IsNullOrEmpty(_tempFilePath) || !File.Exists(_tempFilePath)) return;
        long current = File.GetLastWriteTime(_tempFilePath).Ticks;
        if (current == _lastWriteTimeTicks) return;

        _lastWriteTimeTicks = current;
        string json = File.ReadAllText(_tempFilePath, System.Text.Encoding.UTF8);
        TypedData.DataJson = json;
        _dataJsonField.SetValueWithoutNotify(json);
        _syncStatusLabel.text = $"自动同步 {System.DateTime.Now:HH:mm:ss}";
    }

    /// <summary>
    /// 根据注册的 DataType 生成 JSON 模板（字段全填默认值）喵~
    /// </summary>
    private string BuildTemplate()
    {
        var dataType = ExeRegistry.GetDataType(TypedData.Extension);
        if (dataType == null) return "{}";
        try
        {
            var instance = System.Activator.CreateInstance(dataType);
            return Newtonsoft.Json.JsonConvert.SerializeObject(instance,
                Newtonsoft.Json.Formatting.Indented);
        }
        catch { return "{}"; }
    }

    // ─────────────────────────────────────────────────────────
    //  辅助方法
    // ─────────────────────────────────────────────────────────

    private void UpdateTitle()
    {
        title = TypedData.IsDirectory
            ? $"📂 {TypedData.Name}"
            : $"📄 {TypedData.Name}{TypedData.Extension}";
    }

    private void RefreshDataTypeHint()
    {
        if (_dataTypeHintLabel == null) return;
        bool isFile = TypedData.IsFile;
        _dataJsonField.style.display        = isFile ? DisplayStyle.Flex : DisplayStyle.None;
        _dataTypeHintLabel.style.display    = isFile ? DisplayStyle.Flex : DisplayStyle.None;
        _syncStatusLabel.style.display      = isFile ? DisplayStyle.Flex : DisplayStyle.None;

        if (!isFile) return;

        var dataType = ExeRegistry.GetDataType(TypedData.Extension);
        if (dataType != null)
        {
            var fields = dataType.GetFields(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            var fieldNames = string.Join(", ",
                System.Array.ConvertAll(fields, f => f.Name));
            _dataTypeHintLabel.text = $"类型：{dataType.Name}  字段：{fieldNames}";
        }
        else
        {
            _dataTypeHintLabel.text = "未注册类型（自由 JSON）";
        }
    }

    public override void UpdateData()
    {
        TypedData.Name        = _nameField.value;
        TypedData.Extension   = _extensionField.value;
        TypedData.DataJson    = _dataJsonField.value;
        TypedData.Description = _descriptionField.value;
        TypedData.IsEnabled   = _enabledToggle.value;
    }

    ~VFSNode()
    {
        EditorApplication.update -= PollTempFile;
    }
}
#endif
