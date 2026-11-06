using CloudScope.Commands;
using CloudScope.Measurements;
using OpenTK.Mathematics;

namespace CloudScope.Ui.Commands;

public sealed partial class ViewerCommands
{
    [CommandMethod("MEASURE", "MEAS", Group = CommandGroup.View, Scope = CommandScope.Document,
        Summary = "Stores named distance, height, slope or horizontal area measurements; exports CSV/DXF.",
        Syntax = "MEASURE [Distance/Area/List/Remove/Export] <name> <points...> [Close]")]
    public IEnumerable<PromptStep> Measure(CommandContext context)
    {
        var viewer = context.GetTarget<ViewerController>(); var ed = context.Editor;
        var option = ed.GetKeywords("Measure [Distance/Area/List/Remove/Export] <Distance>:",
            new Keyword("DISTANCE","Distance"),new Keyword("AREA","Area"),new Keyword("LIST","List"),
            new Keyword("REMOVE","Remove"),new Keyword("EXPORT","Export")).WithDefaultKeyword("DISTANCE");
        yield return option; if (option.Status != PromptStatus.Keyword) yield break;
        if (option.Keyword == "LIST") { ed.WriteMessage(viewer.MeasurementDescription); yield break; }
        if (option.Keyword == "EXPORT")
        {
            foreach (var prompt in ExportEngineering(context,false)) yield return prompt;
            yield break;
        }
        var name = ed.GetString("Measurement name:");
        yield return name; if (!name.IsOk) yield break;
        if (option.Keyword == "REMOVE") { ed.WriteMessage(viewer.RemoveMeasurement(name.Value)); yield break; }
        var first = ed.GetPoint("Specify first point:"); yield return first; if (!first.IsOk) yield break;
        var points = new List<Vector3> { first.Value };
        bool area = option.Keyword == "AREA";
        viewer.BeginPolylineDraft(first.Value);
        try
        {
            while (true)
            {
                var next = ed.GetPoint(area ? "Specify next boundary point or [Close]:" : "Specify second point:");
                if (area) next.WithKeywords(new Keyword("CLOSE","Close"));
                yield return next;
                if (next.Status == PromptStatus.Keyword && next.Keyword == "CLOSE")
                {
                    if (points.Count < 3) { ed.WriteMessage("Area requires at least three vertices."); continue; }
                    break;
                }
                if (!next.IsOk) yield break;
                points.Add(next.Value); viewer.AddPolylineDraftVertex(next.Value);
                if (!area) break;
            }
            ed.WriteMessage(viewer.AddMeasurement(name.Value,area ? MeasurementKind.Area : MeasurementKind.Distance,points.ToArray()));
        }
        finally { viewer.ClearPolylineDraft(); }
    }

    private static IEnumerable<PromptStep> ExportEngineering(CommandContext context, bool section)
    {
        var ed = context.Editor;
        var format = ed.GetKeywords("Export format [Csv/Dxf] <Csv>:",new Keyword("CSV","Csv"),new Keyword("DXF","Dxf"))
            .WithDefaultKeyword("CSV");
        yield return format; if (format.Status != PromptStatus.Keyword) yield break;
        bool dxf = format.Keyword == "DXF";
        var path = ed.GetFileNameForSave("Export destination:").Filtering(dxf ? "dxf" : "csv").Requiring();
        yield return path; if (!path.IsOk) yield break;
        var viewer = context.GetTarget<ViewerController>();
        ed.WriteMessage(section ? viewer.ExportCrossSection(path.Value,dxf ? EngineeringExportFormat.Dxf : EngineeringExportFormat.Csv)
            : viewer.ExportMeasurements(path.Value,dxf ? EngineeringExportFormat.Dxf : EngineeringExportFormat.Csv));
    }
}
