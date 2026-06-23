using System;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CloudScope.Avalonia.Hosting;
using CloudScope.Labeling;

namespace CloudScope.Avalonia;

/// <summary>
/// Maps label names to LAS classification codes and colors. Reads the live registry from the
/// host and applies edits through the same command pipeline as the rest of the shell, so the
/// ImGui viewer and this window always stay in sync.
/// </summary>
public sealed class LabelRegistryWindow : Window
{
    private readonly HostController _host;
    private readonly Action<string> _run;
    private readonly StackPanel _list = new() { Spacing = 2 };
    private readonly TextBox _nameBox = new() { PlaceholderText = "Label name", Width = 180 };
    private readonly NumericUpDown _codeBox = new() { Minimum = 0, Maximum = 255, Value = 6, Width = 110 };

    public LabelRegistryWindow(HostController host, Action<string> run)
    {
        _host = host;
        _run = run;

        Title = "Label registry";
        Width = 380;
        Height = 460;
        Background = new SolidColorBrush(Color.FromRgb(0x25, 0x2C, 0x33));

        var define = new Button { Content = "Define / update" };
        define.Click += (_, _) => DefineFromInputs();

        var saveLas = new Button { Content = "Save labels to LAS" };
        saveLas.Click += (_, _) => { _run("SAVELABELS LAS"); };

        var root = new StackPanel { Margin = new global::Avalonia.Thickness(12), Spacing = 10 };
        root.Children.Add(new TextBlock
        {
            Text = "Map label names to LAS classification codes. Set instances with INSTANCE <id> or LABEL \"name\" <id>.",
            Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xB6, 0xC2))
        });
        root.Children.Add(new ScrollViewer
        {
            Height = 250,
            Content = _list
        });
        root.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(0x46, 0x51, 0x5B)) });
        root.Children.Add(new TextBlock { Text = "Add / update", Foreground = Brushes.White });
        var entry = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        entry.Children.Add(_nameBox);
        entry.Children.Add(_codeBox);
        entry.Children.Add(define);
        root.Children.Add(entry);
        root.Children.Add(saveLas);

        Content = root;
        Opened += (_, _) => Refresh();
    }

    public void Refresh()
    {
        _list.Children.Clear();
        string active = _host.ActiveLabel;
        foreach (LabelDefinition def in _host.LabelDefinitions)
        {
            _list.Children.Add(BuildRow(def, string.Equals(def.Name, active, StringComparison.OrdinalIgnoreCase)));
        }
    }

    private Button BuildRow(LabelDefinition def, bool active)
    {
        var swatch = new Border
        {
            Width = 14,
            Height = 14,
            CornerRadius = new global::Avalonia.CornerRadius(2),
            Background = new SolidColorBrush(Color.FromRgb(
                (byte)(def.Color.X * 255f), (byte)(def.Color.Y * 255f), (byte)(def.Color.Z * 255f)))
        };

        var text = new TextBlock
        {
            Text = $"{def.Name}  (class {def.Code}){(active ? "  •" : "")}",
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        };

        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        content.Children.Add(swatch);
        content.Children.Add(text);

        var row = new Button
        {
            Content = content,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = active ? new SolidColorBrush(Color.FromRgb(0x33, 0x3E, 0x49)) : Brushes.Transparent
        };
        string name = def.Name;
        row.Click += (_, _) => { _run($"LABEL \"{name}\""); Refresh(); };
        return row;
    }

    private void DefineFromInputs()
    {
        string name = _nameBox.Text?.Trim() ?? "";
        if (name.Length == 0)
            return;

        int code = (int)(_codeBox.Value ?? 0m);
        _run($"LABELDEF \"{name}\" {code}");
        _run($"LABEL \"{name}\"");
        Refresh();
    }
}
