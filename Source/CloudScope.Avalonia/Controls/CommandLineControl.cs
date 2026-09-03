using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using CloudScope.Commands;

namespace CloudScope.Avalonia.Controls;

/// <summary>
/// The AutoCAD-style command window: a single live command line beneath scrolling output,
/// autocomplete, input recall, and the space-vs-enter submission rule.  The prompt is rendered
/// beside the editable answer, never embedded in it: deleting an autocomplete candidate must not
/// be able to duplicate or corrupt the prompt text.
/// </summary>
public sealed class CommandLineControl : UserControl
{
    private readonly CommandLineSession _session;
    private readonly Func<string, Task> _submit;

    private readonly CommandTranscript _transcript;
    private readonly TextBox _input = new()
    {
        // Local values deliberately override the Fluent theme's focused TextBox state.
        // The CAD command line must not turn dark or acquire an accent outline on focus.
        Background = Brushes.White,
        BorderBrush = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        FocusAdorner = null
    };
    private readonly TextBlock _prompt = new()
    {
        Foreground = new SolidColorBrush(Color.FromRgb(0, 48, 150)),
        FontFamily = new FontFamily(global::CloudScope.Ui.UiPalette.MonoFontStack),
        FontSize = 12,
        FontWeight = FontWeight.Normal,
        HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
        VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
    };
    private readonly Popup _completionPopup = new();
    private readonly ListBox _completionList = new();

    private IReadOnlyList<CommandCompletion> _completions = [];
    private string _completionPrefix = "";
    private bool _settingInput;

    public CommandLineControl(CommandLineSession session, Func<string, Task> submit)
    {
        _session = session;
        _submit = submit;

        // Fluent styles the TextBox's internal PART_BorderElement directly for hover and
        // focus, bypassing the outer control values. Override those theme resources at the
        // TextBox itself so every state remains the same plain white CAD input line. Keeping
        // this local also makes it work after the control is moved into the floating window.
        _input.Resources["TextControlBackground"] = Brushes.White;
        _input.Resources["TextControlBackgroundPointerOver"] = Brushes.White;
        _input.Resources["TextControlBackgroundFocused"] = Brushes.White;
        _input.Resources["TextControlBorderBrush"] = Brushes.Transparent;
        _input.Resources["TextControlBorderBrushPointerOver"] = Brushes.Transparent;
        _input.Resources["TextControlBorderBrushFocused"] = Brushes.Transparent;
        _input.Resources["TextControlBorderThemeThickness"] = new Thickness(0);
        _input.Resources["TextControlBorderThemeThicknessPointerOver"] = new Thickness(0);
        _input.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(0);
        _prompt.IsHitTestVisible = false;
        _prompt.SizeChanged += (_, _) => SetPromptPadding();

        _transcript = new CommandTranscript(session, cadPalette: true);
        _transcript.CommandRecalled += Stage;

        _input.Classes.Add("commandInput");
        _input.KeyDown += OnInputKeyDown;
        _input.TextChanged += (_, _) =>
        {
            if (_settingInput)
                return;
            RefreshCompletions();
        };

        _completionList.Background = Brushes.White;
        _completionList.Foreground = Brushes.Black;

        _completionList.DoubleTapped += (_, _) => AcceptCompletion(submitAfter: true);
        _completionPopup.Child = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(145, 145, 145)),
            // The popup is placed immediately above the input.  Its bottom edge would read
            // as an unwanted grey stripe across the top of the TextBox while typing.
            BorderThickness = new Thickness(1, 1, 1, 0),
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

    /// <summary>Raised by the docked command strip's close button.</summary>
    public event Action? CloseRequested;

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
            ? Math.Clamp(_input.CaretIndex, 0, current.Length)
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
        var close = new Button { Content = "×" };
        close.Classes.Add("commandRailButton");
        ToolTip.SetTip(close, "Hide command line");
        close.Click += (_, _) => CloseRequested?.Invoke();

        var history = new Button { Content = "≡" };
        history.Classes.Add("commandRailButton");
        ToolTip.SetTip(history, "Command history (F2)");
        history.Click += (_, _) => HistoryRequested?.Invoke();

        var rail = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(184, 184, 184)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(145, 145, 145)),
            BorderThickness = new Thickness(0, 0, 1, 0),
            ClipToBounds = true,
            Padding = new Thickness(1, 0, 1, 2),
            Child = new StackPanel
            {
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Bottom,
                Children = { close, history }
            }
        };

        // The TextBox owns the complete width of the command row.  The prompt is only a
        // non-interactive overlay; padding moves typed text past it without shrinking the
        // actual edit control or leaving a differently styled strip at the left.
        var inputLine = new Grid();
        inputLine.Children.Add(_input);
        inputLine.Children.Add(_prompt);

        var inputBorder = new Border
        {
            Background = Brushes.White,
            // Keep the command entry visually continuous with the transcript.  A one-pixel
            // top border becomes conspicuous while the completion popup is dismissed by
            // Backspace, especially at Retina scaling.
            BorderThickness = new Thickness(0),
            Padding = new Thickness(7, 1, 8, 2),
            Child = inputLine
        };

        var content = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        content.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(199, 199, 199)),
            Child = _transcript
        });
        content.Children.Add(inputBorder);
        Grid.SetRow(inputBorder, 1);

        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("24,*") };
        root.Children.Add(rail);
        root.Children.Add(content);
        Grid.SetColumn(content, 1);
        return root;
    }

    private string InputText => _input.Text ?? "";

    private void SetPromptPrefix()
    {
        _prompt.Text = _session.Prompt.TrimEnd() + " ";
        SetPromptPadding();
    }

    private void SetPromptPadding()
    {
        double left = Math.Ceiling(_prompt.Bounds.Width);
        _input.Padding = new Thickness(left, 0, 0, 0);
    }

    private void SetInput(string text, bool caretAtEnd) => SetInput(text, caretAtEnd ? text.Length : 0);

    private void SetInput(string text, int caret)
    {
        _settingInput = true;
        try
        {
            int position = Math.Clamp(caret, 0, text.Length);
            _input.Text = text;
            _input.SelectionStart = position;
            _input.SelectionEnd = position;
            _input.CaretIndex = position;
        }
        finally { _settingInput = false; }
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Home:
                _input.CaretIndex = 0;
                e.Handled = true;
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

        // Suggestions live only in the popup. Injecting the best candidate into the TextBox
        // as selected text makes Backspace fight the autocomplete and can trap the input in
        // a re-suggestion loop. Tab or Enter still accepts the selected candidate.
        if (typed.Length == 0)
        {
            DismissCompletions();
            return;
        }

        _completions = _session.Complete(typed);

        if (_completions.Count == 0)
        {
            DismissCompletions();
            return;
        }

        _completionList.ItemsSource = _completions.Select(c => $"{c.Display}   ({c.Detail})").ToArray();
        _completionList.SelectedIndex = 0;
        _completionPopup.IsOpen = true;
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

}
