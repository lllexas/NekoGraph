using System.Text.RegularExpressions;

namespace NekoGraph.Cli;

internal static class PortSchemaCatalog
{
    private static readonly Dictionary<string, PortSchema> Schemas = new(StringComparer.Ordinal);
    private static readonly Regex ClassRegex = new(@"\bclass\s+(?<name>\w+)", RegexOptions.Compiled);
    private static readonly Regex PortAttributeRegex = new(@"\[(?<kind>InPort|OutPort)\((?<index>\d+)", RegexOptions.Compiled);
    private static readonly Regex FieldRegex = new(@"^\s*(?:public|protected|internal|private)\s+(?:[\w<>\[\],?.]+\s+)+(?<name>\w+)\s*(?:=|;)", RegexOptions.Compiled);
    private static bool _loaded;

    public static PortSchema GetSchema(string typeName)
    {
        EnsureLoaded();
        return Schemas.TryGetValue(typeName, out var schema)
            ? schema
            : PortSchema.Empty;
    }

    private static void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        var repositoryRoot = RepositoryLocator.FindRoot(Environment.CurrentDirectory);
        if (repositoryRoot is null)
        {
            return;
        }

        var runtimePath = Path.Combine(repositoryRoot, "Assets", "Scripts", "NekoGraph", "Runtime");
        if (!Directory.Exists(runtimePath))
        {
            return;
        }

        foreach (var filePath in Directory.EnumerateFiles(runtimePath, "*.cs", SearchOption.AllDirectories))
        {
            try
            {
                LoadSourceSchemas(filePath);
            }
            catch
            {
            }
        }
    }

    private static void LoadSourceSchemas(string filePath)
    {
        string? currentClassName = null;
        var pendingPorts = new List<PendingPortAttribute>();
        var classBuilders = new Dictionary<string, PortSchemaBuilder>(StringComparer.Ordinal);

        foreach (var rawLine in File.ReadLines(filePath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            var classMatch = ClassRegex.Match(line);
            if (classMatch.Success)
            {
                currentClassName = classMatch.Groups["name"].Value;
                pendingPorts.Clear();
                continue;
            }

            if (currentClassName is null)
            {
                continue;
            }

            var portMatch = PortAttributeRegex.Match(line);
            if (portMatch.Success &&
                int.TryParse(portMatch.Groups["index"].Value, out var portIndex))
            {
                pendingPorts.Add(new PendingPortAttribute(
                    portMatch.Groups["kind"].Value,
                    portIndex));
                continue;
            }

            if (line.StartsWith("[", StringComparison.Ordinal))
            {
                continue;
            }

            if (pendingPorts.Count == 0)
            {
                continue;
            }

            var fieldMatch = FieldRegex.Match(line);
            if (!fieldMatch.Success)
            {
                pendingPorts.Clear();
                continue;
            }

            var fieldName = fieldMatch.Groups["name"].Value;
            if (!classBuilders.TryGetValue(currentClassName, out var builder))
            {
                builder = new PortSchemaBuilder();
                classBuilders[currentClassName] = builder;
            }

            foreach (var pendingPort in pendingPorts)
            {
                if (string.Equals(pendingPort.Kind, "OutPort", StringComparison.Ordinal))
                {
                    builder.AddOutputField(pendingPort.Index, fieldName);
                }
                else if (string.Equals(pendingPort.Kind, "InPort", StringComparison.Ordinal))
                {
                    builder.AddInputPort(pendingPort.Index);
                }
            }

            pendingPorts.Clear();
        }

        foreach (var pair in classBuilders)
        {
            if (!pair.Value.HasPorts)
            {
                continue;
            }

            Schemas[pair.Key] = pair.Value.Build();
        }
    }
}

internal sealed record PortSchema(
    Dictionary<int, List<string>> OutputFieldKeysByPort,
    List<int> InputPorts)
{
    public static readonly PortSchema Empty = new([], []);

    public List<int> OutputPorts =>
        OutputFieldKeysByPort.Keys.OrderBy(value => value).ToList();
}

internal sealed class PortSchemaBuilder
{
    private readonly Dictionary<int, HashSet<string>> _outputFields = new();
    private readonly HashSet<int> _inputPorts = [];

    public bool HasPorts => _outputFields.Count > 0 || _inputPorts.Count > 0;

    public void AddOutputField(int portIndex, string fieldName)
    {
        if (!_outputFields.TryGetValue(portIndex, out var fields))
        {
            fields = new HashSet<string>(StringComparer.Ordinal);
            _outputFields[portIndex] = fields;
        }

        fields.Add(fieldName);
    }

    public void AddInputPort(int portIndex)
    {
        _inputPorts.Add(portIndex);
    }

    public PortSchema Build()
    {
        return new PortSchema(
            _outputFields.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.OrderBy(value => value, StringComparer.Ordinal).ToList()),
            _inputPorts.OrderBy(value => value).ToList());
    }
}

internal sealed record PendingPortAttribute(
    string Kind,
    int Index);
