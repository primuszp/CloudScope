using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Threading;
using CloudScope.Commands;

namespace CloudScope.Avalonia.Controls;

/// <summary>
/// The AutoCAD-style command window: a single live command line beneath scrolling output,
/// autocomplete, input recall, and the space-vs-enter submission rule.  The prompt is an
/// immutable prefix of the input, so it reads as one console line rather than a label beside a
/// separate textbox.
/// </summary>
public sealed class CommandLineControl : UserControl
{
    private readonly CommandLineSession _session;
    private readonly Func<string, Task> _submit;

    private readonly CommandTranscript _transcript;
    private readonly TextBox _input = new();
    private readonly Popup _completionPopup = new();
    private readonly ListBox _completionList = new();

    private IReadOnlyList<CommandCompletion> _completions = [];
    private string _completionPrefix = "";
    private string _promptPrefix = "";
    private bool _suppressInlineSuggestion;
    private bool _settingInput;

    public CommandLineControl(CommandLineSession session, Func<string, Task> submit)
    {
        _session = session;
        _submit = submit;

        _transcript = new CommandTranscript(session);
        _transcript.CommandRecalled += Stage;

        _input.Classes.Add("commandInput");
        _input.KeyDown += OnInputKeyDown;
        _input.TextChanged += (_, _) =>
        {
            if (_settingInput)
                return;

            // The prompt is display text, not input.  A mouse selection or paste must not be
            // allowed to turn it into a command, so restore it whenever an edit crosses it.
            string displayed = _input.Text ?? "";
            if (!displayed.StartsWith(_promptPrefix, StringComparison.Ordinal))
            {
                // Clicking into the prefix and typing leaves the old prefix further right;
                // pull it back out rather than letting that display text become input.
                int prefixAt = displayed.IndexOf(_promptPrefix, StringComparison.Ordinal);
                string editedInput = prefixAt >= 0
                    ? displayed.Remove(prefixAt, _promptPrefix.Length)
                    : displayed.TrimStart();
                SetInput(editedInput, caretAtEnd: true);
                return;
            }

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
        // The list belongs directly above the text being typed, left-aligned with it, the way
        // AutoCAD's suggestion list sits over the prompt — not floating off the input's end.
        _completionPopup.PlacementTarget = _input;
        _completionPopup.Placement = PlacementMode.AnchorAndGravity;
        _completionPopup.PlacementAnchor = global::Avalonia.Controls.Primitives.PopupPositioning.PopupAnchor.TopLeft;
        _completionPopup.PlacementGravity = global::Avalonia.Controls.Primitives.PopupPositioning.PopupGravity.TopRight;
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

        var copy = new MenuItem { Header = "Copy" };
        copy.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                await clipboard.SetTextAsync(_transcript.SelectedOrAllText);
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
        _input.CaretIndex = (_input.Text ?? "").Length;
    }

    /// <summary>
    /// Takes a character typed while the command line did not have focus: the input takes
    /// focus and the character lands in it, so a command started from the viewport is not
    /// missing its first letter.
    /// </summary>
    public void BeginTyping(string text)
    {
        string current = InputText;
        int caret = IsKeyboardFocusWithin
            ? Math.Clamp(_input.CaretIndex - _promptPrefix.Length, 0, current.Length)
            : current.Length;

        SetInput(current[..caret] + text + current[caret..], caret + text.Length);
        _input.Focus();
        RefreshCompletions();
    }

    /// <summary>Puts a command in the input without submitting it, as menus and toolbars do.</summary>
    public void Stage(string command)
    {
        _session.Stage(command);
        SetInput(command, caretAtEnd: true);
        FocusInput();
    }

    /// <summary>Redraws the live prompt and history after the session state changed.</summary>
    public void Refresh()
    {
        SetPromptPrefix();
        _transcript.Sync();
    }

    public void Clear() => SetInput("", caretAtEnd: true);

    private Control BuildLayout()
    {
        var promptBorder = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(10, 3, 10, 4),
            Child = _input
        };
        promptBorder[!BorderBrushProperty] = ResourceBinding("CsBorder");
        promptBorder[!BackgroundProperty] = ResourceBinding("CsSurfaceDeep");

        var root = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        root.Children.Add(_transcript);
        root.Children.Add(promptBorder);
        Grid.SetRow(promptBorder, 1);
        return root;
    }

    private string InputText => (_input.Text ?? "").StartsWith(_promptPrefix, StringComparison.Ordinal)
        ? (_input.Text ?? "")[_promptPrefix.Length..]
        : "";

    private void SetPromptPrefix()
    {
        string input = InputText;
        _promptPrefix = _session.Prompt.TrimEnd() + " ";
        SetInput(input, caretAtEnd: true);
    }

    private void SetInput(string text, bool caretAtEnd) => SetInput(text, caretAtEnd ? text.Length : 0);

    private void SetInput(string text, int caret)
    {
        _settingInput = true;
        try
        {
            _input.Text = _promptPrefix + text;
            _input.CaretIndex = _promptPrefix.Length + Math.Clamp(caret, 0, text.Length);
        }
        finally { _settingInput = false; }
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Home:
                _input.CaretIndex = _promptPrefix.Length;
                e.Handled = true;
                return;

            case Key.Left:
                if (_input.CaretIndex <= _promptPrefix.Length)
                {
                    _input.CaretIndex = _promptPrefix.Length;
                    e.Handled = true;
                }
                return;

            case Key.Back:
            case Key.Delete:
                if (e.Key == Key.Back && _input.CaretIndex <= _promptPrefix.Length)
                {
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.Delete && _input.CaretIndex < _promptPrefix.Length)
                {
                    e.Handled = true;
                    return;
                }
                // Backspacing past a suggestion must delete, not re-suggest the same text.
                _suppressInlineSuggestion = true;
                Dispatcher.UIThread.Post(() => _suppressInlineSuggestion = false, DispatcherPriority.Input);
                return;

            case Key.Escape:
                // Esc peels one layer at a time: list, then the active command, then the text.
                if (_completionPopup.IsOpen) DismissCompletions();
                else if (_session.ActiveOptions != null || InputText.Length == 0) SubmitText("CANCEL");
                else SetInput("", caretAtEnd: true);
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

                SubmitText(InputText);
                e.Handled = true;
                return;

            case Key.Space:
                // Only submit on space where the active prompt does not take free-form text,
                // otherwise a quoted path or a multi-word value could never be typed.
                if (!_session.SpaceSubmits || InputText.Trim().Length == 0)
                    return;

                SubmitText(InputText.TrimEnd());
                e.Handled = true;
                return;
        }
    }

    private void SetInput(string text)
    {
        SetInput(text, caretAtEnd: true);
        DismissCompletions();
    }

    private void SubmitText(string command)
    {
        DismissCompletions();
        SetInput("", caretAtEnd: true);
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
        string typed = InputText.Trim();
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
            SetInput(typed + best[typed.Length..], caretAtEnd: true);
            _input.SelectionStart = _promptPrefix.Length + typed.Length;
            _input.SelectionEnd = _promptPrefix.Length + best.Length;
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
        SetInput(insert, caretAtEnd: true);
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
        _completionPrefix = InputText.Trim();
    }

    private static global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension ResourceBinding(string key) =>
        new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(key);
}
