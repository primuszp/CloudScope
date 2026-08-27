using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using CloudScope.Commands;

namespace CloudScope.Avalonia.Controls;

/// <summary>
/// The command transcript, as both the docked command window and the expanded history window
/// show it. It is one text block rather than a list of rows, because AutoCAD's transcript is
/// one piece of text: a selection may run across lines, and what is copied is what was typed
/// and answered, not whichever row the pointer happened to land on.
/// </summary>
public sealed class CommandTranscript : UserControl
{
    private readonly SelectableTextBlock _text = new()
    {
        FontFamily = new FontFamily(global::CloudScope.Ui.UiPalette.MonoFontStack),
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap
    };

    private readonly ScrollViewer _scroll;
    private readonly CommandLineSession _session;
    private readonly bool _cadPalette;
    private readonly List<CommandLineEntry> _shown = [];
    private long _syncedTotal;

    public CommandTranscript(CommandLineSession session, bool cadPalette = false)
    {
        _session = session;
        _cadPalette = cadPalette;

        _scroll = new ScrollViewer
        {
            Content = _text,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(10, 4, 10, 4)
        };

        // The text block selects a word on a double tap and marks the event handled, so the
        // recall has to be listening for handled events to hear it at all.
        _text.AddHandler(DoubleTappedEvent, OnDoubleTapped, handledEventsToo: true);

        Content = _scroll;
        Sync();
    }

    /// <summary>
    /// Raised with a command the user recalled by double-clicking the line that echoed it.
    /// </summary>
    public event Action<string>? CommandRecalled;

    // AutoCAD recalls a command by double-clicking it in the history. What the user gets back
    // is the command in the input, not the command run: a transcript is full of things that
    // should not happen again because a pointer landed twice in the same place.
    private void OnDoubleTapped(object? sender, global::Avalonia.Input.TappedEventArgs e)
    {
        // The double tap has already selected the word under the pointer, and where that
        // selection starts is the character the user pointed at. Hit-testing the layout
        // ourselves does not work here: a text block built from inlines reports a single
        // line, so every point would resolve to the first entry.
        if (EntryAt(_text.SelectionStart) is not { EchoedCommand: { } command })
            return;

        CommandRecalled?.Invoke(command);
    }

    private CommandLineEntry? EntryAt(int position)
    {
        int start = 0;
        foreach (CommandLineEntry entry in _shown)
        {
            // Each entry occupies its own text plus the newline that ends its run.
            int length = entry.Text.Length + Environment.NewLine.Length;
            if (position < start + length)
                return entry;

            start += length;
        }

        return null;
    }

    /// <summary>
    /// Follows the session for as long as this control is attached. The history window used to
    /// carry a "refresh" button for want of this, which meant it could show a stale transcript
    /// of a session that was still running.
    /// </summary>
    protected override void OnAttachedToVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _session.HistoryChanged += OnHistoryChanged;
        Sync();
    }

    protected override void OnDetachedFromVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _session.HistoryChanged -= OnHistoryChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnHistoryChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
            Sync();
        else
            Dispatcher.UIThread.Post(Sync);
    }

    /// <summary>Everything the transcript holds, for "copy all".</summary>
    public string AllText => _session.HistoryText;

    /// <summary>What the user selected, falling back to the whole transcript.</summary>
    public string SelectedOrAllText =>
        _text.SelectedText.Length > 0 ? _text.SelectedText : AllText;

    /// <summary>
    /// Appends the lines that arrived since the last call. Rebuilding the transcript per
    /// command would re-lay-out every line that is already on screen, which is the one thing
    /// a command line must never do.
    /// </summary>
    public void Sync()
    {
        long total = _session.TotalEntries;
        if (total == _syncedTotal)
            return;

        InlineCollection inlines = _text.Inlines ??= [];
        IReadOnlyList<CommandLineEntry> history = _session.History;
        long added = total - _syncedTotal;

        // More lines arrived than the session keeps (or it was cleared): start again.
        if (added >= history.Count || _syncedTotal > total)
        {
            inlines.Clear();
            _shown.Clear();
            foreach (CommandLineEntry entry in history)
            {
                inlines.Add(RunFor(entry));
                _shown.Add(entry);
            }
        }
        else
        {
            for (int i = history.Count - (int)added; i < history.Count; i++)
            {
                inlines.Add(RunFor(history[i]));
                _shown.Add(history[i]);
            }

            while (inlines.Count > _session.HistoryLimit)
            {
                inlines.RemoveAt(0);
                _shown.RemoveAt(0);
            }
        }

        _syncedTotal = total;
        ScrollToTail();
    }

    private Run RunFor(CommandLineEntry entry) =>
        new(entry.Text + Environment.NewLine)
        {
            Foreground = _cadPalette ? CadBrushFor(entry.Kind) : BrushFor(entry.Kind)
        };

    // Only follow the tail when the user is already reading the tail — never yank the view
    // away from history they scrolled back to.
    private void ScrollToTail()
    {
        bool atBottom = _scroll.Offset.Y >= _scroll.Extent.Height - _scroll.Viewport.Height - 4;
        if (atBottom)
            Dispatcher.UIThread.Post(() => _scroll.ScrollToEnd(), DispatcherPriority.Background);
    }

    // One frozen brush per kind: a new brush per line would allocate for every line of output.
    private static readonly Dictionary<CommandEntryKind, IBrush> EntryBrushes =
        Enum.GetValues<CommandEntryKind>().ToDictionary(
            kind => kind,
            kind =>
            {
                uint color = global::CloudScope.Ui.UiPalette.EntryColor(kind);
                return (IBrush)new SolidColorBrush(Color.FromRgb(
                    global::CloudScope.Ui.UiPalette.R(color),
                    global::CloudScope.Ui.UiPalette.G(color),
                    global::CloudScope.Ui.UiPalette.B(color))).ToImmutable();
            });

    private static IBrush BrushFor(CommandEntryKind kind) => EntryBrushes[kind];

    private static IBrush CadBrushFor(CommandEntryKind kind) => kind switch
    {
        CommandEntryKind.Prompt => CadPromptBrush,
        CommandEntryKind.Error => CadErrorBrush,
        CommandEntryKind.Banner => CadDimBrush,
        _ => CadTextBrush
    };

    private static readonly IBrush CadTextBrush = new SolidColorBrush(Color.FromRgb(20, 20, 20)).ToImmutable();
    private static readonly IBrush CadPromptBrush = new SolidColorBrush(Color.FromRgb(0, 48, 150)).ToImmutable();
    private static readonly IBrush CadErrorBrush = new SolidColorBrush(Color.FromRgb(160, 0, 0)).ToImmutable();
    private static readonly IBrush CadDimBrush = new SolidColorBrush(Color.FromRgb(90, 90, 90)).ToImmutable();
}
