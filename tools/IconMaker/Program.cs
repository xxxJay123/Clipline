// Draws the Clipline icon in code and writes Assets/Clipline.ico and docs/images/icon.png.
// Run from the repository root:  dotnet run --project tools/IconMaker
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        var root = Directory.GetCurrentDirectory();
        int[] sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];
        var pngs = sizes.Select(s => (Size: s, Png: Png(Render(s)))).ToList();

        var ico = Path.Combine(root, "src", "Clipline", "Assets", "Clipline.ico");
        Directory.CreateDirectory(Path.GetDirectoryName(ico)!);
        using (var file = File.Create(ico)) WriteIco(file, pngs);
        Console.WriteLine(ico);

        var preview = Path.Combine(root, "docs", "images", "icon.png");
        Directory.CreateDirectory(Path.GetDirectoryName(preview)!);
        File.WriteAllBytes(preview, Png(Render(512)));
        Console.WriteLine(preview);
    }

    /// <summary>ICO with PNG entries, which Windows reads at every size since Vista.</summary>
    static void WriteIco(Stream stream, List<(int Size, byte[] Png)> images)
    {
        using var w = new BinaryWriter(stream);
        w.Write((short)0);
        w.Write((short)1);
        w.Write((short)images.Count);
        var offset = 6 + 16 * images.Count;
        foreach (var (size, png) in images)
        {
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((short)1);
            w.Write((short)32);
            w.Write(png.Length);
            w.Write(offset);
            offset += png.Length;
        }
        foreach (var (_, png) in images) w.Write(png);
    }

    static byte[] Png(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    static BitmapSource Render(int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(size / 256.0, size / 256.0));
            Draw(dc, simple: size <= 32);
            dc.Pop();
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }

    static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    /// <summary>The icon on a 256 grid: a dusk-blue tile, a sagging line, and photos pegged on it.</summary>
    static void Draw(DrawingContext dc, bool simple)
    {
        var tile = new Rect(8, 8, 240, 240);
        var radius = simple ? 60 : 56;
        dc.DrawRoundedRectangle(new LinearGradientBrush(C("#33406E"), C("#161B2E"), 90), null, tile, radius, radius);
        dc.DrawRoundedRectangle(null, new Pen(new LinearGradientBrush(Color.FromArgb(70, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90), 3),
            new Rect(9.5, 9.5, 237, 237), radius - 1.5, radius - 1.5);
        dc.PushClip(new RectangleGeometry(tile, radius, radius));

        var ropeTop = simple ? 64.0 : 70.0;
        var sag = simple ? 30.0 : 42.0;
        var rope = new PathGeometry([new PathFigure(new Point(-10, ropeTop),
            [new QuadraticBezierSegment(new Point(128, ropeTop + sag * 2), new Point(266, ropeTop), true)], false)]);
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), simple ? 12 : 8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, Shifted(rope, 3));
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(C("#C9CEDD")), simple ? 9 : 6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, rope);

        double RopeY(double x) => ropeTop + 4 * sag * ((x + 10) / 276) * (1 - (x + 10) / 276);

        if (simple)
        {
            Card(dc, 128, RopeY(128), 132, 112, -6, Sunset());
        }
        else
        {
            Card(dc, 84, RopeY(84), 96, 80, -7, Sunset());
            Card(dc, 176, RopeY(176), 76, 100, 5, Sea());
        }
        dc.Pop();
    }

    static Geometry Shifted(Geometry g, double dy)
    {
        var copy = g.Clone();
        copy.Transform = new TranslateTransform(0, dy);
        return copy;
    }

    /// <summary>A glass card with a photo, hanging from its clip at (x, ropeY).</summary>
    static void Card(DrawingContext dc, double x, double ropeY, double w, double h, double angle, Action<DrawingContext, Rect> photo)
    {
        var top = ropeY + 6;
        dc.PushTransform(new RotateTransform(angle, x, ropeY));
        var card = new Rect(x - w / 2, top, w, h);
        // Shadow
        for (var i = 6; i >= 1; i--)
        {
            var spread = card;
            spread.Inflate(i * 1.6, i * 1.6);
            spread.Offset(0, 8);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb((byte)(10 + (6 - i) * 4), 0, 0, 0)), null, spread, 18 + i, 18 + i);
        }
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(235, 246, 247, 251)), new Pen(new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)), 1.5), card, 16, 16);
        var inner = card;
        inner.Inflate(-7, -7);
        dc.PushClip(new RectangleGeometry(inner, 10, 10));
        photo(dc, inner);
        dc.Pop();
        // The clip
        var clip = new Rect(x - 7, ropeY - 15, 14, 36);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), null, new Rect(clip.X, clip.Y + 3, clip.Width, clip.Height), 5, 5);
        var metal = new LinearGradientBrush(new GradientStopCollection
        {
            new(C("#B3B3B3"), 0), new(C("#F2F2F2"), 0.35), new(C("#D1D1D1"), 0.65), new(C("#9A9A9A"), 1),
        }, 0);
        dc.DrawRoundedRectangle(metal, new Pen(new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), 1), clip, 5, 5);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)), null, new Rect(x - 4, ropeY - 2, 8, 2.4), 1.2, 1.2);
        dc.Pop();
    }

    static Action<DrawingContext, Rect> Sunset() => (dc, r) =>
    {
        dc.DrawRectangle(new LinearGradientBrush(new GradientStopCollection
        {
            new(C("#5B4B9A"), 0), new(C("#E07A8C"), 0.55), new(C("#FFC48A"), 1),
        }, 90), null, r);
        dc.DrawEllipse(new SolidColorBrush(C("#FFE9C4")), null, new Point(r.X + r.Width * 0.66, r.Y + r.Height * 0.58), r.Width * 0.13, r.Width * 0.13);
        var hills = new PathGeometry([new PathFigure(new Point(r.Left, r.Bottom),
        [
            new LineSegment(new Point(r.Left, r.Y + r.Height * 0.72), true),
            new LineSegment(new Point(r.X + r.Width * 0.3, r.Y + r.Height * 0.5), true),
            new LineSegment(new Point(r.X + r.Width * 0.55, r.Y + r.Height * 0.7), true),
            new LineSegment(new Point(r.X + r.Width * 0.8, r.Y + r.Height * 0.56), true),
            new LineSegment(new Point(r.Right, r.Y + r.Height * 0.68), true),
            new LineSegment(new Point(r.Right, r.Bottom), true),
        ], true)]);
        dc.DrawGeometry(new SolidColorBrush(C("#3B2B5C")), null, hills);
    };

    static Action<DrawingContext, Rect> Sea() => (dc, r) =>
    {
        dc.DrawRectangle(new LinearGradientBrush(C("#7FD6E8"), C("#2E7FB8"), 90), null, r);
        for (var i = 0; i < 3; i++)
        {
            var y = r.Y + r.Height * (0.55 + i * 0.13);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)), null, new Rect(r.X + r.Width * 0.12, y, r.Width * (0.76 - i * 0.18), 5), 2.5, 2.5);
        }
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(230, 255, 250, 235)), null, new Point(r.X + r.Width * 0.3, r.Y + r.Height * 0.25), r.Width * 0.12, r.Width * 0.12);
    };
}
