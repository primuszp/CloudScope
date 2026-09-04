using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using CloudScope.Commands;

namespace CloudScope.Avalonia.Controls;

/// <summary>
/// The AutoCAD-style command window used by the WPF shell: scrolling output above a fixed
/// prompt and a separate input field.  Completion, input recall, and the space-vs-enter rule
/// remain shell behaviour rather than being tied to the visual arrangement.
/// </summary>
public sealed class CommandLineControl : UserControl
{
    private readonly CommandLineSession _session;
    private readonly Func<string, Task> _submit;

    private readonly CommandTranscript _transcript;
    private readonly TextBlock _prompt = new()
    {
        Foreground = Brushes.Black,
        VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
        FontFamily = new FontFamily(global::CloudScope.Ui.UiPalette.MonoFontStack),
        FontSize = 12
    };
    private readonly TextBox _input = new()
    {
        // Local values deliberately override the Fluent theme's focused TextBox state.
        // The CAD command line must not turn dark or acquire an accent outline on focus.
        Background = Brushes.White,
        BorderBrush = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        FocusAdorner = null,
        TextWrapping = TextWrapping.Wrap
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
        _input.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(0);

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
        var inputRow = new DockPanel { LastChildFill = true };
        inputRow.Children.Add(_prompt);
        DockPanel.SetDock(_prompt, Dock.Left);
        inputRow.Children.Add(_input);

        // Mirrors AeroCAD.View's WPF command control: the live prompt is pinned below the
        // history, followed by a thin separator and a plain scrolling transcript.
        var inputBorder = new Border
        {
            Background = Brushes.White,
            Padding = new Thickness(4, 2),
            Child = inputRow
        };

        var content = new Grid { RowDefinitions = new RowDefinitions("*,1,Auto") };
        content.Children.Add(new Border
        {
            Background = Brushes.White,
            Child = _transcript
        });
        content.Children.Add(new Border
        {
            Margin = new Thickness(2, 0),
            Background = new SolidColorBrush(Color.FromRgb(208, 208, 216))
        });
        Grid.SetRow(content.Children[^1], 1);
        content.Children.Add(inputBorder);
        Grid.SetRow(inputBorder, 2);
        return content;
    }

    private string InputText => _input.Text ?? "";

    private void SetPromptPrefix()
    {
        _prompt.Text = _session.Prompt.TrimEnd();
        _prompt.Margin = new Thickness(0, 0, string.IsNullOrEmpty(_prompt.Text) ? 0 : 6, 0);
    }

    private void SetInput(string text, bool caretAtEnd) => SetInput(text, caretAtEnd ? text.Length : 0);

    private void SetInput(string text, int caret)
    {
        _settingInput = true;
        try
        {
            _input.Text = text;
            _input.CaretIndex = Math.Clamp(caret, 0, text.Length);
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

            case Key.Left:
                if (_input.CaretIndex <= 0)
                {
                    _input.CaretIndex = 0;
                    e.Handled = true;
                }
                return;

            case Key.Back:
                if (_input.CaretIndex <= 0)
                {
                    e.Handled = true;
                    return;
                }
                return;

            case Key.Delete:
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
