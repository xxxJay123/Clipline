using System.Windows.Controls;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace Clipline;

/// <summary>
/// The colours of the glass, in light and dark. Windows cannot blur what is
/// behind a per-pixel transparent window, so the glass is a translucent pane
/// with a specular edge lit from above, the way Tendedero falls back to it
/// while a capture is in flight.
/// </summary>
internal sealed class Palette
{
    public required Brush Glass { get; init; }
    public required Brush GlassEdge { get; init; }
    public required Brush Hairline { get; init; }
    public required Brush BadgeGlass { get; init; }
    public required Brush Ink { get; init; }
    public required Brush SecondaryInk { get; init; }
    public required Brush PhotoStroke { get; init; }
    public required Color Dim { get; init; }

    public static Palette Light { get; } = new()
    {
        Glass = Freeze(new SolidColorBrush(Color.FromArgb(200, 250, 250, 252))),
        GlassEdge = Freeze(Vertical(Color.FromArgb(235, 255, 255, 255), Color.FromArgb(70, 255, 255, 255))),
        Hairline = Freeze(new SolidColorBrush(Color.FromArgb(30, 0, 0, 0))),
        BadgeGlass = Freeze(new SolidColorBrush(Color.FromArgb(222, 248, 248, 250))),
        Ink = Freeze(new SolidColorBrush(Color.FromRgb(28, 28, 30))),
        SecondaryInk = Freeze(new SolidColorBrush(Color.FromRgb(96, 96, 102))),
        PhotoStroke = Freeze(new SolidColorBrush(Color.FromArgb(46, 255, 255, 255))),
        Dim = Color.FromArgb(150, 20, 20, 24),
    };

    public static Palette Dark { get; } = new()
    {
        Glass = Freeze(new SolidColorBrush(Color.FromArgb(196, 52, 52, 58))),
        GlassEdge = Freeze(Vertical(Color.FromArgb(120, 255, 255, 255), Color.FromArgb(18, 255, 255, 255))),
        Hairline = Freeze(new SolidColorBrush(Color.FromArgb(120, 0, 0, 0))),
        BadgeGlass = Freeze(new SolidColorBrush(Color.FromArgb(226, 44, 44, 48))),
        Ink = Freeze(new SolidColorBrush(Color.FromRgb(242, 242, 247))),
        SecondaryInk = Freeze(new SolidColorBrush(Color.FromRgb(174, 174, 182))),
        PhotoStroke = Freeze(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255))),
        Dim = Color.FromArgb(170, 0, 0, 0),
    };

    public static Palette Current => SystemTheme.AppsDark ? Dark : Light;

    public static readonly FontFamily Font = new("Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily DisplayFont = new("Segoe UI Variable Display, Segoe UI");

    static LinearGradientBrush Vertical(Color top, Color bottom) => new(top, bottom, 90);

    static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}

/// <summary>The small drawn parts: clip, glass badges and their glyphs.</summary>
internal static class Parts
{
    static readonly Brush Metal = Frozen(new LinearGradientBrush(new GradientStopCollection
    {
        new(Color.FromRgb(179, 179, 179), 0),
        new(Color.FromRgb(237, 237, 237), 0.35),
        new(Color.FromRgb(209, 209, 209), 0.65),
        new(Color.FromRgb(158, 158, 158), 1),
    }, 0));

    static readonly Brush MetalEdge = Frozen(new LinearGradientBrush(Color.FromArgb(230, 255, 255, 255), Color.FromArgb(46, 0, 0, 0), 90));
    static readonly Brush Slot = Frozen(new SolidColorBrush(Color.FromArgb(82, 0, 0, 0)));

    /// <summary>
    /// A minimal aluminium clip: a brushed metal pill with a slot where it
    /// grips the line, and a soft shadow so it reads on any background.
    /// </summary>
    public static FrameworkElement Clothespin()
    {
        var grid = new Grid { Width = LineMetrics.ClipWidth, Height = LineMetrics.ClipHeight, IsHitTestVisible = false };
        grid.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(3.5),
            Background = Metal,
            BorderBrush = MetalEdge,
            BorderThickness = new Thickness(0.6),
        });
        grid.Children.Add(new Rectangle
        {
            Width = 5,
            Height = 1.4,
            RadiusX = 0.7,
            RadiusY = 0.7,
            Fill = Slot,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 8.5, 0, 0),
        });
        grid.Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.32, BlurRadius = 4, ShadowDepth = 1.5, Direction = 270 };
        return grid;
    }

    /// <summary>A round glass badge holding a glyph.</summary>
    public static Border Badge(double size, UIElement glyph, Palette palette) => new()
    {
        Width = size,
        Height = size,
        CornerRadius = new CornerRadius(size / 2),
        Background = palette.BadgeGlass,
        BorderBrush = palette.GlassEdge,
        BorderThickness = new Thickness(0.75),
        Child = glyph,
        IsHitTestVisible = false,
        Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.18, BlurRadius = 6, ShadowDepth = 1, Direction = 270 },
    };

    public static FrameworkElement Cross(Brush ink) => Glyph("M -3.2,-3.2 L 3.2,3.2 M 3.2,-3.2 L -3.2,3.2", ink, 1.5);

    /// <summary>Taking down a file that lives elsewhere only hides it from the line.</summary>
    public static FrameworkElement Hide(Brush ink) =>
        Glyph("M -4.6,0 C -2.9,-3.1 2.9,-3.1 4.6,0 C 2.9,3.1 -2.9,3.1 -4.6,0 Z M -1.2,0 A 1.2,1.2 0 1 0 1.2,0 A 1.2,1.2 0 1 0 -1.2,0 M -4,4 L 4,-4", ink, 1.1);

    public static FrameworkElement Check(Brush ink) => Glyph("M -3.6,0.2 L -1.1,2.7 L 3.8,-2.6", ink, 1.6);

    public static FrameworkElement Play(Brush ink) => Centered(new Path
    {
        Data = Geometry.Parse("M -2.6,-5.2 L 6,0 L -2.6,5.2 Z"),
        Fill = ink,
        Stroke = ink,
        StrokeThickness = 1.2,
        StrokeLineJoin = PenLineJoin.Round,
    });

    static FrameworkElement Glyph(string data, Brush ink, double thickness) => Centered(new Path
    {
        Data = Geometry.Parse(data),
        Stroke = ink,
        StrokeThickness = thickness,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
    });

    /// <summary>Glyphs are drawn around (0, 0); an empty canvas centred in its parent puts that origin in the middle.</summary>
    static FrameworkElement Centered(Path path) => new Canvas
    {
        Width = 0,
        Height = 0,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Children = { path },
        IsHitTestVisible = false,
    };

    static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
