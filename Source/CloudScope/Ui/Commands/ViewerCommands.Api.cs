using CloudScope.Commands;

namespace CloudScope.Ui.Commands;

public sealed partial class ViewerCommands
{
    private static readonly Keyword[] ApiKeywords = [new("ON", "ON"), new("OFF", "OFf")];
    [CommandMethod("API", Flags = CommandFlags.NoUndoMarker, Group = CommandGroup.Utility, Scope = CommandScope.Viewer,
        Summary = "Starts or stops the authenticated local command HTTP API.", Syntax = "API [ON/OFf] [port]")]
    public IEnumerable<PromptStep> Api(CommandContext context)
    {
        var state = context.Editor.GetKeywords("Local API [ON/OFf] <ON>:", ApiKeywords).WithDefaultKeyword("ON");
        yield return state;
        if (state.Status != PromptStatus.Keyword) yield break;
        int number = 47830;
        if (state.Is("ON"))
        {
            var port = context.Editor.GetInteger("Local port <47830>:").WithRange(1024, 65535).WithDefault(47830);
            yield return port;
            if (!port.IsOk) yield break;
            number = port.Value;
        }
        context.Editor.WriteMessage(context.GetTarget<ViewerController>().ConfigureLocalApi(state.Is("ON"), number));
    }
}
