using CloudScope.Commands;
using CloudScope.Selection;
using OpenTK.Mathematics;

namespace CloudScope.Ui.Commands;

public sealed partial class ViewerCommands
{
    private static readonly Keyword[] AxisKeywords =
    [
        new("X", "X"),
        new("Y", "Y"),
        new("Z", "Z")
    ];

    private static readonly Keyword ReferenceKeyword = new("REFERENCE", "Reference");

    [CommandMethod("SELECT",
        Group = CommandGroup.Edit, Scope = CommandScope.Document,
        Summary = "Draws a selection volume and applies it to the current label.",
        Syntax = "SELECT [Box/Sphere/Cylinder/Undo/CONFirm/CANcel/Fit/Ground]")]
    public IEnumerable<PromptStep> Select(CommandContext context)
    {
        var viewer = context.GetTarget<ViewerController>();
        Editor ed = context.Editor;

        // Repeating SELECT with Enter while a volume is on screen accepts it, the way Enter
        // accepts a pending operation in AutoCAD.
        if (!ed.HasArguments && viewer.HasActiveSelection)
        {
            viewer.ConfirmActiveSelection();
            ed.WriteMessage("Selection applied.");
            yield break;
        }

        PromptStep shape = ed
            .GetKeywords("Specify selection shape [Box/Sphere/Cylinder/Undo] <Box>:", SelectKeywords)
            .WithDefaultKeyword("BOX");
        yield return shape;

        if (!TryToolFor(shape.Keyword, out SelectionToolType tool))
        {
            if (shape.Is("UNDO"))
            {
                ed.WriteMessage(viewer.UndoSelectionCommand() ? "Last selection change undone." : "Nothing to undo.");
                yield break;
            }

            ed.Cancel();
            yield break;
        }

        viewer.SetTool(tool);
        ed.WriteMessage($"{tool} selection started. Draw the selection volume.");

        // The tool stays live between questions, so an abandoned command must put it away.
        // Disposing this iterator — which is all Escape does — runs the finally block.
        bool applied = false;
        try
        {
            while (true)
            {
                PromptStep adjust = ed.GetKeywords(
                    "Adjust selection using grips or [CONFirm/Undo/CANcel/Fit/Ground/Box/Sphere/Cylinder]:",
                    SelectKeywords);
                yield return adjust;

                if (adjust.Status == PromptStatus.None)
                {
                    if (!viewer.HasActiveSelection)
                        yield break;

                    viewer.ConfirmActiveSelection();
                    applied = true;
                    ed.WriteMessage("Selection applied.");
                    yield break;
                }

                switch (adjust.Keyword)
                {
                    case "CONFIRM":
                        if (!viewer.HasActiveSelection)
                        {
                            ed.WriteMessage("No active selection.");
                            continue;
                        }

                        viewer.ConfirmActiveSelection();
                        applied = true;
                        ed.WriteMessage("Selection applied.");
                        yield break;

                    case "CANCEL":
                        ed.Cancel();
                        yield break;

                    case "UNDO":
                        ed.WriteMessage(viewer.UndoSelectionCommand()
                            ? "Last selection change undone."
                            : "Nothing to undo.");
                        continue;

                    case "FIT":
                        ed.WriteMessage(viewer.FitActiveSelection(useGround: false));
                        continue;

                    case "GROUND":
                        ed.WriteMessage(viewer.FitActiveSelection(useGround: true));
                        continue;

                    default:
                        if (TryToolFor(adjust.Keyword, out SelectionToolType next))
                        {
                            viewer.SetTool(next);
                            ed.WriteMessage($"{next} selection started. Draw the selection volume.");
                        }

                        continue;
                }
            }
        }
        finally
        {
            if (!applied)
                viewer.CancelOrExitLabelMode();
        }
    }

    private static bool TryToolFor(string keyword, out SelectionToolType tool)
    {
        switch (keyword)
        {
            case "BOX": tool = SelectionToolType.Box; return true;
            case "SPHERE": tool = SelectionToolType.Sphere; return true;
            case "CYLINDER": tool = SelectionToolType.Cylinder; return true;
            default: tool = SelectionToolType.Box; return false;
        }
    }

    [CommandMethod("CONFIRM", Flags = CommandFlags.NoHistory,
        Group = CommandGroup.Edit, Scope = CommandScope.Document,
        Summary = "Applies the active selection to the current label.",
        Syntax = "CONFIRM")]
    public IEnumerable<PromptStep> Confirm(CommandContext context)
    {
        var viewer = context.GetTarget<ViewerController>();
        if (!viewer.HasActiveSelection)
        {
            context.Editor.WriteMessage("No active selection.");
            yield break;
        }

        viewer.ConfirmActiveSelection();
        context.Editor.WriteMessage("Selection applied.");
    }

    [CommandMethod("CANCEL", "ESC", Flags = CommandFlags.NoHistory | CommandFlags.NoUndoMarker,
        Group = CommandGroup.Edit, Scope = CommandScope.Viewer,
        Summary = "Cancels the active command or selection.",
        Syntax = "CANCEL")]
    public IEnumerable<PromptStep> Cancel(CommandContext context)
    {
        context.GetTarget<ViewerController>().CancelOrExitLabelMode();
        context.Editor.Cancel();
        yield break;
    }

    [CommandMethod("FIT", Flags = CommandFlags.NoUndoMarker,
        Group = CommandGroup.Edit, Scope = CommandScope.Document,
        Summary = "Shrinks the selection volume onto the points inside it.",
        Syntax = "FIT [Ground]")]
    public IEnumerable<PromptStep> Fit(CommandContext context)
    {
        PromptStep option = context.Editor
            .GetKeywords("Fit selection [Points/Ground] <Points>:", new Keyword("POINTS", "Points"), new Keyword("GROUND", "Ground"))
            .WithDefaultKeyword("POINTS");
        yield return option;

        context.Editor.WriteMessage(
            context.GetTarget<ViewerController>().FitActiveSelection(option.Is("GROUND")));
    }

    [CommandMethod("MOVE", "M", Flags = CommandFlags.NoUndoMarker,
        Group = CommandGroup.Edit, Scope = CommandScope.Document,
        Summary = "Moves the selection volume by a displacement or between two points.",
        Syntax = "MOVE [X/Y/Z] <dx,dy,dz> | <base point> <second point>")]
    public IEnumerable<PromptStep> Move(CommandContext context)
    {
        var viewer = context.GetTarget<ViewerController>();
        Editor ed = context.Editor;

        if (!viewer.HasActiveSelection)
        {
            ed.WriteMessage("No active selection. Start one with SELECT.");
            yield break;
        }

        PromptPointStep basePoint = ed
            .GetPoint("Specify base point or displacement [X/Y/Z]:")
            .WithKeywords(AxisKeywords);
        yield return basePoint;

        // An axis keyword asks for one distance along that axis; this is how a typed MOVE
        // stays as short as the gesture it replaces.
        if (basePoint.Status == PromptStatus.Keyword)
        {
            int axis = basePoint.Keyword switch { "X" => 0, "Y" => 1, _ => 2 };
            PromptDoubleStep distance = ed.GetDouble($"Specify distance along {basePoint.Keyword}:");
            yield return distance;
            if (!distance.IsOk) yield break;

            Vector3 along = axis switch
            {
                0 => new Vector3(distance.Single, 0f, 0f),
                1 => new Vector3(0f, distance.Single, 0f),
                _ => new Vector3(0f, 0f, distance.Single)
            };
            ed.WriteMessage(viewer.MoveActiveSelection(along));
            yield break;
        }

        if (!basePoint.IsOk) yield break;

        PromptPointStep second = ed
            .GetCorner("Specify second point <use first point as displacement>:", basePoint.Value);
        yield return second;

        Vector3 displacement = second.IsOk ? second.Value - basePoint.Value : basePoint.Value;
        ed.WriteMessage(viewer.MoveActiveSelection(displacement));
    }

    [CommandMethod("ROTATE", "RO", Flags = CommandFlags.NoUndoMarker,
        Group = CommandGroup.Edit, Scope = CommandScope.Document,
        Summary = "Rotates the selection volume about a world axis.",
        Syntax = "ROTATE [X/Y/Z] <angle in degrees>")]
    public IEnumerable<PromptStep> Rotate(CommandContext context)
    {
        var viewer = context.GetTarget<ViewerController>();
        Editor ed = context.Editor;

        if (!viewer.HasActiveSelection)
        {
            ed.WriteMessage("No active selection. Start one with SELECT.");
            yield break;
        }

        PromptStep axis = ed
            .GetKeywords("Specify rotation axis [X/Y/Z] <Z>:", AxisKeywords)
            .WithDefaultKeyword("Z");
        yield return axis;

        PromptDoubleStep angle = ed.GetAngle("Specify rotation angle in degrees:");
        yield return angle;
        if (!angle.IsOk) yield break;

        int index = axis.Keyword switch { "X" => 0, "Y" => 1, _ => 2 };
        ed.WriteMessage(viewer.RotateActiveSelection(angle.Single, index));
    }

    [CommandMethod("SCALE", "SC", Flags = CommandFlags.NoUndoMarker,
        Group = CommandGroup.Edit, Scope = CommandScope.Document,
        Summary = "Scales the selection volume by a factor.",
        Syntax = "SCALE <factor> | Reference <current> <new>")]
    public IEnumerable<PromptStep> Scale(CommandContext context)
    {
        var viewer = context.GetTarget<ViewerController>();
        Editor ed = context.Editor;

        if (!viewer.HasActiveSelection)
        {
            ed.WriteMessage("No active selection. Start one with SELECT.");
            yield break;
        }

        PromptDoubleStep factor = ed
            .GetDouble("Specify scale factor or [Reference] <1.0>:")
            .WithRange(0.001, 1000)
            .WithKeywords(ReferenceKeyword);
        yield return factor;

        if (factor.Is("REFERENCE"))
        {
            PromptDoubleStep current = ed.GetDistance("Specify reference length:");
            yield return current;
            if (!current.IsOk || current.Value <= 0) yield break;

            PromptDoubleStep target = ed.GetDistance("Specify new length:");
            yield return target;
            if (!target.IsOk) yield break;

            ed.WriteMessage(viewer.ScaleActiveSelection((float)(target.Value / current.Value)));
            yield break;
        }

        if (!factor.IsOk) yield break;
        ed.WriteMessage(viewer.ScaleActiveSelection(factor.Single));
    }
}
