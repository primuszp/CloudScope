using System.Globalization;
using System.Text;
using Avalonia.Controls;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Media;
using CloudScope.Commands;
using CloudScope.Ui;

namespace CloudScope.Avalonia.Controls;

/// <summary>
/// CloudScope's line-icon set: 24-unit stroke drawings, one per <see cref="RibbonIcons"/> key
/// plus a few the explorer uses. They are geometry rather than font glyphs so they look the
/// same on Windows and macOS whatever symbol fonts happen to be installed.
/// </summary>
/// <remarks>
/// Every path carries the <c>icon</c> class, so its stroke comes from the control theme and
/// follows the owning button's state (idle, active, disabled). Only the data-colour icons —
/// the colour-by modes, whose whole point is the colour — tint individual strokes locally.
/// </remarks>
public static class Icons
{
    public const string Camera = "camera";
    public const string Folder = "folder";
    public const string Eye = RibbonIcons.Visible;
    public const string EyeOff = RibbonIcons.Hidden;
    public const string ChevronDown = "chevron-down";
    public const string ChevronUp = "chevron-up";

    private static readonly Dictionary<string, (string Data, uint? Tint)[]> Paths = Build();

    // Drawings made of dots read as points only when the dots are solid.
    private static readonly HashSet<string> Filled = [RibbonIcons.Cloud, RibbonIcons.Thin, RibbonIcons.Point];

    /// <summary>A square icon of <paramref name="size"/> logical pixels with a ~1.5px stroke.</summary>
    public static Control Create(string key, double size = 16)
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        double stroke = 1.5 * 24 / size;

        if (!Paths.TryGetValue(key, out (string Data, uint? Tint)[]? parts))
            parts = Paths[RibbonIcons.Info];

        foreach ((string data, uint? tint) in parts)
        {
            var path = new Path
            {
                Data = StreamGeometry.Parse(data),
                StrokeThickness = stroke,
                StrokeLineCap = PenLineCap.Round,
                StrokeJoin = PenLineJoin.Round,
                Classes = { "icon" }
            };

            if (Filled.Contains(key))
                path.Classes.Add("filled");

            if (tint is uint color)
                path.Stroke = new SolidColorBrush(Color.FromRgb(UiPalette.R(color), UiPalette.G(color), UiPalette.B(color)));

            canvas.Children.Add(path);
        }

        return new Viewbox
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            Child = canvas,
            IsHitTestVisible = false
        };
    }

    /// <summary>A small closed circle — the building block of the point-cloud drawings.</summary>
    private static string Dot(double x, double y, double r = 1.1) => string.Create(CultureInfo.InvariantCulture,
        $"M{x - r} {y} A{r} {r} 0 1 0 {x + r} {y} A{r} {r} 0 1 0 {x - r} {y} Z ");

    private static string Circle(double x, double y, double r) => Dot(x, y, r);

    private static string Dots(params (double X, double Y)[] points)
    {
        var builder = new StringBuilder();
        foreach ((double x, double y) in points)
            builder.Append(Dot(x, y));
        return builder.ToString();
    }

    private static (string, uint?)[] One(string data) => [(data, null)];

    private static Dictionary<string, (string Data, uint? Tint)[]> Build()
    {
        const string frame = "M3 4 H21 V20 H3 Z ";
        const string cube = "M12 2 L21 7 V17 L12 22 L3 17 V7 Z M3 7 L12 12 L21 7 M12 12 V22";
        const string eye = "M2 12 C5 6 19 6 22 12 C19 18 5 18 2 12 Z ";
        const string corners = "M3 8 V3 H8 M16 3 H21 V8 M21 16 V21 H16 M8 21 H3 V16 ";

        string cloud = Dots((6, 14), (9, 10), (12, 13), (15, 9), (18, 12), (8, 17), (13, 17.5), (17, 16.5), (11, 6.5), (20, 8));

        var map = new Dictionary<string, (string, uint?)[]>(StringComparer.Ordinal)
        {
            [RibbonIcons.Open] = One("M3 6 H9 L11 8 H21 V19 H3 Z M3 11 H21"),
            [Folder] = One("M3 6 H9 L11 8 H21 V19 H3 Z"),
            [RibbonIcons.Import] = One("M12 3 V14 M7 9 L12 14 L17 9 M4 16 V20 H20 V16"),
            [RibbonIcons.Export] = One("M12 14 V3 M7 8 L12 3 L17 8 M4 16 V20 H20 V16"),
            [RibbonIcons.Scanner] = One("M9 3 H15 V10 H9 Z M12 6.5 H12.01 M12 10 V14 L7 21 M12 14 L17 21 M12 14 V21"),
            [RibbonIcons.Layers] = One("M12 3 L21 8 L12 13 L3 8 Z M3 12 L12 17 L21 12 M3 16 L12 21 L21 16"),
            [RibbonIcons.Cloud] = One(cloud),
            [RibbonIcons.Navigate] = One("M5 3 L19 11 L12.5 13 L9.5 20 Z"),
            [RibbonIcons.Label] = One("M3 3 H11 L21 13 L13 21 L3 11 Z " + Circle(7.5, 7.5, 1.5)),
            [RibbonIcons.Box] = One("M5 5 H19 V19 H5 Z M3 3 H7 V7 H3 Z M17 3 H21 V7 H17 Z M17 17 H21 V21 H17 Z M3 17 H7 V21 H3 Z"),
            [RibbonIcons.Sphere] = One("M3 12 A9 9 0 1 0 21 12 A9 9 0 1 0 3 12 Z M3 12 A9 3.5 0 0 0 21 12"),
            [RibbonIcons.Cylinder] = One("M5 6 A7 3 0 0 0 19 6 A7 3 0 0 0 5 6 Z M5 6 V18 A7 3 0 0 0 19 18 V6"),
            [RibbonIcons.Confirm] = One("M4 12 L10 18 L20 6"),
            [RibbonIcons.Cancel] = One("M6 6 L18 18 M18 6 L6 18"),
            [RibbonIcons.Erase] = One("M15 3 L21 9 L11 19 H6 L3 16 Z M9 9 L15 15 M11 19 H21"),
            [RibbonIcons.Move] = One("M12 3 V21 M3 12 H21 M9 6 L12 3 L15 6 M9 18 L12 21 L15 18 M6 9 L3 12 L6 15 M18 9 L21 12 L18 15"),
            [RibbonIcons.Rotate] = One("M20 12 A8 8 0 1 1 17.7 6.3 M20 3 V8 H15"),
            [RibbonIcons.Scale] = One("M3 21 H12 V12 H3 Z M12 12 L21 3 M15 3 H21 V9"),
            [RibbonIcons.Fit] = One(corners + Circle(12, 12, 3.5)),
            [RibbonIcons.Ground] = One("M2 20 H22 M2 16 L7 11 L11 14 L16 8 L22 14"),
            [RibbonIcons.Undo] = One("M9 14 L4 9 L9 4 M4 9 H15 A5 5 0 0 1 15 19 H11"),
            [RibbonIcons.Redo] = One("M15 14 L20 9 L15 4 M20 9 H9 A5 5 0 0 0 9 19 H13"),
            [RibbonIcons.ZoomExtents] = One(corners + "M8 8 H16 V16 H8 Z"),
            [RibbonIcons.ZoomWindow] = One("M4 10 A6 6 0 1 0 16 10 A6 6 0 1 0 4 10 Z M14.5 14.5 L21 21 M7 8 H13 V12 H7 Z"),
            [RibbonIcons.Pan] = One("M8 13 V6 A1.5 1.5 0 0 1 11 6 V11 M11 5 A1.5 1.5 0 0 1 14 5 V11 M14 6 A1.5 1.5 0 0 1 17 6 V12 M17 9 A1.5 1.5 0 0 1 20 9 V15 A6 6 0 0 1 14 21 H12.5 A6 6 0 0 1 7.5 18.5 L4.3 13.8 A1.5 1.5 0 0 1 6.8 12.2 L8 13"),
            [RibbonIcons.Orbit] = One(Circle(12, 12, 3) + "M3 12 A9 4.5 0 0 0 21 12 M21 12 A9 4.5 0 0 0 6 8.6 M6 5 V8.8 H9.8"),
            [RibbonIcons.Pivot] = One("M12 3 V8 M12 16 V21 M3 12 H8 M16 12 H21 " + Circle(12, 12, 2)),
            [RibbonIcons.Previous] = One("M11 6 L5 12 L11 18 M5 12 H19"),
            [RibbonIcons.ViewIso] = One(cube),
            [RibbonIcons.ViewTop] = One("M4 4 H20 V20 H4 Z M9 9 H15 V15 H9 Z M4 4 L9 9 M20 4 L15 9 M20 20 L15 15 M4 20 L9 15"),
            [RibbonIcons.ViewFront] = One("M4 8 H16 V20 H4 Z M4 8 L8 4 H20 V16 L16 20 M16 8 L20 4"),
            [RibbonIcons.ViewSide] = One("M8 8 H20 V20 H8 Z M8 8 L4 4 V16 L8 20 M4 4 H16 L20 8"),
            [RibbonIcons.Save] = One("M5 3 H16 L21 8 V21 H3 V3 Z M7 3 V8 H15 V3 M7 21 V14 H17 V21"),
            [RibbonIcons.List] = One("M9 6 H21 M9 12 H21 M9 18 H21 M4 6 H5 M4 12 H5 M4 18 H5"),
            [RibbonIcons.Perspective] = One("M3 7 L21 3 V21 L3 17 Z M3 12 H21 M12 5 V19"),
            [RibbonIcons.Parallel] = One("M4 5 H20 V19 H4 Z M4 12 H20 M12 5 V19"),
            [RibbonIcons.ViewportSingle] = One(frame),
            [RibbonIcons.ViewportTwo] = One(frame + "M12 4 V20"),
            [RibbonIcons.ViewportTwoH] = One(frame + "M3 12 H21"),
            [RibbonIcons.ViewportFour] = One(frame + "M12 4 V20 M3 12 H21"),
            [RibbonIcons.ViewportNine] = One(frame + "M9 4 V20 M15 4 V20 M3 9.33 H21 M3 14.67 H21"),
            [RibbonIcons.Explorer] = One("M4 4 H10 V8 H4 Z M7 8 V18 H12 M7 13 H12 M12 11 H20 V15 H12 Z M12 16 H20 V20 H12 Z"),
            [RibbonIcons.Properties] = One("M4 6 H20 M4 12 H20 M4 18 H20 M9 4 V8 M15 10 V14 M7 16 V20"),
            [RibbonIcons.CommandLine] = One(frame + "M7 9 L10 12 L7 15 M12 15 H17"),
            [RibbonIcons.History] = One("M4 12 A8 8 0 1 0 6.3 6.3 M3 4 V8 H7 M12 8 V12 L15 14"),
            [RibbonIcons.DockLeft] = One(frame + "M9 4 V20 M5 8 H7 M5 11 H7"),
            [RibbonIcons.DockRight] = One(frame + "M15 4 V20 M17 8 H19 M17 11 H19"),
            [RibbonIcons.ColorRgb] =
            [
                (Circle(12, 8, 4.5), UiPalette.DataRed),
                (Circle(8, 15, 4.5), UiPalette.DataGreen),
                (Circle(16, 15, 4.5), UiPalette.DataBlue)
            ],
            [RibbonIcons.ColorHeight] =
            [
                ("M4 20 H20", null),
                ("M6 20 V16", UiPalette.DataBlue),
                ("M10 20 V12", UiPalette.DataGreen),
                ("M14 20 V8", UiPalette.DataYellow),
                ("M18 20 V4", UiPalette.DataRed)
            ],
            [RibbonIcons.ColorClass] =
            [
                ("M4 4 H10 V10 H4 Z", UiPalette.DataGreen),
                ("M14 4 H20 V10 H14 Z", UiPalette.DataOrange),
                ("M4 14 H10 V20 H4 Z", UiPalette.DataBlue),
                ("M14 14 H20 V20 H14 Z", UiPalette.DataPurple)
            ],
            [RibbonIcons.ColorIntensity] = One(Circle(12, 12, 4) + "M12 2 V4 M12 20 V22 M2 12 H4 M20 12 H22 M4.9 4.9 L6.3 6.3 M17.7 17.7 L19.1 19.1 M4.9 19.1 L6.3 17.7 M17.7 6.3 L19.1 4.9"),
            [RibbonIcons.ColorReturn] = One("M3 12 H7 L9 6 L12 18 L15 9 L17 12 H21"),
            [RibbonIcons.Point] = One(Circle(12, 12, 3)),
            [RibbonIcons.PointLarger] = One(Circle(10, 13, 4.5) + "M19 3 V9 M16 6 H22"),
            [RibbonIcons.PointSmaller] = One(Circle(10, 13, 2.5) + "M16 6 H22"),
            [RibbonIcons.Filter] = One("M3 4 H21 L14 12 V19 L10 21 V12 Z"),
            [RibbonIcons.Thin] = One(Dots((5, 5), (12, 5), (19, 5), (8.5, 12), (15.5, 12), (5, 19), (12, 19), (19, 19))),
            [RibbonIcons.Gauge] = One("M4 17 A8 8 0 1 1 20 17 M12 13 L16 8 M4 20 H20"),
            [RibbonIcons.Settings] = One(Circle(12, 12, 3) + Circle(12, 12, 7) + "M12 2 V5 M12 19 V22 M2 12 H5 M19 12 H22 M4.9 4.9 L7 7 M17 17 L19.1 19.1 M4.9 19.1 L7 17 M17 7 L19.1 4.9"),
            [RibbonIcons.Reset] = One("M4 12 A8 8 0 1 0 6.3 6.3 M4 3 V8 H9"),
            [RibbonIcons.Registry] = One("M4 4 H20 V20 H4 Z M4 9 H20 M4 14 H20 M9 4 V20"),
            [RibbonIcons.Instance] = One("M9 3 L7 21 M17 3 L15 21 M4 8 H21 M3 16 H20"),
            [RibbonIcons.Add] = One("M12 5 V19 M5 12 H19"),
            [RibbonIcons.Tree] = One("M12 3 L6 11 H9 L5 17 H19 L15 11 H18 Z M12 17 V21"),
            [RibbonIcons.Chart] = One("M4 20 V4 M4 20 H20 M8 16 V11 M12 16 V7 M16 16 V13"),
            [RibbonIcons.Section] = One("M2 17 L6 12 L10 14 L14 8 L18 10 L22 6 M2 21 H22 M12 3 V21"),
            [RibbonIcons.Flip] = One("M7 4 L3 8 L7 12 M3 8 H21 M17 12 L21 16 L17 20 M21 16 H3"),
            [RibbonIcons.Mesh] = One("M3 20 L12 4 L21 20 Z M7.5 12 H16.5 L12 20 Z"),
            [RibbonIcons.Visible] = One(eye + Circle(12, 12, 3)),
            [RibbonIcons.Hidden] = One(eye + Circle(12, 12, 3) + "M4 4 L20 20"),
            [RibbonIcons.Polyline] = One("M3 18 L8 8 L14 14 L21 5 M2 17 H4 V19 H2 Z M13 13 H15 V15 H13 Z"),
            [RibbonIcons.Polyline3D] = One("M3 19 L9 13 V5 L17 9 L21 19 M8 12 H10 V14 H8 Z M16 8 H18 V10 H16 Z"),
            [RibbonIcons.Edit] = One("M4 20 H8 L20 8 L16 4 L4 16 Z M14 6 L18 10"),
            [RibbonIcons.Ortho] = One("M5 3 V19 H21 M5 15 H9 V19"),
            [RibbonIcons.Info] = One(Circle(12, 12, 9) + "M12 11 V16 M12 7.5 V8"),
            [RibbonIcons.Index] = One("M4 4 H20 V20 H4 Z M12 4 V20 M4 12 H20 M12 8 H16 M16 4 V12 M14 4 V8"),
            [RibbonIcons.Script] = One("M6 3 H18 V21 H6 Z M9 8 H15 M9 12 H15 M9 16 H13"),
            [RibbonIcons.Api] = One("M8 7 L3 12 L8 17 M16 7 L21 12 L16 17 M14 4 L10 20"),
            [Camera] = One("M3 7 H7 L9 5 H15 L17 7 H21 V19 H3 Z " + Circle(12, 13, 3)),
            [ChevronDown] = One("M6 9 L12 15 L18 9"),
            [ChevronUp] = One("M6 15 L12 9 L18 15")
        };

        return map;
    }
}
