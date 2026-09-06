using CloudScope.Commands;
using OpenTK.Mathematics;

namespace CloudScope.Ui.Commands;

public sealed partial class ViewerCommands
{
    private static readonly Keyword[] CrossSectionKeywords =
    [
        new("NEW", "New"),
        new("WIDTH", "Width"),
        new("FLIP", "Flip"),
        new("VIEW", "View"),
        new("LIST", "List"),
        new("CLEAR", "CLear")
    ];

    private float _lastCrossSectionWidth = 1f;

    [CommandMethod("XSECTION", "XS",
        Group = CommandGroup.View, Scope = CommandScope.Document,
        Summary = "Creates and displays a finite vertical point-cloud cross-section.",
        Syntax = "XSECTION <first point> <second point> [width] | [New/Width/Flip/View/List/CLear]")]
    public IEnumerable<PromptStep> CrossSection(CommandContext context)
    {
        var viewer = context.GetTarget<ViewerController>();
        Editor ed = context.Editor;

        PromptPointStep first = ed
            .GetPoint("Specify section start point or [New/Width/Flip/View/List/CLear] <New>:")
            .WithKeywords(CrossSectionKeywords)
            .WithDefaultKeyword("NEW");
        yield return first;

        if (first.Status == PromptStatus.Keyword)
        {
            switch (first.Keyword)
            {
                case "CLEAR":
                    ed.WriteMessage(viewer.ClearCrossSection());
                    yield break;
                case "FLIP":
                    ed.WriteMessage(viewer.FlipCrossSection());
                    yield break;
                case "VIEW":
                    ed.WriteMessage(viewer.ShowCrossSectionInActiveViewport());
                    yield break;
                case "LIST":
                    ed.WriteMessage(viewer.CrossSectionDescription);
                    yield break;
                case "WIDTH":
                    PromptDoubleStep changedWidth = ed
                        .GetDistance($"Specify section width <{_lastCrossSectionWidth:0.###}>:")
                        .WithRange(0.001, double.MaxValue)
                        .WithDefault(_lastCrossSectionWidth);
                    yield return changedWidth;
                    if (!changedWidth.IsOk) yield break;
                    _lastCrossSectionWidth = changedWidth.Single;
                    ed.WriteMessage(viewer.SetCrossSectionWidth(_lastCrossSectionWidth));
                    yield break;
                case "NEW":
                    first = ed.GetPoint("Specify section start point:");
                    yield return first;
                    break;
            }
        }

        if (!first.IsOk) yield break;

        viewer.BeginCrossSectionDraft(first.Value, _lastCrossSectionWidth);
        try
        {
            PromptPointStep second = ed.GetCorner("Specify section end point:", first.Value);
            yield return second;
            if (!second.IsOk) yield break;

            viewer.SetCrossSectionDraftBaseline(first.Value, second.Value, _lastCrossSectionWidth);
            PromptDistanceOrPointStep width = ed
                .GetDistanceOrPoint(
                    $"Specify section width or pick one edge <{_lastCrossSectionWidth:0.###}>:",
                    point => MeasureSectionWidth(first.Value, second.Value, point))
                .WithRange(0.001, double.MaxValue)
                .WithDefault(_lastCrossSectionWidth);
            yield return width;
            if (!width.IsOk) yield break;

            _lastCrossSectionWidth = width.Single;
            ed.WriteMessage(viewer.CreateCrossSection(first.Value, second.Value, _lastCrossSectionWidth));
        }
        finally
        {
            viewer.ClearCrossSectionDraft();
        }
    }

    private static double MeasureSectionWidth(Vector3 start, Vector3 end, Vector3 point)
    {
        Vector2 along = new(end.X - start.X, end.Y - start.Y);
        if (along.LengthSquared < 1e-10f) return 0d;
        along.Normalize();
        Vector2 normal = new(-along.Y, along.X);
        Vector2 delta = new(point.X - start.X, point.Y - start.Y);
        return 2d * Math.Abs(Vector2.Dot(delta, normal));
    }
}
