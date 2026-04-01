namespace NekoGraph.Cli;

internal static class CliApp
{
    public static int Run(string[] args)
    {
        var command = CommandParser.Parse(args);

        if (command.ShowVersion)
        {
            Console.Out.WriteLine(VersionInfo.DisplayString);
            return 0;
        }

        if (command.ShowHelp)
        {
            Console.Out.WriteLine(HelpText.Value);
            return 0;
        }

        if (command.ShowNodeHelp)
        {
            Console.Out.WriteLine(NodeHelpText.Value);
            return 0;
        }

        if (command.RunFull)
        {
            return RunFullCommand.Execute(command.PackId!);
        }

        if (command.ShowProcess)
        {
            return ShowLocalCommand.ExecuteProcess(command.PackId!, command.TargetId!);
        }

        if (command.ShowNode)
        {
            return ShowLocalCommand.ExecuteNode(command.PackId!, command.TargetId!);
        }

        if (command.ShowMission)
        {
            return ShowLocalCommand.ExecuteMission(command.PackId!, command.TargetId!);
        }

        if (command.VfsList)
        {
            return VfsCommand.ExecuteList(command.PackId!, command.VfsPath!);
        }

        if (command.VfsShow)
        {
            return VfsCommand.ExecuteShow(command.PackId!, command.VfsPath!);
        }

        if (command.VfsMkdir)
        {
            return VfsCommand.ExecuteMkdir(command.PackId!, command.VfsPath!);
        }

        if (command.VfsWrite)
        {
            return VfsCommand.ExecuteWrite(command.PackId!, command.VfsPath!, command.FieldValue!);
        }

        if (command.VfsDelete)
        {
            return VfsCommand.ExecuteDelete(command.PackId!, command.VfsPath!);
        }

        if (command.QueryBridge)
        {
            return ShowLocalCommand.ExecuteBridge(
                command.PackId!,
                command.SourceRef!,
                command.DestinationRef!,
                command.FromPortIndex,
                command.ToPortIndex);
        }

        if (command.QueryFields)
        {
            return UnnamedFieldCommand.ExecuteQueryFields(
                command.PackId!,
                command.SourceRef!,
                command.DestinationRef!,
                command.EdgeIndex!.Value,
                command.FromPortIndex,
                command.ToPortIndex);
        }

        if (command.EditCreateBridge)
        {
            return EditUnnamedCommand.ExecuteCreateBridge(
                command.PackId!,
                command.SourceRef!,
                command.DestinationRef!,
                command.NodeKind!,
                command.FromPortIndex,
                command.ToPortIndex);
        }

        if (command.EditDestroyBridge)
        {
            return EditUnnamedCommand.ExecuteDestroyBridge(
                command.PackId!,
                command.SourceRef!,
                command.DestinationRef!,
                command.FromPortIndex,
                command.ToPortIndex);
        }

        if (command.EditInsertUnnamed)
        {
            return command.EdgeIndex.HasValue
                ? EditUnnamedCommand.ExecuteInsertAt(command.PackId!, command.SourceRef!, command.DestinationRef!, command.EdgeIndex.Value, command.NodeKind!, command.FromPortIndex, command.ToPortIndex)
                : EditUnnamedCommand.ExecuteInsert(command.PackId!, command.SourceRef!, command.DestinationRef!, command.NodeKind!, command.FromPortIndex, command.ToPortIndex);
        }

        if (command.EditRemoveUnnamed)
        {
            return command.EdgeIndex.HasValue
                ? EditUnnamedCommand.ExecuteRemoveAt(command.PackId!, command.SourceRef!, command.DestinationRef!, command.EdgeIndex.Value)
                : EditUnnamedCommand.ExecuteRemove(command.PackId!, command.SourceRef!, command.DestinationRef!);
        }

        if (command.EditField)
        {
            return UnnamedFieldCommand.ExecuteEditField(
                command.PackId!,
                command.SourceRef!,
                command.DestinationRef!,
                command.EdgeIndex!.Value,
                command.FieldName!,
                command.FieldValue!,
                command.FromPortIndex,
                command.ToPortIndex);
        }

        Console.Error.WriteLine(command.ErrorMessage ?? "No command specified.");
        Console.Error.WriteLine("Use --help to inspect available commands.");
        return 1;
    }
}
