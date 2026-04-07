using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// 命令数据 - CommandNode 存储命令参数的核心数据结构喵~
/// </summary>
[Serializable]
public class CommandData
{
    [Tooltip("命令名（对应 CommandRegistry 中的注册名）")]
    public string CommandName = "";

    [Tooltip("命令参数列表，数量和含义由 CommandName 决定")]
    public List<string> Parameters = new List<string>();

    [JsonIgnore]
    public string Parameter
    {
        get => GetParam(0);
        set => SetParam(0, value);
    }

    public string GetParam(int index, string defaultValue = "")
    {
        if (Parameters == null || index < 0 || index >= Parameters.Count)
            return defaultValue;
        return Parameters[index] ?? defaultValue;
    }

    public void SetParam(int index, string value)
    {
        if (Parameters == null) Parameters = new List<string>();
        while (Parameters.Count <= index) Parameters.Add("");
        Parameters[index] = value;
    }
}
