namespace NekoGraph.Cli;

internal static class NamedNodeRef
{
    public static string? TryBuild(PackNode node)
    {
        if (IsSpine(node) && !string.IsNullOrWhiteSpace(node.ProcessId))
        {
            return $"spine:{node.ProcessId}";
        }

        if (IsLeafA(node) && !string.IsNullOrWhiteSpace(node.ProcessId))
        {
            return $"leaf-a:{node.ProcessId}";
        }

        if (IsLeafB(node) && !string.IsNullOrWhiteSpace(node.ProcessId))
        {
            return $"leaf-b:{node.ProcessId}";
        }

        if (IsMissionA(node) && !string.IsNullOrWhiteSpace(node.MissionId))
        {
            return $"mission-a:{node.MissionId}";
        }

        if (IsMissionS(node) && !string.IsNullOrWhiteSpace(node.MissionId))
        {
            return $"mission-s:{node.MissionId}";
        }

        if (IsMissionF(node) && !string.IsNullOrWhiteSpace(node.MissionId))
        {
            return $"mission-f:{node.MissionId}";
        }

        if (IsMissionR(node) && !string.IsNullOrWhiteSpace(node.MissionId))
        {
            return $"mission-r:{node.MissionId}";
        }

        return null;
    }

    private static bool IsSpine(PackNode node) =>
        node.TypeName.Contains("SpineNode", StringComparison.OrdinalIgnoreCase);

    private static bool IsLeafA(PackNode node) =>
        node.TypeName.Contains("LeafNode_A", StringComparison.OrdinalIgnoreCase);

    private static bool IsLeafB(PackNode node) =>
        node.TypeName.Contains("LeafNode_B", StringComparison.OrdinalIgnoreCase);

    private static bool IsMissionA(PackNode node) =>
        node.TypeName.Contains("MissionNode_A", StringComparison.OrdinalIgnoreCase);

    private static bool IsMissionS(PackNode node) =>
        node.TypeName.Contains("MissionNode_S", StringComparison.OrdinalIgnoreCase);

    private static bool IsMissionF(PackNode node) =>
        node.TypeName.Contains("MissionNode_F", StringComparison.OrdinalIgnoreCase);

    private static bool IsMissionR(PackNode node) =>
        node.TypeName.Contains("MissionNode_R", StringComparison.OrdinalIgnoreCase);
}
