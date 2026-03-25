namespace NekoGraph.Cli;

internal sealed class ParsedCommand
{
    public bool ShowHelp { get; init; }

    public bool ShowVersion { get; init; }

    public bool RunFull { get; init; }

    public bool ShowProcess { get; init; }

    public bool ShowMission { get; init; }

    public bool ShowNode { get; init; }

    public bool EditInsertUnnamed { get; init; }

    public bool EditRemoveUnnamed { get; init; }

    public bool QueryBridge { get; init; }

    public bool QueryFields { get; init; }

    public bool EditCreateBridge { get; init; }

    public bool EditDestroyBridge { get; init; }

    public bool EditField { get; init; }

    public string? PackId { get; init; }

    public string? TargetId { get; init; }

    public string? SourceRef { get; init; }

    public string? DestinationRef { get; init; }

    public string? NodeKind { get; init; }

    public int? EdgeIndex { get; init; }

    public int? FromPortIndex { get; init; }

    public int? ToPortIndex { get; init; }

    public string? FieldName { get; init; }

    public string? FieldValue { get; init; }

    public string? ErrorMessage { get; init; }
}

internal static class CommandParser
{
    public static ParsedCommand Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return new ParsedCommand { ShowHelp = true };
        }

        if (args.Length == 1)
        {
            return args[0] switch
            {
                "--help" or "-h" or "help" => new ParsedCommand { ShowHelp = true },
                "--version" or "-v" or "version" => new ParsedCommand { ShowVersion = true },
                _ => new ParsedCommand { ErrorMessage = $"Unknown argument: {args[0]}" }
            };
        }

        if (args.Length == 3 &&
            string.Equals(args[0], "--run", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(args[1], "--full", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(args[2])
                ? new ParsedCommand { ErrorMessage = "PackID cannot be empty." }
                : new ParsedCommand { RunFull = true, PackId = args[2] };
        }

        if (args.Length == 4 &&
            string.Equals(args[0], "--show", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(args[1], "--node", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(args[2]) || string.IsNullOrWhiteSpace(args[3])
                ? new ParsedCommand { ErrorMessage = "--show --node requires <packid> and <named-node-ref>." }
                : new ParsedCommand { ShowNode = true, PackId = args[2], TargetId = args[3] };
        }

        if (args.Length == 4 &&
            string.Equals(args[0], "--show", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(args[1], "--process", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(args[2]) || string.IsNullOrWhiteSpace(args[3])
                ? new ParsedCommand { ErrorMessage = "--show --process requires <packid> and <processid>." }
                : new ParsedCommand { ShowProcess = true, PackId = args[2], TargetId = args[3] };
        }

        if (args.Length == 4 &&
            string.Equals(args[0], "--show", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(args[1], "--mission", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(args[2]) || string.IsNullOrWhiteSpace(args[3])
                ? new ParsedCommand { ErrorMessage = "--show --mission requires <packid> and <missionid>." }
                : new ParsedCommand { ShowMission = true, PackId = args[2], TargetId = args[3] };
        }

        if (args.Length >= 5 &&
            string.Equals(args[0], "--query", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(args[1], "--bridge", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParsePortOptions(args, 5, out var fromPortIndex, out var toPortIndex, out var portError))
            {
                return new ParsedCommand { ErrorMessage = portError };
            }

            return HasBlank(args[2], args[3], args[4])
                ? new ParsedCommand { ErrorMessage = "--query --bridge requires <packid> <from-named-ref> <to-named-ref>." }
                : new ParsedCommand
                {
                    QueryBridge = true,
                    PackId = args[2],
                    SourceRef = args[3],
                    DestinationRef = args[4],
                    FromPortIndex = fromPortIndex,
                    ToPortIndex = toPortIndex
                };
        }

        if (args.Length >= 6 &&
            string.Equals(args[0], "--query", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(args[1], "--fields", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParsePortOptions(args, 6, out var fromPortIndex, out var toPortIndex, out var portError))
            {
                return new ParsedCommand { ErrorMessage = portError };
            }

            if (HasBlank(args[2], args[3], args[4], args[5]))
            {
                return new ParsedCommand { ErrorMessage = "--query --fields requires <packid> <from-named-ref> <to-named-ref> <unnamed-node-index>." };
            }

            if (!int.TryParse(args[5], out var unnamedNodeIndex) || unnamedNodeIndex < 0)
            {
                return new ParsedCommand { ErrorMessage = "unnamed-node-index must be a non-negative integer." };
            }

            return new ParsedCommand
            {
                QueryFields = true,
                PackId = args[2],
                SourceRef = args[3],
                DestinationRef = args[4],
                EdgeIndex = unnamedNodeIndex,
                FromPortIndex = fromPortIndex,
                ToPortIndex = toPortIndex
            };
        }

        if (args.Length >= 6 &&
            string.Equals(args[0], "--edit", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(args[1], "--create-bridge", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParsePortOptions(args, 6, out var fromPortIndex, out var toPortIndex, out var portError))
            {
                return new ParsedCommand { ErrorMessage = portError };
            }

            return HasBlank(args[2], args[3], args[4], args[5])
                ? new ParsedCommand { ErrorMessage = "--edit --create-bridge requires <packid> <from-named-ref> <to-named-ref> <node-kind-list>." }
                : new ParsedCommand
                {
                    EditCreateBridge = true,
                    PackId = args[2],
                    SourceRef = args[3],
                    DestinationRef = args[4],
                    NodeKind = args[5],
                    FromPortIndex = fromPortIndex,
                    ToPortIndex = toPortIndex
                };
        }

        if (args.Length >= 5 &&
            string.Equals(args[0], "--edit", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(args[1], "--destroy-bridge", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParsePortOptions(args, 5, out var fromPortIndex, out var toPortIndex, out var portError))
            {
                return new ParsedCommand { ErrorMessage = portError };
            }

            return HasBlank(args[2], args[3], args[4])
                ? new ParsedCommand { ErrorMessage = "--edit --destroy-bridge requires <packid> <from-named-ref> <to-named-ref>." }
                : new ParsedCommand
                {
                    EditDestroyBridge = true,
                    PackId = args[2],
                    SourceRef = args[3],
                    DestinationRef = args[4],
                    FromPortIndex = fromPortIndex,
                    ToPortIndex = toPortIndex
                };
        }

        if (args.Length >= 8 &&
            string.Equals(args[0], "--edit", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(args[1], "--field", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParsePortOptions(args, 8, out var fromPortIndex, out var toPortIndex, out var portError))
            {
                return new ParsedCommand { ErrorMessage = portError };
            }

            if (HasBlank(args[2], args[3], args[4], args[5], args[6]))
            {
                return new ParsedCommand { ErrorMessage = "--edit --field requires <packid> <from-named-ref> <to-named-ref> <unnamed-node-index> <field-name> <value>." };
            }

            if (!int.TryParse(args[5], out var unnamedNodeIndex) || unnamedNodeIndex < 0)
            {
                return new ParsedCommand { ErrorMessage = "unnamed-node-index must be a non-negative integer." };
            }

            return new ParsedCommand
            {
                EditField = true,
                PackId = args[2],
                SourceRef = args[3],
                DestinationRef = args[4],
                EdgeIndex = unnamedNodeIndex,
                FieldName = args[6],
                FieldValue = args[7],
                FromPortIndex = fromPortIndex,
                ToPortIndex = toPortIndex
            };
        }

        if (args.Length == 6 &&
            string.Equals(args[0], "--edit", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(args[1], "--insert-unnamed", StringComparison.OrdinalIgnoreCase))
        {
            return HasBlank(args[2], args[3], args[4], args[5])
                ? new ParsedCommand { ErrorMessage = "--edit --insert-unnamed requires <packid> <from-named-ref> <to-named-ref> <trigger|comparer|command>." }
                : new ParsedCommand
                {
                    EditInsertUnnamed = true,
                    PackId = args[2],
                    SourceRef = args[3],
                    DestinationRef = args[4],
                    NodeKind = args[5]
                };
        }

        if (args.Length == 7 &&
            string.Equals(args[0], "--edit", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(args[1], "--insert-unnamed-at", StringComparison.OrdinalIgnoreCase))
        {
            if (HasBlank(args[2], args[3], args[4], args[5], args[6]))
            {
                return new ParsedCommand { ErrorMessage = "--edit --insert-unnamed-at requires <packid> <from-named-ref> <to-named-ref> <depth-edge-index> <trigger|comparer|command>." };
            }

            if (!int.TryParse(args[5], out var edgeIndex) || edgeIndex < 0)
            {
                return new ParsedCommand { ErrorMessage = "depth-edge-index must be a non-negative integer." };
            }

            return new ParsedCommand
            {
                EditInsertUnnamed = true,
                PackId = args[2],
                SourceRef = args[3],
                DestinationRef = args[4],
                EdgeIndex = edgeIndex,
                NodeKind = args[6]
            };
        }

        if (args.Length == 5 &&
            string.Equals(args[0], "--edit", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(args[1], "--remove-unnamed", StringComparison.OrdinalIgnoreCase))
        {
            return HasBlank(args[2], args[3], args[4])
                ? new ParsedCommand { ErrorMessage = "--edit --remove-unnamed requires <packid> <from-named-ref> <to-named-ref>." }
                : new ParsedCommand
                {
                    EditRemoveUnnamed = true,
                    PackId = args[2],
                    SourceRef = args[3],
                    DestinationRef = args[4]
                };
        }

        if (args.Length == 6 &&
            string.Equals(args[0], "--edit", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(args[1], "--remove-unnamed-at", StringComparison.OrdinalIgnoreCase))
        {
            if (HasBlank(args[2], args[3], args[4], args[5]))
            {
                return new ParsedCommand { ErrorMessage = "--edit --remove-unnamed-at requires <packid> <from-named-ref> <to-named-ref> <depth-edge-index>." };
            }

            if (!int.TryParse(args[5], out var edgeIndex) || edgeIndex < 0)
            {
                return new ParsedCommand { ErrorMessage = "depth-edge-index must be a non-negative integer." };
            }

            return new ParsedCommand
            {
                EditRemoveUnnamed = true,
                PackId = args[2],
                SourceRef = args[3],
                DestinationRef = args[4],
                EdgeIndex = edgeIndex
            };
        }

        return new ParsedCommand
        {
            ErrorMessage = $"Unsupported argument sequence: {string.Join(' ', args)}"
        };
    }

    private static bool HasBlank(params string[] values) =>
        values.Any(string.IsNullOrWhiteSpace);

    private static bool TryParsePortOptions(
        string[] args,
        int baseArgumentCount,
        out int? fromPortIndex,
        out int? toPortIndex,
        out string? errorMessage)
    {
        fromPortIndex = null;
        toPortIndex = null;
        errorMessage = null;

        if (args.Length == baseArgumentCount)
        {
            return true;
        }

        if ((args.Length - baseArgumentCount) % 2 != 0)
        {
            errorMessage = "Port options must be passed as flag/value pairs.";
            return false;
        }

        for (var i = baseArgumentCount; i < args.Length; i += 2)
        {
            var flag = args[i];
            var rawValue = args[i + 1];
            if (!int.TryParse(rawValue, out var portIndex) || portIndex < 0)
            {
                errorMessage = $"{flag} must be a non-negative integer.";
                return false;
            }

            switch (flag)
            {
                case "--from-port":
                    fromPortIndex = portIndex;
                    break;
                case "--to-port":
                    toPortIndex = portIndex;
                    break;
                default:
                    errorMessage = $"Unsupported option: {flag}";
                    return false;
            }
        }

        return true;
    }
}
