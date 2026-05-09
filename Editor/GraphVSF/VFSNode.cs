#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.UIElements;
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
    private PopupField<string> _contentKindField;
    private PopupField<string> _contentSourceField;
    private TextField _inlineTextField;
    private TextField _referencePathField;
    private ObjectField _referenceObjectField;
    private Label _dataTypeHintLabel;
    private Label _syncStatusLabel;
    private Label _validationLabel;
    private TextField _descriptionField;
    private Toggle _enabledToggle;
    private VisualElement _externalEditorRow;

    // 外联编辑器 buffer 路径
    private string _tempFilePath;
    private long _lastWriteTimeTicks;
    private static readonly List<string> ContentKindOptions = new() { "Json", "Csv", "ScriptableObject", "Nekograph" };
    private static readonly List<string> ContentSourceOptions = new() { "直接输入", "引用资源" };

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
            RefreshContentUI();
            RefreshDataTypeHint();
        });
        foldout.Add(_extensionField);

        _contentKindField = new PopupField<string>("内容类型", ContentKindOptions, ToKindOption(TypedData.GetEffectiveContentKind()));
        _contentKindField.RegisterValueChangedCallback(evt =>
        {
            TypedData.ContentKind = FromKindOption(evt.newValue);
            if (TypedData.ContentKind == VFSContentKind.UnityObject)
            {
                TypedData.ContentSource = VFSContentSource.Reference;
                _contentSourceField.SetValueWithoutNotify(ToSourceOption(TypedData.ContentSource));
            }
            RestoreReferenceObjectField();
            RefreshContentUI();
            RefreshDataTypeHint();
        });
        foldout.Add(_contentKindField);

        _contentSourceField = new PopupField<string>("内容来源", ContentSourceOptions, ToSourceOption(TypedData.GetEffectiveContentSource()));
        _contentSourceField.RegisterValueChangedCallback(evt =>
        {
            TypedData.ContentSource = FromSourceOption(evt.newValue);
            if (TypedData.GetEffectiveContentKind() == VFSContentKind.UnityObject)
            {
                TypedData.ContentSource = VFSContentSource.Reference;
                _contentSourceField.SetValueWithoutNotify(ToSourceOption(TypedData.ContentSource));
            }
            RestoreReferenceObjectField();
            RefreshContentUI();
        });
        foldout.Add(_contentSourceField);

        // 类型提示 + 内容区域（只在文件节点时显示）
        _dataTypeHintLabel = new Label();
        _dataTypeHintLabel.style.color = new StyleColor(new Color(0.5f, 0.8f, 1f));
        _dataTypeHintLabel.style.fontSize = 10;
        _dataTypeHintLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
        foldout.Add(_dataTypeHintLabel);

        _inlineTextField = new TextField("内嵌内容") { multiline = true };
        _inlineTextField.style.minHeight = 60;
        _inlineTextField.value = TypedData.GetInlineText();
        _inlineTextField.RegisterValueChangedCallback(evt =>
        {
            TypedData.InlineText = evt.newValue;
        });
        _inlineTextField.RegisterCallback<FocusOutEvent>(_ => RefreshValidationMessage());
        foldout.Add(_inlineTextField);

        _referencePathField = new TextField("引用路径");
        _referencePathField.value = TypedData.ReferencePath;
        _referencePathField.RegisterValueChangedCallback(evt => TypedData.ReferencePath = evt.newValue);
        _referencePathField.SetEnabled(false);
        foldout.Add(_referencePathField);

        _referenceObjectField = new ObjectField("引用文件")
        {
            allowSceneObjects = false,
            objectType = typeof(UnityEngine.Object)
        };
        _referenceObjectField.RegisterValueChangedCallback(evt => OnReferenceObjectChanged(evt.newValue));
        RestoreReferenceObjectField();
        foldout.Add(_referenceObjectField);

        // 外联编辑器按钮行
        _externalEditorRow = new VisualElement();
        _externalEditorRow.style.flexDirection = FlexDirection.Row;
        _externalEditorRow.style.marginTop = 2;

        var openBtn = new Button(OpenInExternalEditor) { text = "↗ 外部编辑器" };
        openBtn.style.flexGrow = 1;
        _externalEditorRow.Add(openBtn);

        var reloadBtn = new Button(ReloadFromTempFile) { text = "↺ 同步" };
        reloadBtn.style.width = 50;
        _externalEditorRow.Add(reloadBtn);

        foldout.Add(_externalEditorRow);

        // 同步状态提示
        _syncStatusLabel = new Label();
        _syncStatusLabel.style.fontSize = 9;
        _syncStatusLabel.style.color = new StyleColor(new Color(0.6f, 0.6f, 0.6f));
        foldout.Add(_syncStatusLabel);

        _validationLabel = new Label();
        _validationLabel.style.fontSize = 9;
        _validationLabel.style.whiteSpace = WhiteSpace.Normal;
        foldout.Add(_validationLabel);

        RefreshContentUI();
        RestoreReferenceObjectField();
        RefreshDataTypeHint();
        RefreshValidationMessage();

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

        // 根据 ContentKind 决定文件后缀
        string fileExtension = TypedData.GetEffectiveContentKind() switch
        {
            VFSContentKind.Csv => ".csv",
            VFSContentKind.UnityObject => ".json",
            VFSContentKind.Nekograph => ".nekograph",
            _ => ".json"
        };
        _tempFilePath = Path.Combine(tempDir, $"VFSNode_{TypedData.NodeID}{fileExtension}");

        // 若 DataJson 为空，写入类型模板（如有注册）
        string content = TypedData.GetInlineText();
        if (string.IsNullOrWhiteSpace(content))
            content = BuildTemplate();

        File.WriteAllText(_tempFilePath, content, System.Text.Encoding.UTF8);
        _lastWriteTimeTicks = File.GetLastWriteTime(_tempFilePath).Ticks;

        if (TypedData.GetEffectiveContentSource() == VFSContentSource.Reference)
        {
            OpenReferencedAsset();
            return;
        }

        // 打开系统默认文本编辑器
        Process.Start(new ProcessStartInfo(_tempFilePath) { UseShellExecute = true });
        _syncStatusLabel.text = $"已打开：{Path.GetFileName(_tempFilePath)}（等待编辑后可回写）";
    }

    private void ReloadFromTempFile()
    {
        if (string.IsNullOrEmpty(_tempFilePath) || !File.Exists(_tempFilePath))
        {
            _syncStatusLabel.text = TypedData.GetEffectiveContentSource() == VFSContentSource.Reference
                ? "引用模式不使用临时文件回写"
                : "尚未打开外部编辑器";
            return;
        }
        string json = File.ReadAllText(_tempFilePath, System.Text.Encoding.UTF8);
        TypedData.InlineText = json;
        _inlineTextField.SetValueWithoutNotify(json);
        _lastWriteTimeTicks = File.GetLastWriteTime(_tempFilePath).Ticks;
        _syncStatusLabel.text = $"已同步 {System.DateTime.Now:HH:mm:ss}";
        RefreshValidationMessage();
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
        TypedData.InlineText = json;
        _inlineTextField.SetValueWithoutNotify(json);
        _syncStatusLabel.text = $"自动同步 {System.DateTime.Now:HH:mm:ss}";
        RefreshValidationMessage();
    }

    /// <summary>
    /// 根据 DataType 生成对应的内容模板喵~
    /// </summary>
    private string BuildTemplate()
    {
        var dataType = ExeRegistry.GetDataType(TypedData.Extension);

        if (TypedData.GetEffectiveContentKind() == VFSContentKind.Csv)
        {
            // ChoiceData: 单列选项列表
            if (dataType != null && dataType.Name == "ChoiceData")
                return "选项1\n选项2\n选项3";

            // 其他 CSV 类型的通用模板
            return "id,text\nsample,hello";
        }

        if (TypedData.GetEffectiveContentKind() == VFSContentKind.UnityObject)
            return string.Empty;

        if (TypedData.GetEffectiveContentKind() == VFSContentKind.Nekograph)
        {
            var pack = new BasePackData
            {
                PackID = string.IsNullOrWhiteSpace(TypedData.Name) ? "run_stage" : TypedData.Name,
                DisplayName = TypedData.Name
            };
            pack.Initialize();
            return pack.ToJson();
        }

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
        title = IsDirectoryNode()
            ? $"📂 {TypedData.Name}"
            : $"📄 {TypedData.Name}{TypedData.Extension}";
    }

    private void RefreshDataTypeHint()
    {
        if (_dataTypeHintLabel == null) return;
        bool isFile = IsFileNode();
        _dataTypeHintLabel.style.display    = isFile ? DisplayStyle.Flex : DisplayStyle.None;
        _syncStatusLabel.style.display      = isFile ? DisplayStyle.Flex : DisplayStyle.None;

        if (!isFile) return;

        // 自动推断 ContentKind（从 DataType 的 [VFSContentKind] Attribute）
        AutoDetectContentKind();

        var dataType = ExeRegistry.GetDataType(TypedData.Extension);
        string kindText = $"载荷：{TypedData.GetEffectiveContentKind()} / {TypedData.GetEffectiveContentSource()}";
        if (dataType != null)
        {
            var fields = dataType.GetFields(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            var fieldNames = string.Join(", ",
                System.Array.ConvertAll(fields, f => f.Name));
            _dataTypeHintLabel.text = $"{kindText}\n类型：{dataType.Name}  字段：{fieldNames}";
        }
        else
        {
            _dataTypeHintLabel.text = $"{kindText}\n未注册类型（自由载荷）";
        }
    }

    /// <summary>
    /// 根据 EXEHandler 注册的 DataType 上的 [VFSContentKind] Attribute 自动推断 ContentKind
    /// </summary>
    private void AutoDetectContentKind()
    {
        var dataType = ExeRegistry.GetDataType(TypedData.Extension);
        if (dataType == null) return;

        var detectedKind = ExeRegistry.GetContentKindFromDataType(dataType);
        if (detectedKind.HasValue)
        {
            TypedData.ContentKind = detectedKind.Value;
            _contentKindField.SetValueWithoutNotify(ToKindOption(TypedData.ContentKind));
        }
    }

    public override void UpdateData()
    {
        TypedData.Name          = _nameField.value;
        TypedData.Extension     = _extensionField.value;
        TypedData.ContentKind   = FromKindOption(_contentKindField.value);
        TypedData.ContentSource = FromSourceOption(_contentSourceField.value);
        TypedData.InlineText    = _inlineTextField.value;
        TypedData.Description   = _descriptionField.value;
        TypedData.IsEnabled     = _enabledToggle.value;
        RefreshValidationMessage();
    }

    ~VFSNode()
    {
        EditorApplication.update -= PollTempFile;
    }

    private void RefreshContentUI()
    {
        bool isFile = IsFileNode();
        bool isInlineText = isFile &&
                            TypedData.GetEffectiveContentKind() != VFSContentKind.UnityObject &&
                            TypedData.GetEffectiveContentSource() == VFSContentSource.Inline;
        bool isReference = isFile && TypedData.GetEffectiveContentSource() == VFSContentSource.Reference;

        _contentKindField.style.display = isFile ? DisplayStyle.Flex : DisplayStyle.None;
        _contentSourceField.style.display = isFile ? DisplayStyle.Flex : DisplayStyle.None;
        _inlineTextField.style.display = isInlineText ? DisplayStyle.Flex : DisplayStyle.None;
        _referencePathField.style.display = isReference ? DisplayStyle.Flex : DisplayStyle.None;
        _referenceObjectField.style.display = isReference ? DisplayStyle.Flex : DisplayStyle.None;
        _externalEditorRow.style.display = isInlineText ? DisplayStyle.Flex : DisplayStyle.None;
        _validationLabel.style.display = isFile ? DisplayStyle.Flex : DisplayStyle.None;
        if (isReference)
        {
            _syncStatusLabel.text = "引用模式：打开的是原始资源文件，不走临时回写";
        }
        else if (isInlineText && string.IsNullOrWhiteSpace(_syncStatusLabel.text))
        {
            _syncStatusLabel.text = "直接输入模式：可用外部编辑器临时编辑后回写";
        }
        if (isReference)
            RestoreReferenceObjectField();
    }

    private void OnReferenceObjectChanged(UnityEngine.Object value)
    {
        if (value == null)
        {
            TypedData.AssetGuid = "";
            TypedData.AssetPath = "";
            TypedData.ReferencePath = "";
            _referencePathField.SetValueWithoutNotify("");
            if (TypedData.GetEffectiveContentKind() == VFSContentKind.UnityObject)
                TypedData.UnityObjectTypeName = "";
            RefreshValidationMessage();
            return;
        }

        string assetPath = AssetDatabase.GetAssetPath(value);
        if (!ValidateReferenceObject(value, assetPath, out var validationMessage))
        {
            _referenceObjectField.SetValueWithoutNotify(null);
            TypedData.AssetGuid = "";
            TypedData.AssetPath = "";
            TypedData.ReferencePath = "";
            TypedData.UnityObjectTypeName = "";
            _referencePathField.SetValueWithoutNotify("");
            SetValidationMessage(validationMessage, false);
            return;
        }

        TypedData.AssetPath = assetPath ?? "";
        TypedData.AssetGuid = string.IsNullOrWhiteSpace(assetPath) ? "" : AssetDatabase.AssetPathToGUID(assetPath);
        TypedData.UnityObjectTypeName = TypedData.GetEffectiveContentKind() == VFSContentKind.UnityObject
            ? value.GetType().AssemblyQualifiedName
            : "";

        if (TryConvertAssetPathToReferencePath(assetPath, out var resolvedReferencePath))
        {
            TypedData.ReferencePath = resolvedReferencePath;
            _referencePathField.SetValueWithoutNotify(resolvedReferencePath);
        }
        else if (!string.IsNullOrWhiteSpace(assetPath))
        {
            TypedData.ReferencePath = assetPath;
            _referencePathField.SetValueWithoutNotify(assetPath);
        }

        SetValidationMessage(validationMessage, true);
    }

    private void RestoreReferenceObjectField()
    {
        var objectType = ResolveReferenceObjectType();
        _referenceObjectField.objectType = objectType ?? typeof(UnityEngine.Object);

        if (!string.IsNullOrWhiteSpace(TypedData.AssetPath))
        {
            var asset = AssetDatabase.LoadAssetAtPath(TypedData.AssetPath, _referenceObjectField.objectType);
            if (asset != null)
            {
                _referenceObjectField.SetValueWithoutNotify(asset);
            }
        }
    }

    private Type ResolveReferenceObjectType()
    {
        if (TypedData.GetEffectiveContentKind() != VFSContentKind.UnityObject)
            return typeof(DefaultAsset);

        var dataType = ExeRegistry.GetDataType(TypedData.Extension);
        if (dataType != null && typeof(UnityEngine.Object).IsAssignableFrom(dataType))
            return dataType;

        return ResolveUnityObjectType();
    }

    private Type ResolveUnityObjectType()
    {
        var dataType = ExeRegistry.GetDataType(TypedData.Extension);
        if (dataType != null && typeof(UnityEngine.Object).IsAssignableFrom(dataType))
            return dataType;

        if (string.IsNullOrWhiteSpace(TypedData.UnityObjectTypeName))
            return typeof(UnityEngine.Object);

        var objectType = Type.GetType(TypedData.UnityObjectTypeName);
        return objectType != null && typeof(UnityEngine.Object).IsAssignableFrom(objectType)
            ? objectType
            : typeof(UnityEngine.Object);
    }

    private static bool TryConvertAssetPathToReferencePath(string assetPath, out string referencePath)
    {
        referencePath = null;
        if (string.IsNullOrWhiteSpace(assetPath))
            return false;

        var normalized = assetPath.Replace('\\', '/');
        const string resourcesMarker = "/Resources/";
        int resourcesIndex = normalized.IndexOf(resourcesMarker, StringComparison.OrdinalIgnoreCase);
        if (resourcesIndex >= 0)
        {
            string relative = normalized[(resourcesIndex + resourcesMarker.Length)..];
            referencePath = Path.ChangeExtension(relative, null)?.Replace('\\', '/');
            return !string.IsNullOrWhiteSpace(referencePath);
        }

        const string streamingMarker = "/StreamingAssets/";
        int streamingIndex = normalized.IndexOf(streamingMarker, StringComparison.OrdinalIgnoreCase);
        if (streamingIndex >= 0)
        {
            referencePath = normalized[(streamingIndex + streamingMarker.Length)..];
            return !string.IsNullOrWhiteSpace(referencePath);
        }

        return false;
    }

    private void OpenReferencedAsset()
    {
        string assetPath = TypedData.AssetPath;
        if (string.IsNullOrWhiteSpace(assetPath) && !string.IsNullOrWhiteSpace(TypedData.ReferencePath))
            VFSContentResolver.TryResolveReferenceFilePath(TypedData.ReferencePath, TypedData.AssetPath, out assetPath);

        if (string.IsNullOrWhiteSpace(assetPath))
        {
            _syncStatusLabel.text = "引用资源未定位到真实文件";
            return;
        }

        if (assetPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
        {
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (asset != null)
            {
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
                _syncStatusLabel.text = $"已定位资源：{Path.GetFileName(assetPath)}";
                return;
            }
        }

        if (File.Exists(assetPath))
        {
            Process.Start(new ProcessStartInfo(assetPath) { UseShellExecute = true });
            _syncStatusLabel.text = $"已打开源文件：{Path.GetFileName(assetPath)}";
            return;
        }

        _syncStatusLabel.text = $"找不到引用文件：{assetPath}";
    }

    private static string ToKindOption(VFSContentKind kind)
    {
        return kind switch
        {
            VFSContentKind.Csv => "Csv",
            VFSContentKind.UnityObject => "ScriptableObject",
            VFSContentKind.Nekograph => "Nekograph",
            _ => "Json"
        };
    }

    private static VFSContentKind FromKindOption(string option)
    {
        return option switch
        {
            "Csv" => VFSContentKind.Csv,
            "ScriptableObject" => VFSContentKind.UnityObject,
            "Nekograph" => VFSContentKind.Nekograph,
            _ => VFSContentKind.Json
        };
    }

    private static string ToSourceOption(VFSContentSource source)
    {
        return source == VFSContentSource.Reference ? "引用资源" : "直接输入";
    }

    private static VFSContentSource FromSourceOption(string option)
    {
        return option == "引用资源" ? VFSContentSource.Reference : VFSContentSource.Inline;
    }

    private bool IsFileNode()
    {
        return !string.IsNullOrWhiteSpace(_extensionField?.value ?? TypedData.Extension);
    }

    private bool IsDirectoryNode()
    {
        return !IsFileNode();
    }

    private bool ValidateReferenceObject(UnityEngine.Object value, string assetPath, out string message)
    {
        if (TypedData.GetEffectiveContentKind() == VFSContentKind.UnityObject)
        {
            var requiredType = ResolveReferenceObjectType();
            if (requiredType != null && requiredType != typeof(UnityEngine.Object) && !requiredType.IsInstanceOfType(value))
            {
                message = $"引用类型不匹配，需要 {requiredType.Name}，实际是 {value.GetType().Name}";
                return false;
            }

            message = $"UnityObject 合法：{value.name}";
            return true;
        }

        if (!IsSupportedTextReferenceAsset(assetPath, out var textLoadMessage))
        {
            message = textLoadMessage;
            return false;
        }

        if (!VFSContentResolver.TryLoadReferencedText(GetReferencePathFromAsset(assetPath), assetPath, out var text))
        {
            message = $"无法读取文本资源：{Path.GetFileName(assetPath)}";
            return false;
        }

        return ValidateTextPayload(text, assetPath, out message);
    }

    private bool ValidateTextPayload(string text, string assetPath, out string message)
    {
        switch (TypedData.GetEffectiveContentKind())
        {
            case VFSContentKind.Json:
                try
                {
                    JToken.Parse(text);
                    message = $"JSON 合法：{Path.GetFileName(assetPath)}";
                    return true;
                }
                catch (JsonReaderException ex)
                {
                    message = $"JSON 非法：line {ex.LineNumber}, col {ex.LinePosition} - {ex.Message}";
                    return false;
                }

            case VFSContentKind.Csv:
                return ValidateCsv(text, assetPath, out message);

            case VFSContentKind.Nekograph:
                try
                {
                    var pack = BasePackData.FromJson(text);
                    if (pack == null)
                    {
                        message = $".nekograph 非法：{Path.GetFileName(assetPath)} 解析为空";
                        return false;
                    }

                    message = $".nekograph 合法：{pack.PackID} ({pack.Nodes?.Count ?? 0} nodes)";
                    return true;
                }
                catch (Exception ex)
                {
                    message = $".nekograph 非法：{ex.Message}";
                    return false;
                }

            default:
                message = "未定义的文本载荷类型";
                return false;
        }
    }

    private bool ValidateCsv(string text, string assetPath, out string message)
    {
        var rows = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        int expectedColumns = -1;

        for (int rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            var row = rows[rowIndex];
            if (string.IsNullOrEmpty(row))
                continue;

            if (!TryParseCsvRow(row, out var columnCount, out var errorColumn, out var error))
            {
                message = $"CSV 非法：line {rowIndex + 1}, col {errorColumn} - {error}";
                return false;
            }

            if (expectedColumns < 0)
            {
                expectedColumns = columnCount;
            }
            else if (columnCount != expectedColumns)
            {
                message = $"CSV 非法：line {rowIndex + 1} 列数不一致，expected {expectedColumns}, actual {columnCount}";
                return false;
            }
        }

        message = $"CSV 合法：{Path.GetFileName(assetPath)}";
        return true;
    }

    private bool TryParseCsvRow(string row, out int columnCount, out int errorColumn, out string error)
    {
        columnCount = 1;
        errorColumn = 0;
        error = "";
        bool inQuotes = false;

        for (int i = 0; i < row.Length; i++)
        {
            char ch = row[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < row.Length && row[i + 1] == '"')
                {
                    i++;
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (ch == ',' && !inQuotes)
            {
                columnCount++;
                continue;
            }
        }

        if (inQuotes)
        {
            errorColumn = row.Length;
            error = "未闭合的引号";
            return false;
        }

        return true;
    }

    private void RefreshValidationMessage()
    {
        if (!IsFileNode())
        {
            SetValidationMessage("", true);
            return;
        }

        if (TypedData.GetEffectiveContentSource() == VFSContentSource.Inline)
        {
            if (TypedData.GetEffectiveContentKind() == VFSContentKind.UnityObject)
            {
                SetValidationMessage("ScriptableObject 仅支持引用资源", false);
                return;
            }

            if (ValidateTextPayload(TypedData.GetInlineText(), $"{TypedData.Name}{TypedData.Extension}", out var inlineMessage))
            {
                SetValidationMessage(inlineMessage, true);
            }
            else
            {
                SetValidationMessage(inlineMessage, false);
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(TypedData.AssetPath) && string.IsNullOrWhiteSpace(TypedData.ReferencePath))
        {
            SetValidationMessage("尚未绑定引用资源", false);
            return;
        }

        SetValidationMessage("引用资源待校验", true);
    }

    private void SetValidationMessage(string message, bool ok)
    {
        _validationLabel.text = message;
        _validationLabel.style.color = new StyleColor(ok ? new Color(0.35f, 0.8f, 0.45f) : new Color(1f, 0.45f, 0.4f));
    }

    private void ValidateReferencePathInput()
    {
        RefreshValidationMessage();
    }

    private static bool IsSupportedTextReferenceAsset(string assetPath, out string message)
    {
        message = "";
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            message = "引用资源没有有效路径";
            return false;
        }

        var normalized = assetPath.Replace('\\', '/');
        bool underResources = normalized.Contains("/Resources/", StringComparison.OrdinalIgnoreCase);
        bool underStreaming = normalized.Contains("/StreamingAssets/", StringComparison.OrdinalIgnoreCase);
        if (!underResources && !underStreaming)
        {
            message = "文本引用只支持 Resources 或 StreamingAssets";
            return false;
        }

        string extension = Path.GetExtension(normalized);
        if (string.IsNullOrWhiteSpace(extension))
        {
            message = "文本引用缺少文件后缀";
            return false;
        }

        if (!string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".nekograph", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase))
        {
            message = $"不支持的文本后缀：{extension}";
            return false;
        }

        return true;
    }

    private static string GetReferencePathFromAsset(string assetPath)
    {
        return TryConvertAssetPathToReferencePath(assetPath, out var referencePath) ? referencePath : "";
    }
}
#endif
