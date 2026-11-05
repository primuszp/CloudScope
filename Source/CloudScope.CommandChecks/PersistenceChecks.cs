using CloudScope;
using CloudScope.Commands;
using CloudScope.IO;
using CloudScope.Ui;
using CloudScope.Labeling;

internal static class PersistenceChecks
{
    public static void Run(Action<string, bool, string> check)
    {
        var labels = new LabelManager();
        var history = new UndoManager();
        labels.ActionRecorded += history.Record;
        check("New labels start clean", !labels.HasUnsavedChanges, "");
        labels.ApplyLabel([0, 1], "Tree");
        labels.MarkSaved();
        labels.ApplyLabel([0, 1], "Tree");
        check("Identical annotation does not dirty a saved document", !labels.HasUnsavedChanges, "");
        labels.ClearAll();
        check("Deleting every saved label is still unsaved", labels.Count == 0 && labels.HasUnsavedChanges, "");
        history.Undo();
        check("Undo returns to the saved checkpoint", !labels.HasUnsavedChanges && labels.Count == 2, "");
        history.Redo();
        check("Redo leaves the saved checkpoint", labels.HasUnsavedChanges && labels.Count == 0, "");
        labels.Reset();
        check("Replacing a document clears the save checkpoint", !labels.HasUnsavedChanges, "");
        string directory = Path.Combine(Path.GetTempPath(), "cloudscope-persistence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "labels.json");
            File.WriteAllText(path, "original");
            try { AtomicFile.Write(path, stream => { stream.WriteByte(42); throw new IOException("Injected write failure"); }); }
            catch (IOException) { }
            check("Failed publication preserves the old file and removes temporary data", File.ReadAllText(path) == "original" && Directory.GetFiles(directory).Length == 1, "");
            AtomicFile.WriteText(path, "complete");
            check("Successful publication replaces the old file", File.ReadAllText(path) == "complete", "");
            File.WriteAllText(path, "{\"labels\":{\"Tree\":[0]}}");
            using var viewer = new ViewerController(640, 480, new HeadlessBackend());
            viewer.Load();
            viewer.LoadPointCloud(new CloudScope.Loading.PointCloudDataset([new PointData()], 1, 1, false, 1, new CloudScope.Loading.PointCloudAttributes(new byte[1], new ushort[1], new byte[1], new double[1], 0, 0)));
            var commands = new ViewerCommandDispatcher(viewer);
            commands.Execute($"LOADLABELS \"{path}\"");
            commands.Execute("QUIT Yes");
            check("Quit remains open when no save destination exists", !viewer.CloseRequested && viewer.HasUnsavedLabels, "");
            commands.Execute($"SAVELABELS \"{Path.Combine(directory, "missing", "labels.json")}\"");
            check("An IO error cannot mark labels saved", viewer.HasUnsavedLabels && !viewer.CloseRequested, "");
            commands.Execute($"SAVELABELS \"{Path.Combine(directory, "saved.json")}\"");
            check("Successful save establishes the viewer checkpoint", !viewer.HasUnsavedLabels, "");
            check("Saved viewer closes without another save prompt", commands.Execute("QUIT").Status == CommandStatus.Ended && viewer.CloseRequested, "");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
