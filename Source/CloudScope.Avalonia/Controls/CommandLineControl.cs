using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Threading;
using CloudScope.Commands;

namespace CloudScope.Avalonia.Controls;

/// <summary>
/// The AutoCAD-style command window: scrolling typed output, clickable prompt keywords,
/// autocomplete over prompt keywords and command names, input recall, and the space-vs-enter
/// submission rule. All behaviour comes from <see cref="CommandLineSession"/>, so the
/// GameWindow shell's command window behaves identically.
/// </summary>
public sealed class CommandLineControl : UserControl
{
    private readonly CommandLineSession _session;
    private readonly Func<string, Task> _submit;

    private readonly ItemsControl _historyList = new();
    private readonly ScrollViewer _historyScroll;
    private readonly WrapPanel _keywordPanel = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock _promptText = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _input = new();
    private readonly Popup _completionPopup = new();
    private readonly ListBox _completionList = new();

    private readonly global::System.Collections.ObjectModel.ObservableCollection<CommandLineEntry> _entries = [];
    private IReadOnlyList<CommandCompletion> _completions = [];
    private string _completionPrefix = "";
    private bool _suppressInlineSuggestion;
    private long _syncedTotal;

    public CommandLineControl(CommandLineSession session, Func<string, Task> submit)
    {
        _session = session;
        _submit = submit;

        _historyList.ItemsSource = _entries;
        _historyList.ItemTemplate = new FuncDataTemplate<CommandLineEntry>((entry, _) =>
            new SelectableTextBlock
            {
                Text = entry?.Text ?? "",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                FontFamily = MonoFont,
                Foreground = BrushFor(entry?.Kind ?? CommandEntryKind.Output)
            });

        _historyScroll = new ScrollViewer
        {
            Content = _historyList,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(10, 4, 10, 4)
        };

        _promptText.Classes.Add("prompt");
        _input.Classes.Add("commandInput");
        _input.PlaceholderText = "Type a command — Tab completes, ↑ recalls, F2 shows history";
        _input.KeyDown += OnInputKeyDown;
        _input.TextChanged += (_, _) =>
        {
            if (!_suppressInlineSuggestion)
                RefreshCompletions();
        };

        _completionList.DoubleTapped += (_, _) => AcceptCompletion(submitAfter: true);
        _completionPopup.Child = new Border
        {
            [!BackgroundProperty] = ResourceBinding("CsSurfaceDeep"),
            [!BorderBrushProperty] = ResourceBinding("CsBorder"),
            BorderThickness = new Thickness(1),
            Child = _completionList,
            MinWidth = 320,
            MaxHeight = 220
        };
        _completionPopup.PlacementTarget = _input;
        _completionPopup.Placement = PlacementMode.Top;
        _completionPopup.HorizontalOffset = 0;
        _completionPopup.IsLightDismissEnabled = false;

        Content = BuildLayout();
        ContextMenu = BuildContextMenu();
        Refresh();
    }

    /// <summary>Raised when the user asks for the expanded history window (F2).</summary>
    public event Action? HistoryRequested;

    // AutoCAD's command-line context menu: rerun something recent, or take the transcript.
    private ContextMenu BuildContextMenu()
    {
        var recentCommands = new MenuItem { Header = "Recent commands" };
        var recentInput = new MenuItem { Header = "Recent input" };

        var copy = new MenuItem { Header = "Copy history" };
        copy.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                await clipboard.SetTextAsync(_session.HistoryText);
        };

        var clear = new MenuItem { Header = "Clear history" };
        clear.Click += (_, _) =>
        {
            _session.ClearHistory();
            Refresh();
        };

        var history = new MenuItem { Header = "Expanded history\tF2" };
        history.Click += (_, _) => HistoryRequested?.Invoke();

        var menu = new ContextMenu
        {
            ItemsSource = new object[]
            {
                recentCommands, recentInput, new Separator(), copy, clear, new Separator(), history
            }
        };

        // The lists are rebuilt on open so they always show what was actually run.
        menu.Opening += (_, _) =>
        {
            FillCommandList(recentCommands, _session.RecentCommands.Take(12));
            FillCommandList(recentInput, _session.RecentInput.Take(12));
        };

        return menu;
    }

    private void FillCommandList(MenuItem parent, IEnumerable<string> commands)
    {
        var items = new List<MenuItem>();
        foreach (string command in commands)
        {
            var item = new MenuItem { Header = command };
            item.Click += (_, _) => SubmitText(command);
            items.Add(item);
        }

        parent.ItemsSource = items;
        parent.IsEnabled = items.Count > 0;
    }

    public void FocusInput()
    {
        _input.Focus();
        _input.CaretIndex = _input.Text?.Length ?? 0;
    }

    /// <summary>Puts a command in the input without submitting it, as menus and toolbars do.</summary>
    public void Stage(string command)
    {
        _session.Stage(command);
        _input.Text = command;
        FocusInput();
    }

    /// <summary>Redraws prompt, keywords and history after the session state changed.</summary>
    public void Refresh()
    {
        _promptText.Text = _session.Prompt;
        SyncHistory();
        RebuildKeywords();
        ScrollHistoryToEnd();
    }

    public void Clear() => _input.Text = "";

    // Append only what is new. Rebuilding the transcript would re-template every visible
    // row on every command, which is the one thing a command line must never do.
    private void SyncHistory()
    {
        long total = _session.TotalEntries;
        if (total == _syncedTotal)
            return;

        IReadOnlyList<CommandLineEntry> history = _session.History;
        long added = total - _syncedTotal;
        if (added >= history.Count || _syncedTotal > total)
        {
            // More lines arrived than the session keeps (or it was cleared): resync fully.
            _entries.Clear();
            foreach (CommandLineEntry entry in history)
                _entries.Add(entry);
        }
        else
        {
            for (int i = history.Count - (int)added; i < history.Count; i++)
                _entries.Add(history[i]);

            while (_entries.Count > _session.HistoryLimit)
                _entries.RemoveAt(0);
        }

        _syncedTotal = total;
    }

    private static readonly FontFamily MonoFont = new(global::CloudScope.Ui.UiPalette.MonoFontStack);

    private Control BuildLayout()
    {
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Height = 22,
            Children =
            {
                new TextBlock { Text = "COMMAND", Classes = { "sectionTitle" }, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = "F2  HISTORY", Classes = { "sectionTitle" }, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, [Grid.ColumnProperty] = 2 }
            }
        };
        header[!BackgroundProperty] = ResourceBinding("CsSurfaceAlt");

        var promptRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Margin = new Thickness(10, 2, 10, 4),
            Children = { _promptText, _input }
        };
        Grid.SetColumn(_input, 1);

        var promptBorder = new Border
        {
            BorderThickness = new Thickness(2, 1, 0, 0),
            Child = new StackPanel
            {
                Children =
                {
                    new Border { Child = _keywordPanel, Margin = new Thickness(10, 4, 10, 0) },
                    promptRow,
                    _completionPopup
                }
            }
        };
        promptBorder[!BorderBrushProperty] = ResourceBinding("CsAccent");
        promptBorder[!BackgroundProperty] = ResourceBinding("CsSurface");

        var root = new Grid { RowDefinitions = new RowDefinitions("22,*,Auto") };
        root.Children.Add(header);
        root.Children.Add(_historyScroll);
        root.Children.Add(promptBorder);
        Grid.SetRow(_historyScroll, 1);
        Grid.SetRow(promptBorder, 2);
        return root;
    }

    // Prompt keywords are buttons: clicking one is exactly the same as typing it.
    private void RebuildKeywords()
    {
        _keywordPanel.Children.Clear();
        PromptOptions? options = _session.ActiveOptions;
        if (options == null || options.Keywords.Count == 0)
        {
            _keywordPanel.IsVisible = false;
            return;
        }

        _keywordPanel.IsVisible = true;
        foreach (Keyword keyword in options.Keywords)
        {
            bool isDefault = string.Equals(keyword.GlobalName, options.DefaultKeyword, StringComparison.OrdinalIgnoreCase);
            var button = new Button
            {
                Content = BuildKeywordText(keyword),
                Classes = { "keywordLink" },
                Tag = keyword.GlobalName
            };

            if (isDefault)
                button.Classes.Add("defaultKeyword");

            if (keyword.Abbreviation.Length > 0)
                ToolTip.SetTip(button, $"Type {keyword.Abbreviation}");

            button.Click += (_, _) => SubmitText(keyword.GlobalName);
            _keywordPanel.Children.Add(button);
        }
    }

    // AutoCAD underlines the capitalised letters of a keyword to show what may be typed
    // instead of the whole word ("ReTurn" accepts "RT").
    private static TextBlock BuildKeywordText(Keyword keyword)
    {
        var block = new TextBlock { FontSize = 11 };
        foreach (KeywordSegment segment in keyword.DisplaySegments)
        {
            block.Inlines?.Add(new Run(segment.Text)
            {
                TextDecorations = segment.IsAbbreviation ? TextDecorations.Underline : null
            });
        }

        return block;
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Back:
            case Key.Delete:
                // Backspacing past a suggestion must delete, not re-suggest the same text.
                _suppressInlineSuggestion = true;
                Dispatcher.UIThread.Post(() => _suppressInlineSuggestion = false, DispatcherPriority.Input);
                return;

            case Key.Escape:
                // Esc peels one layer at a time: list, then the active command, then the text.
                if (_completionPopup.IsOpen) DismissCompletions();
                else if (_session.ActiveOptions != null || (_input.Text ?? "").Length == 0) SubmitText("CANCEL");
                else _input.Text = "";
                e.Handled = true;
                return;

            case Key.Tab:
                CycleCompletion(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
                e.Handled = true;
                return;

            case Key.Up:
                if (_completionPopup.IsOpen) CycleCompletion(-1);
                else SetInput(_session.Recall(-1));
                e.Handled = true;
                return;

            case Key.Down:
                if (_completionPopup.IsOpen) CycleCompletion(1);
                else SetInput(_session.Recall(1));
                e.Handled = true;
                return;

            case Key.F2:
                HistoryRequested?.Invoke();
                e.Handled = true;
                return;

            case Key.Enter:
                if (_completionPopup.IsOpen && _completionList.SelectedIndex >= 0)
                {
                    AcceptCompletion(submitAfter: true);
                    e.Handled = true;
                    return;
                }

                SubmitText(_input.Text ?? "");
                e.Handled = true;
                return;

            case Key.Space:
                // Only submit on space where the active prompt does not take free-form text,
                // otherwise a quoted path or a multi-word value could never be typed.
                if (!_session.SpaceSubmits || (_input.Text ?? "").Trim().Length == 0)
                    return;

                SubmitText((_input.Text ?? "").TrimEnd());
                e.Handled = true;
                return;
        }
    }

    private void SetInput(string text)
    {
        _input.Text = text;
        _input.CaretIndex = text.Length;
        DismissCompletions();
    }

    private void SubmitText(string command)
    {
        DismissCompletions();
        _input.Text = "";
        _ = SubmitAsync(command);
    }

    private async Task SubmitAsync(string command)
    {
        await _submit(command);
        Refresh();
        FocusInput();
    }

    private void RefreshCompletions()
    {
        string typed = (_input.Text ?? "").Trim();
        if (typed == _completionPrefix)
            return;

        _completionPrefix = typed;
        _completions = _session.Complete(typed);

        if (_completions.Count == 0)
        {
            DismissCompletions();
            return;
        }

        _completionList.ItemsSource = _completions.Select(c => $"{c.Display}   ({c.Detail})").ToArray();
        _completionList.SelectedIndex = 0;
        _completionPopup.IsOpen = true;
        ApplyInlineSuggestion(typed);
    }

    // The best candidate's remainder is inserted as selected text, so typing over it replaces
    // it and Enter accepts it — AutoCAD's inline autocomplete.
    private void ApplyInlineSuggestion(string typed)
    {
        if (_suppressInlineSuggestion || typed.Length == 0)
            return;

        string best = _completions[0].Insert;
        if (best.Length <= typed.Length ||
            !best.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
            return;

        _suppressInlineSuggestion = true;
        try
        {
            _input.Text = typed + best[typed.Length..];
            _input.SelectionStart = typed.Length;
            _input.SelectionEnd = best.Length;
            _completionPrefix = typed;
        }
        finally
        {
            _suppressInlineSuggestion = false;
        }
    }

    private void CycleCompletion(int direction)
    {
        if (!_completionPopup.IsOpen)
        {
            RefreshCompletions();
            if (!_completionPopup.IsOpen)
                return;
        }

        int count = _completions.Count;
        _completionList.SelectedIndex = (_completionList.SelectedIndex + direction + count) % count;
        AcceptCompletion(submitAfter: false);
    }

    private void AcceptCompletion(bool submitAfter)
    {
        int index = _completionList.SelectedIndex;
        if (index < 0 || index >= _completions.Count)
            return;

        string insert = _completions[index].Insert;
        _input.Text = insert;
        _input.CaretIndex = insert.Length;
        _completionPrefix = insert;

        if (submitAfter)
            SubmitText(insert);
        else
            _input.Focus();
    }

    private void DismissCompletions()
    {
        _completionPopup.IsOpen = false;
        _completions = [];
        _completionPrefix = (_input.Text ?? "").Trim();
    }

    // Only follow the tail when the user is already reading the tail — never yank the view
    // away from history they scrolled back to.
    private void ScrollHistoryToEnd()
    {
        bool atBottom = _historyScroll.Offset.Y >= _historyScroll.Extent.Height - _historyScroll.Viewport.Height - 4;
        _historyList.InvalidateMeasure();
        if (atBottom)
            Dispatcher.UIThread.Post(() => _historyScroll.ScrollToEnd(), DispatcherPriority.Background);
    }

    // One frozen brush per kind: history lines are re-templated often and a new brush per
    // line would allocate for every visible row.
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

    private static global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension ResourceBinding(string key) =>
        new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(key);
}
