using CloudScope.Commands;

namespace CloudScope.Ui.Commands;

public sealed partial class ViewerCommands
{
    [CommandMethod("SAVEPROJECT", "SAVE", Flags = CommandFlags.NoUndoMarker, Group = CommandGroup.File,
        Scope = CommandScope.Viewer, Summary = "Saves the complete project, including drawings, labels and views.", Syntax = "SAVEPROJECT [path]")]
    public IEnumerable<PromptStep> SaveProject(CommandContext context)
    {
        var viewer = context.GetTarget<ViewerController>();
        string path = viewer.ProjectPath;
        if (context.Editor.HasArguments || path.Length == 0)
        {
            var target = context.Editor.GetFileNameForSave("Project file name:").Filtering("cloudscope").Requiring();
            yield return target;
            if (!target.IsOk) yield break;
            path = ProjectExtension(target.Value);
        }
        context.Editor.WriteMessage(viewer.SaveProject(path));
    }

    [CommandMethod("SAVEAS", Flags = CommandFlags.NoUndoMarker, Group = CommandGroup.File,
        Scope = CommandScope.Viewer, Summary = "Saves the complete project under a new name.", Syntax = "SAVEAS <path>")]
    public IEnumerable<PromptStep> SaveProjectAs(CommandContext context)
    {
        var target = context.Editor.GetFileNameForSave("New project file name:").Filtering("cloudscope").Requiring();
        yield return target;
        if (target.IsOk) context.Editor.WriteMessage(context.GetTarget<ViewerController>().SaveProject(ProjectExtension(target.Value)));
    }

    [CommandMethod("OPENPROJECT", Flags = CommandFlags.NoUndoMarker, Group = CommandGroup.File,
        Scope = CommandScope.Viewer, Summary = "Restores a complete project after validating its sources.", Syntax = "OPENPROJECT <path>")]
    public IEnumerable<PromptStep> OpenProject(CommandContext context)
    {
        var target = context.Editor.GetFileNameForOpen("Project to open:").Filtering("cloudscope").Requiring();
        yield return target;
        if (!target.IsOk) yield break;
        var approval = new ProjectChangeApproval();
        foreach (var step in ProtectProject(context, approval)) yield return step;
        if (approval.Approved) context.Editor.WriteMessage(context.GetTarget<ViewerController>().OpenProject(target.Value));
    }

    [CommandMethod("RECOVER", Flags = CommandFlags.NoUndoMarker, Group = CommandGroup.File,
        Scope = CommandScope.Viewer, Summary = "Restores the newest abandoned automatic recovery snapshot, or a supplied snapshot.", Syntax = "RECOVER [path]")]
    public IEnumerable<PromptStep> RecoverProject(CommandContext context)
    {
        string path;
        if (context.Editor.HasArguments)
        {
            var file = context.Editor.GetFileNameForOpen("Recovery snapshot:").Filtering("cloudscope").Requiring();
            yield return file;
            if (!file.IsOk) yield break;
            path = file.Value;
        }
        else
        {
            var available = ProjectRecovery.Available();
            if (available.Length == 0) { context.Editor.WriteMessage("No abandoned project recovery snapshot was found."); yield break; }
            path = available[0];
        }
        var approval = new ProjectChangeApproval();
        foreach (var step in ProtectProject(context, approval)) yield return step;
        if (approval.Approved) context.Editor.WriteMessage(context.GetTarget<ViewerController>().OpenProject(path, recovery: true));
    }

    private static string ProjectExtension(string path) => Path.HasExtension(path) ? path : path + ".cloudscope";
    private sealed class ProjectChangeApproval { public bool Approved { get; set; } }
    private static IEnumerable<PromptStep> ProtectProject(CommandContext context, ProjectChangeApproval approval)
    {
        var viewer = context.GetTarget<ViewerController>();
        if (!viewer.HasUnsavedProject) { approval.Approved = true; yield break; }
        var save = context.Editor.GetKeywords("Save project changes? [Yes/No/Cancel] <Yes>:",
            new Keyword("YES", "Yes"), new Keyword("NO", "No"), new Keyword("CANCEL", "Cancel")).WithDefaultKeyword("YES");
        yield return save;
        if (save.Is("NO")) { approval.Approved = true; yield break; }
        if (!save.Is("YES")) { context.Editor.Cancel(); yield break; }
        string path = viewer.ProjectPath;
        if (path.Length == 0)
        {
            var target = context.Editor.GetFileNameForSave("Save project as:").Filtering("cloudscope").Requiring();
            yield return target;
            if (!target.IsOk) { context.Editor.Cancel(); yield break; }
            path = ProjectExtension(target.Value);
        }
        context.Editor.WriteMessage(viewer.SaveProject(path));
        approval.Approved = true;
    }
}
