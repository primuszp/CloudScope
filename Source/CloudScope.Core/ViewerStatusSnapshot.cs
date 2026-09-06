using CloudScope.Loading;
using CloudScope.Selection;

namespace CloudScope;

/// <summary>
/// Everything the shells' status bars and properties panels display, captured in one
/// immutable value. The viewer publishes it whenever state changes so no UI has to poll
/// the controller or keep its own copy — the Avalonia inspector and the ImGui properties
/// panel render the same snapshot.
/// </summary>
/// <remarks>
/// Viewport layout and view are strings because their enums live in the viewer project;
/// the shells only display them. It is a struct because the panel-rendered viewer reads it
/// once per frame, where a class would put a short-lived allocation on the render loop's
/// GC path for no benefit.
/// </remarks>
public readonly record struct ViewerStatusSnapshot
{
    public ViewerStatusSnapshot() { }

    public static readonly ViewerStatusSnapshot Empty = new();

    /// <summary>File name of the loaded point cloud, empty when nothing is loaded.</summary>
    public string SourceName { get; init; } = "";

    /// <summary>Points loaded into the viewer.</summary>
    public int LoadedCount { get; init; }

    /// <summary>Points passing the active filter (equals <see cref="LoadedCount"/> when unfiltered).</summary>
    public int VisibleCount { get; init; }

    /// <summary>Description of the active point filter, empty when none.</summary>
    public string Filter { get; init; } = "";

    /// <summary>Active cross-section description, empty when section display is off.</summary>
    public string CrossSection { get; init; } = "";

    public InteractionMode Mode { get; init; } = InteractionMode.Navigate;
    public SelectionToolType ActiveTool { get; init; } = SelectionToolType.Box;
    public SelectionInteractionState InteractionState { get; init; } = SelectionInteractionState.Navigate;
    public bool HasActiveSelection { get; init; }

    public string CurrentLabel { get; init; } = "";
    public int? CurrentInstanceId { get; init; }

    public ColorSource ColorSource { get; init; } = ColorSource.Rgb;
    public float PointSize { get; init; } = 1f;
    public bool IsPerspective { get; init; } = true;

    /// <summary>Display name of the active viewport layout, e.g. "Single" or "Two vertical".</summary>
    public string ViewportLayout { get; init; } = "Single";

    /// <summary>Display name of the active viewport's standard view, e.g. "Top".</summary>
    public string ViewName { get; init; } = "Perspective";

    public float Fps { get; init; }

    /// <summary>Whether the viewer wants the label registry shown. Shells project it; the
    /// LABELS command is the only thing that sets it, so both shells agree on what it means.</summary>
    public bool LabelWindowVisible { get; init; }

    /// <summary>Whether the viewer wants the expanded command history shown.</summary>
    public bool CommandHistoryVisible { get; init; }

    /// <summary>
    /// Whether the docked command window is shown. It starts visible and only COMMANDLINE
    /// changes it, so a shell never hides the command line on its own.
    /// </summary>
    public bool CommandLineVisible { get; init; } = true;

    /// <summary>Whether the command window floats free of the workspace instead of docking.</summary>
    public bool CommandLineFloating { get; init; }

    /// <summary>
    /// Whether the viewer has been asked to close (QUIT). A shell that owns its own window
    /// closes it when it sees this; the GameWindow shell ends its loop on the same flag.
    /// </summary>
    public bool CloseRequested { get; init; }

    /// <summary>Percentage of a load in progress, or -1 when nothing is loading.</summary>
    public int LoadProgress { get; init; } = -1;

    public bool IsLoading => LoadProgress >= 0;

    public bool HasCloud => LoadedCount > 0;

    public string InstanceText => CurrentInstanceId is int id ? id.ToString() : "none";

    public string ProjectionText => IsPerspective ? "Perspective" : "Parallel";

    public string PointCountText => VisibleCount == LoadedCount
        ? $"{LoadedCount:N0}"
        : $"{VisibleCount:N0} / {LoadedCount:N0}";
}
