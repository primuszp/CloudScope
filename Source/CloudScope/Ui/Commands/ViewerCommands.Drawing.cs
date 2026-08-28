using CloudScope.Commands;
using OpenTK.Mathematics;

namespace CloudScope.Ui.Commands;

public sealed partial class ViewerCommands
{
    private static readonly Keyword[] PolylineKeywords =
    [
        new("CLOSE", "Close", "C"),
        new("UNDO", "Undo", "U")
    ];

    [CommandMethod("3DPOLY", "POLYLINE", "PLINE", "PL",
        Group = CommandGroup.Edit, Scope = CommandScope.Viewer,
        Summary = "Draws a straight-segment 3D polyline with object and axis snapping.",
        Syntax = "3DPOLY <first point> <next point>... [Close/Undo]")]
    public IEnumerable<PromptStep> Polyline3D(CommandContext context)
    {
        ViewerController viewer = context.GetTarget<ViewerController>();
        Editor editor = context.Editor;

        PromptPointStep first = editor.GetPoint("Specify start point:");
        yield return first;
        if (!first.IsOk) yield break;

        viewer.BeginPolylineDraft(first.Value);
        bool committed = false;
        try
        {
            while (true)
            {
                PromptPointStep next = editor
                    .GetCorner("Specify next point or [Close/Undo] <finish>:",
                        viewer.PolylineDraftLastPoint)
                    .WithDirectionalDistance(distance => viewer.PointAlongCurrentDirection(
                        viewer.PolylineDraftLastPoint, distance))
                    .WithKeywords(PolylineKeywords);
                yield return next;

                if (next.Status == PromptStatus.None)
                {
                    if (viewer.PolylineDraftVertexCount < 2)
                    {
                        editor.WriteMessage("Specify at least one more point.");
                        continue;
                    }
                    editor.WriteMessage(viewer.CommitPolylineDraft(closed: false));
                    committed = true;
                    yield break;
                }

                if (next.Is("CLOSE"))
                {
                    if (viewer.PolylineDraftVertexCount < 3)
                    {
                        editor.WriteMessage("Specify at least three points before Close.");
                        continue;
                    }
                    editor.WriteMessage(viewer.CommitPolylineDraft(closed: true));
                    committed = true;
                    yield break;
                }

                if (next.Is("UNDO"))
                {
                    editor.WriteMessage(viewer.UndoPolylineDraftVertex()
                        ? "Last polyline vertex removed."
                        : "The start point cannot be removed.");
                    continue;
                }

                if (!next.IsOk) yield break;
                viewer.AddPolylineDraftVertex(next.Value);
            }
        }
        finally
        {
            if (!committed) viewer.ClearPolylineDraft();
        }
    }
}
