#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using NekoGraph;

/// <summary>
/// PostOffice 发送节点 - 在流程图任意环节发送 TriggerEvent 事件喵~
///
/// 策划可以在右键菜单中找到：📮 发送事件 (Post)
/// 选择后可以在双级下拉菜单中选择任意已注册的事件（包括枚举和特性定义的）喵~
///
/// 【双级下拉菜单重构版】
/// 先选分类，再选事件，用户体验更佳喵~
/// </summary>
[NodeMenuItem("📮 发送事件 (Post)", typeof(PostEventNodeData))]
[NodeType(NodeSystem.Common)]
public class PostEventNode : BaseNode<PostEventNodeData>
{
    private PopupField<string> _categoryDropdown;      // 分类选择下拉框喵~
    private PopupField<string> _eventDropdown;         // 事件选择下拉框喵~
    private Label _protocolLabel;
    private TextField _payloadField;
    private VisualElement _payloadContainer;

    public PostEventNode() : base()
    {
        InitializeUI();
    }

    public PostEventNode(PostEventNodeData data) : base(data)
    {
        InitializeUI();
    }

    private void InitializeUI()
    {
        title = "📮 发送事件";
        titleContainer.style.backgroundColor = new Color(0.6f, 0.3f, 0.6f); // 紫色系
        style.width = 280;

        // --- 配置区域 ---
        var foldout = new Foldout() { text = "事件配置", value = true };

        // 1. 获取所有分类和当前事件的分类喵~
        var categories = TriggerRegistryInfo.GetAllCategories();
        string currentCategory = categories[0]; // 默认使用第一个分类喵~

        // 如果已有事件名，获取其分类
        if (!string.IsNullOrEmpty(TypedData.EventName) || TypedData.Event != TriggerEvent.GameStarted)
        {
            string eventName = string.IsNullOrEmpty(TypedData.EventName) 
                ? TypedData.Event.ToString() 
                : TypedData.EventName;
            
            string categoryFromEvent = TriggerRegistryInfo.GetCategoryFromEventName(eventName);
            if (categories.Contains(categoryFromEvent))
            {
                currentCategory = categoryFromEvent;
            }
        }

        // 2. 分类选择下拉框喵~
        _categoryDropdown = new PopupField<string>("分类", categories, currentCategory);
        _categoryDropdown.RegisterValueChangedCallback(evt =>
        {
            // 分类改变时，更新事件下拉框
            UpdateEventDropdown(evt.newValue);
        });
        foldout.Add(_categoryDropdown);

        // 3. 事件类型下拉框（根据分类动态加载）喵~
        var eventChoices = TriggerRegistryInfo.GetEventsInCategory(currentCategory);
        if (eventChoices.Count == 0)
        {
            eventChoices.Add("None"); // 默认选项
        }

        // 获取当前事件的显示名，如果不在列表中则使用第一个选项
        string currentDisplayName = eventChoices[0]; // 默认使用第一个
        if (!string.IsNullOrEmpty(TypedData.EventName) || TypedData.Event != TriggerEvent.GameStarted)
        {
            string eventName = string.IsNullOrEmpty(TypedData.EventName) 
                ? TypedData.Event.ToString() 
                : TypedData.EventName;
            
            string displayNameFromEvent = TriggerRegistryInfo.GetDisplayNameFromEventName(eventName);
            if (eventChoices.Contains(displayNameFromEvent))
            {
                currentDisplayName = displayNameFromEvent;
            }
        }

        _eventDropdown = new PopupField<string>("事件", eventChoices, currentDisplayName);
        _eventDropdown.RegisterValueChangedCallback(evt =>
        {
            // 通过显示名找回事件名
            string eventName = TriggerRegistryInfo.GetEventNameFromDisplayName(evt.newValue);
            TypedData.SetEventName(eventName);
            
            // 更新协议显示和 Payload 输入框
            var meta = TypedData.GetMeta();
            if (meta != null)
            {
                UpdateProtocolUI(meta);
                RebuildPayloadField(meta);
            }
        });
        foldout.Add(_eventDropdown);

        // 4. 协议提示标签喵~
        _protocolLabel = new Label();
        _protocolLabel.style.fontSize = 10;
        _protocolLabel.style.marginTop = 5;
        _protocolLabel.style.color = new Color(0.8f, 0.8f, 1f);
        _protocolLabel.style.whiteSpace = WhiteSpace.Normal;
        foldout.Add(_protocolLabel);

        // 5. Payload 输入区域喵~
        _payloadContainer = new VisualElement();
        foldout.Add(_payloadContainer);

        extensionContainer.Add(foldout);

        // 初始化协议显示和 Payload 输入框喵~
        var currentMeta = TypedData.GetMeta();
        if (currentMeta != null)
        {
            UpdateProtocolUI(currentMeta);
            RebuildPayloadField(currentMeta);
        }

        RefreshExpandedState();
    }

    /// <summary>
    /// 更新事件下拉框的选项喵~
    /// </summary>
    private void UpdateEventDropdown(string category)
    {
        var eventChoices = TriggerRegistryInfo.GetEventsInCategory(category);
        if (eventChoices.Count == 0)
        {
            eventChoices.Add("None");
        }

        // 先更新 choices，再设置 value
        _eventDropdown.choices = eventChoices;

        // 选择第一个事件并更新数据
        if (eventChoices.Count > 0)
        {
            string eventName = TriggerRegistryInfo.GetEventNameFromDisplayName(eventChoices[0]);
            TypedData.SetEventName(eventName);
            // 使用 SetValueWithoutNotify 避免触发 ValueChangedCallback 导致重复刷新喵~
            _eventDropdown.SetValueWithoutNotify(eventChoices[0]);
            
            // 更新协议显示和 Payload 输入框
            var meta = TypedData.GetMeta();
            if (meta != null)
            {
                UpdateProtocolUI(meta);
                RebuildPayloadField(meta);
            }
        }
    }

    /// <summary>
    /// 更新协议显示信息喵~
    /// </summary>
    private void UpdateProtocolUI(TriggerRegistry.TriggerMeta meta)
    {
        _protocolLabel.text = $"📜 协议：{meta.Info.Protocol}\nℹ️ {meta.Info.Tooltip ?? "在流程中发送指定全局事件喵~"}";
    }

    /// <summary>
    /// 根据协议重建 Payload 输入框喵~
    /// </summary>
    private void RebuildPayloadField(TriggerRegistry.TriggerMeta meta)
    {
        _payloadContainer.Clear();
        _payloadField = null;

        // None 协议不需要 Payload 输入喵~
        if (meta.Info.Protocol == EventProtocol.None)
        {
            var infoLabel = new Label("ℹ️ 此事件不需要参数");
            infoLabel.style.fontSize = 9;
            infoLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
            _payloadContainer.Add(infoLabel);
            return;
        }

        // Entity 协议需要特殊处理喵~
        if (meta.Info.Protocol == EventProtocol.Entity)
        {
            var infoLabel = new Label("ℹ️ Entity 类型事件将从上游节点自动获取 Payload");
            infoLabel.style.fontSize = 9;
            infoLabel.style.color = new Color(1f, 1f, 0.3f);
            _payloadContainer.Add(infoLabel);
            return;
        }

        // 其他协议创建文本输入框喵~
        var field = new TextField("参数值")
        {
            value = TypedData.PayloadValue ?? "",
            tooltip = GetProtocolTooltip(meta.Info.Protocol)
        };

        field.RegisterValueChangedCallback(evt =>
        {
            TypedData.PayloadValue = evt.newValue;
        });

        _payloadField = field;
        _payloadContainer.Add(field);

        // 添加示例提示喵~
        var exampleLabel = new Label(GetProtocolExample(meta.Info.Protocol));
        exampleLabel.style.fontSize = 8;
        exampleLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
        exampleLabel.style.marginTop = 2;
        _payloadContainer.Add(exampleLabel);
    }

    /// <summary>
    /// 获取协议提示信息喵~
    /// </summary>
    private string GetProtocolTooltip(EventProtocol protocol)
    {
        switch (protocol)
        {
            case EventProtocol.Numeric: return "请输入数值（整数或小数）喵~";
            case EventProtocol.String: return "请输入字符串喵~";
            case EventProtocol.Boolean: return "请输入 True 或 False 喵~";
            case EventProtocol.Vector: return "请输入格式：x,y,z（如：1.5,0,3.0）喵~";
            default: return "";
        }
    }

    /// <summary>
    /// 获取协议示例喵~
    /// </summary>
    private string GetProtocolExample(EventProtocol protocol)
    {
        switch (protocol)
        {
            case EventProtocol.Numeric: return "示例：42 或 3.14";
            case EventProtocol.String: return "示例：Mission_001";
            case EventProtocol.Boolean: return "示例：True 或 False";
            case EventProtocol.Vector: return "示例：1.5,0,3.0";
            default: return "";
        }
    }

    public override void UpdateData()
    {
        // 数据已经在回调中实时更新
        if (_payloadField != null)
        {
            TypedData.PayloadValue = _payloadField.value;
        }
    }
}
#endif
