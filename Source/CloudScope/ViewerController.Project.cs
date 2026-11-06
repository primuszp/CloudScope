using CloudScope.Commands;
using CloudScope.Labeling;
using CloudScope.Loading;
using CloudScope.Projects;
using CloudScope.Rendering;
using CloudScope.Sections;
using CloudScope.Store;

namespace CloudScope;

public sealed partial class ViewerController
{
    private readonly ProjectFingerprint _projectFingerprint = new();
    private readonly BackgroundOperation<PreparedProject> _projectOperation;
    private readonly ProjectRecovery _projectRecovery = new();
    private string _projectCheckpoint = "";
    public string ProjectPath { get; private set; } = "";
    private DateTime _nextProjectStatus;
    private bool _projectStatusDirty;
    private bool ProjectStatusIsDirty
    {
        get
        {
            if (DateTime.UtcNow >= _nextProjectStatus) { _projectStatusDirty = HasUnsavedProject; _nextProjectStatus = DateTime.UtcNow.AddMilliseconds(250); }
            return _projectStatusDirty;
        }
    }
    public bool HasUnsavedProject => ProjectSignature() != _projectCheckpoint;
    internal Task ProjectCompletion => _projectOperation.Completion;
    private string ProjectSignature() => _projectFingerprint.Capture(CaptureProject(includeAnnotations: false), _selection.Labels.Revision, _document.Surface);

    internal ProjectState CaptureProject(bool includeAnnotations = true) => new()
    {
        Source = _document.Dataset is { } data ? new(_document.SourcePath, _document.SourceFormat, data.LoadedCount,
            data.SourceIdentity, data.Radius, data.HasColor, data.ColorScale, data.SourcePayloadSha256) : null,
        Layers = _document.Layers.Select(l => new ProjectLayer(l.ProjectId, Path.GetFullPath(l.Store.Directory), l.Name, l.Visible, l.Tint, l.ProjectIdentity)).ToArray(),
        Polylines = _polylines.Select(t => t.Polyline).ToArray(),
        PlanarPolylines = _planarPolylines.Select(t => t.Polyline).ToArray(),
        Sections = _document.Sections.Items.ToArray(),
        ActiveSectionId = _document.Sections.ActiveId,
        Measurements = _document.Measurements.Items.ToArray(),
        Definitions = LabelRegistry.Definitions.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
        Annotations = includeAnnotations ? _selection.CaptureProjectAnnotations() : [],
        NamedViews = _navigation.Capture(),
        Filter = ProjectFilter.Capture(_document.Dataset?.Filter),
        ColorSource = _document.Dataset?.CurrentColorSource ?? ColorSource.Rgb,
        KeepPercentage = _document.Dataset?.KeepPercentage ?? 100,
        HasSurface = _document.Surface != null,
        Workspace = new()
        {
            Viewports = _viewports.Select(v => new ProjectViewport(v.Camera.SaveState(), v.Input.PointSize, v.SectionMode)).ToArray(),
            Layout = (int)_viewportLayout, PreviousLayout = (int)_previousViewportLayout, AuxiliaryView = (int)_auxiliaryViewportView,
            ActiveViewport = _activeViewportIndex, ResidentVisible = _residentVisible, SurfaceVisible = _surfaceVisible,
            Ortho = OrthoMode, Mode = Mode, Tool = ActiveToolType, CurrentLabel = CurrentLabel, CurrentInstance = CurrentInstanceId,
            ExplorerVisible = ExplorerVisible, PropertiesVisible = PropertiesVisible, RibbonVisible = RibbonVisible,
            PalettesOnRight = PalettesOnRight, CommandLineVisible = CommandLineVisible, CommandLineFloating = CommandLineFloating,
            HistoryVisible = CommandHistoryVisible, LabelsVisible = LabelWindowVisible
        }
    };

    public string SaveProject(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_loadOperation.IsRunning || _projectOperation.IsRunning || _surfaceOperation.IsRunning || _processing.IsRunning)
            throw new InvalidOperationException("Wait for background work to finish, or cancel it, before saving the project.");
        path = Path.GetFullPath(path);
        ProtectProjectDestination(path);
        ProjectFile.Save(path, CaptureProject(), _document.Dataset, _document.Surface);
        ProjectPath = path;
        _projectCheckpoint = ProjectSignature();
        _projectRecovery.Clear();
        return $"Project saved: {path}.";
    }

    private void ProtectProjectDestination(string path)
    {
        if (_document.SourcePath.Length > 0 && FilePathIdentity.SamePath(path, _document.SourcePath))
            throw new InvalidOperationException("Choose a project path different from the source survey.");
        foreach (var layer in _document.Layers)
        {
            string relative = Path.GetRelativePath(Path.GetFullPath(layer.Store.Directory), path);
            if (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar))
                throw new InvalidOperationException("Save the project outside its source tile stores.");
        }
    }

    public string OpenProject(string path, bool recovery = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        path = Path.GetFullPath(path);
        if (!File.Exists(path)) return $"Project not found: {path}";
        if (_loadOperation.IsRunning || _surfaceOperation.IsRunning || _processing.IsRunning)
            return "Wait for background work to finish, or cancel it, before opening a project.";
        bool recover = recovery || ProjectRecovery.IsRecovery(path);
        string checkpoint = ProjectSignature();
        return _projectOperation.TryStart((cancellation, progress) => PreparedProject.Read(path, cancellation, progress), prepared =>
        {
            using (prepared)
            {
                if (checkpoint != ProjectSignature()) throw new InvalidOperationException("The document changed while the project was loading. Its current work was preserved; retry opening the project.");
                PublishProject(prepared);
                ProjectPath = recover ? "" : path;
                _projectCheckpoint = recover ? "recovered" : ProjectSignature();
                _projectRecovery.Clear();
                BackgroundMessage?.Invoke($"Project {(ProjectPath.Length == 0 ? "recovered; save it to keep the restored work" : "opened")}: {path}.");
            }
        }, error => BackgroundMessage?.Invoke($"Project load failed: {error.Message}")) ? $"Reading project {Path.GetFileName(path)}..." : "A project is already loading.";
    }

    private static PointCloudRenderData RenderData(PointCloudDataset? data) => data == null ? PointCloudRenderData.Empty
        : new(data.ViewPoints, data.RenderOrder, data.Attributes, data.ViewToSource, data.SourcePoints, data.CurrentColorSource);

    private void PublishProject(PreparedProject prepared)
    {
        // Stage GPU publication before changing any document objects. On an upload failure the old sources remain owned and usable.
        try
        {
            if (prepared.Dataset is { } points && !_pointRenderer.SupportsAttributeColoring)
                points.SetColorSource(prepared.State.ColorSource, recolor: true);
            _pointRenderer.Upload(RenderData(prepared.Dataset));
            if (prepared.Layers.Count > 0) _streamingRenderer.Open(prepared.Layers); else _streamingRenderer.Close();
            _surfaceRenderer.Upload(prepared.Surface);
        }
        catch
        {
            _pointRenderer.Upload(RenderData(_document.Dataset));
            if (_document.Layers.Count > 0) _streamingRenderer.Open(_document.Layers); else _streamingRenderer.Close();
            _surfaceRenderer.Upload(_document.Surface);
            throw;
        }
        _loadOperation.Cancel(); _surfaceOperation.Cancel(); _processing.Cancel();
        _selection.Reset();
        DiscardCrossSection();
        foreach (var old in _document.Layers) old.Store.Dispose();
        _document.Layers.Clear();
        _document.ReplaceResident(prepared.Dataset);
        _document.Layers.AddRange(prepared.Layers);
        prepared.Adopted = true; // renderer and selection keep the staged list; mapped-file ownership moved to this document
        if (prepared.State.Source is { } source)
        {
            _document.SetSource(source.Path, source.Format);
            _selection.SetSourceIdentity(source.Identity);
            _selection.SetSourceFilePath(source.Path, source.Format == "LAS");
            LoadDatasetIntoSelection();
            _cloudRadius = prepared.Dataset!.Radius;
        }
        else
        {
            _document.SetDisplayName(_document.Layers.Count == 0 ? "" : _document.Layers.Count == 1 ? _document.Layers[0].Name : $"{_document.Layers.Count} layers");
            _cloudRadius = _document.Layers.Count == 0 ? 50 : LayerRadius();
        }
        _selection.RestoreProject(prepared.Streamed, prepared.Annotations);
        LabelRegistry.LoadFrom(prepared.State.Definitions);
        foreach (var line in prepared.State.Polylines) AddPolyline(line, select: false);
        foreach (var line in prepared.State.PlanarPolylines) AddPlanarPolyline(line, select: false);
        _nextPolylineId = prepared.State.Polylines.Select(l => l.Id).Concat(prepared.State.PlanarPolylines.Select(l => l.Id)).DefaultIfEmpty(0).Max() + 1;
        _document.Sections.Replace(prepared.State.Sections, prepared.State.ActiveSectionId);
        _document.Measurements.Replace(prepared.State.Measurements);
        SetCrossSectionState(_document.Sections.Active);
        _navigation.Replace(prepared.State.NamedViews);
        _document.SetSurface(prepared.Surface);
        RestoreWorkspace(prepared.State.Workspace);
        UndoHistory.Clear();
        _highlightRenderer.MarkDirty();
    }

    private void RestoreWorkspace(ProjectWorkspace w)
    {
        _viewportLayout = (ViewportLayoutKind)w.Layout; _previousViewportLayout = (ViewportLayoutKind)w.PreviousLayout;
        _auxiliaryViewportView = (ViewportViewKind)w.AuxiliaryView; _activeViewportIndex = w.ActiveViewport;
        _forceAuxiliaryViewRefresh = false;
        UpdateViewportLayout();
        for (int i = 0; i < _viewports.Length; i++)
        {
            var v = _viewports[i]; var saved = w.Viewports[i];
            v.Camera.CancelTransition(); v.Camera.RestoreState(saved.Camera, animate: false);
            v.Input.SetPointSize(saved.PointSize); v.SectionMode = saved.SectionMode; v.IsInitialized = true;
        }
        _residentVisible = w.ResidentVisible; _surfaceVisible = w.SurfaceVisible; OrthoMode = w.Ortho;
        SetTool(w.Tool); SetMode(w.Mode); SetLabel(w.CurrentLabel); _selection.SetInstanceId(w.CurrentInstance);
        ExplorerVisible = w.ExplorerVisible; PropertiesVisible = w.PropertiesVisible; RibbonVisible = w.RibbonVisible;
        PalettesOnRight = w.PalettesOnRight; CommandLineVisible = w.CommandLineVisible; CommandLineFloating = w.CommandLineFloating;
        CommandHistoryVisible = w.HistoryVisible; LabelWindowVisible = w.LabelsVisible;
    }

    private void StartNewProject()
    {
        _projectRecovery.Clear();
        ProjectPath = "";
        _navigation.Replace(new Dictionary<string, OrbitCamera.CameraState>());
        _projectCheckpoint = ProjectSignature();
    }

    private void UpdateProjectRecovery()
    {
        _projectRecovery.Tick(() => HasUnsavedProject && !_projectOperation.IsRunning && !_loadOperation.IsRunning,
            ProjectSignature, () => (CaptureProject(), _document.Dataset, _document.Surface),
            message => _pendingWork.Enqueue(() => BackgroundMessage?.Invoke(message)));
    }
}
