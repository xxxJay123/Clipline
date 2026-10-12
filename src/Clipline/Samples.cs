using System.Windows.Controls;
using System.Windows.Shapes;

namespace Clipline;

/// <summary>
/// Made-up screenshots, drawn in code, for the demo and for the pictures in
/// the README. Nothing here comes from a real screen.
/// </summary>
internal static class Samples
{
    public static List<string> Write(string folder)
    {
        Directory.CreateDirectory(folder);
        var made = new List<string>();
        foreach (var (name, w, h, draw) in All())
        {
            var path = System.IO.Path.Combine(folder, name + ".png");
            if (!File.Exists(path)) Save(Render(w, h, draw), path);
            made.Add(path);
        }
        return made;
    }

    static IEnumerable<(string Name, int W, int H, Action<DrawingContext, double, double> Draw)> All()
    {
        yield return ("Sunset study", 1500, 1000, Photo);
        yield return ("Release notes", 1600, 1000, Code);
        yield return ("Weekly numbers", 1400, 900, Chart);
        yield return ("Chat with Mei", 860, 1400, Chat);
        yield return ("Palette", 1600, 900, Swatches);
    }

    static RenderTargetBitmap Render(int w, int h, Action<DrawingContext, double, double> draw)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) draw(dc, w, h);
        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    static void Save(BitmapSource image, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    static SolidColorBrush B(byte r, byte g, byte b, byte a = 255) => new(Color.FromArgb(a, r, g, b));

    static void Photo(DrawingContext dc, double w, double h)
    {
        var sky = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromRgb(46, 49, 104), 0),
            new(Color.FromRgb(150, 82, 140), 0.45),
            new(Color.FromRgb(246, 146, 112), 0.78),
            new(Color.FromRgb(255, 206, 140), 1),
        }, 90);
        dc.DrawRectangle(sky, null, new Rect(0, 0, w, h));
        dc.DrawEllipse(B(255, 236, 196), null, new Point(w * 0.62, h * 0.6), 110, 110);
        dc.DrawEllipse(B(255, 236, 196, 60), null, new Point(w * 0.62, h * 0.6), 170, 170);
        Ridge(dc, w, h, 0.62, B(122, 74, 120), 7);
        Ridge(dc, w, h, 0.72, B(82, 52, 98), 11);
        Ridge(dc, w, h, 0.84, B(44, 32, 70), 17);
    }

    static void Ridge(DrawingContext dc, double w, double h, double level, Brush fill, int seed)
    {
        var random = new Random(seed);
        var figure = new PathFigure { StartPoint = new Point(0, h), IsClosed = true };
        figure.Segments.Add(new LineSegment(new Point(0, h * level), true));
        for (var x = 0.0; x <= w; x += w / 9)
            figure.Segments.Add(new LineSegment(new Point(x, h * level - random.NextDouble() * h * 0.12), true));
        figure.Segments.Add(new LineSegment(new Point(w, h), true));
        dc.DrawGeometry(fill, null, new PathGeometry([figure]));
    }

    static void Code(DrawingContext dc, double w, double h)
    {
        dc.DrawRectangle(B(30, 31, 38), null, new Rect(0, 0, w, h));
        dc.DrawRectangle(B(24, 25, 31), null, new Rect(0, 0, w, 64));
        dc.DrawRectangle(B(36, 37, 46), null, new Rect(0, 64, 300, h - 64));
        dc.DrawRoundedRectangle(B(56, 58, 72), null, new Rect(340, 18, 220, 30), 8, 8);
        for (var i = 0; i < 12; i++)
            dc.DrawRoundedRectangle(B(90, 94, 116, (byte)(i == 3 ? 255 : 150)), null, new Rect(36, 100 + i * 44, 120 + (i * 37 % 110), 14), 7, 7);
        var colors = new[] { B(199, 146, 234), B(130, 170, 255), B(195, 232, 141), B(255, 203, 107), B(137, 221, 255), B(240, 113, 120) };
        var random = new Random(3);
        for (var line = 0; line < 19; line++)
        {
            var x = 360.0 + (line % 5 == 0 ? 0 : 40 * (1 + line % 3));
            var y = 100 + line * 46;
            dc.DrawRoundedRectangle(B(80, 84, 100), null, new Rect(330, y + 2, 14, 12), 4, 4);
            for (var token = 0; token < 1 + random.Next(4); token++)
            {
                var width = 50 + random.Next(170);
                dc.DrawRoundedRectangle(colors[random.Next(colors.Length)], null, new Rect(x, y, width, 16), 8, 8);
                x += width + 18;
            }
        }
    }

    static void Chart(DrawingContext dc, double w, double h)
    {
        dc.DrawRectangle(B(244, 245, 249), null, new Rect(0, 0, w, h));
        dc.DrawRoundedRectangle(Brushes.White, new Pen(B(226, 228, 236), 2), new Rect(60, 60, w - 120, h - 120), 28, 28);
        dc.DrawRoundedRectangle(B(36, 38, 52), null, new Rect(110, 110, 360, 30), 15, 15);
        dc.DrawRoundedRectangle(B(170, 174, 190), null, new Rect(110, 158, 220, 20), 10, 10);
        var values = new[] { 0.42, 0.58, 0.5, 0.74, 0.66, 0.88, 0.8, 0.95 };
        var baseLine = h - 140;
        for (var i = 0; i < values.Length; i++)
        {
            var bar = new Rect(130 + i * 140, baseLine - values[i] * 520, 84, values[i] * 520);
            dc.DrawRoundedRectangle(i == values.Length - 1 ? B(88, 101, 242) : B(173, 182, 255), null, bar, 14, 14);
        }
        dc.DrawLine(new Pen(B(226, 228, 236), 3), new Point(110, baseLine + 2), new Point(w - 110, baseLine + 2));
    }

    static void Chat(DrawingContext dc, double w, double h)
    {
        dc.DrawRectangle(B(236, 240, 245), null, new Rect(0, 0, w, h));
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, 150));
        dc.DrawEllipse(B(255, 179, 128), null, new Point(100, 85), 44, 44);
        dc.DrawRoundedRectangle(B(40, 42, 54), null, new Rect(170, 62, 200, 24), 12, 12);
        dc.DrawRoundedRectangle(B(160, 166, 180), null, new Rect(170, 98, 130, 18), 9, 9);
        var y = 200.0;
        var random = new Random(9);
        for (var i = 0; i < 9; i++)
        {
            var mine = i % 3 == 1;
            var bw = 260 + random.Next(300);
            var bh = 70 + random.Next(2) * 50;
            var x = mine ? w - 50 - bw : 50;
            dc.DrawRoundedRectangle(mine ? B(0, 132, 255) : Brushes.White, null, new Rect(x, y, bw, bh), 28, 28);
            dc.DrawRoundedRectangle(mine ? B(255, 255, 255, 170) : B(180, 186, 198), null, new Rect(x + 28, y + 26, bw - 90, 18), 9, 9);
            y += bh + 26;
        }
    }

    static void Swatches(DrawingContext dc, double w, double h)
    {
        dc.DrawRectangle(B(250, 248, 244), null, new Rect(0, 0, w, h));
        var colors = new[] { B(232, 93, 74), B(244, 162, 89), B(244, 211, 94), B(110, 190, 150), B(70, 140, 190), B(108, 92, 178) };
        for (var i = 0; i < colors.Length; i++)
        {
            var r = new Rect(80 + i * 245, 140, 205, 480);
            dc.DrawRoundedRectangle(colors[i], null, r, 30, 30);
            dc.DrawRoundedRectangle(B(60, 56, 52), null, new Rect(r.X, r.Bottom + 40, 150, 22), 11, 11);
            dc.DrawRoundedRectangle(B(170, 164, 156), null, new Rect(r.X, r.Bottom + 80, 100, 18), 9, 9);
        }
    }
}

/// <summary>Pictures of the line for the README, in light and dark, drawn without touching the screen.</summary>
internal static class Snapshot
{
    sealed class Host : ICardHost
    {
        public void CardClicked(CardView card) { }
        public void CardDoubleClicked(CardView card) { }
        public void CardHeld(CardView card) { }
        public void CardPreview(CardView card) { }
        public void CardCross(CardView card) { }
        public void CardMenu(CardView card) { }
        public void CardDrag(CardView card) { }
        public void CardPressChanged(CardView card, bool pressed) { }
    }

    public static int Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var samples = Samples.Write(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Clipline Snapshot", "Samples"));
        foreach (var dark in new[] { false, true })
        {
            Render(samples, dark, System.IO.Path.Combine(folder, dark ? "line-dark.png" : "line-light.png"), 1440, hoverIndex: 2, copiedIndex: 0);
            Render([], dark, System.IO.Path.Combine(folder, dark ? "empty-dark.png" : "empty-light.png"), 1100, -1, -1);
        }
        // The tray glyph, large enough to judge, on both taskbars.
        var strip = new DrawingVisual();
        using (var dc = strip.RenderOpen())
        {
            var x = 0.0;
            foreach (var (back, ink) in new[] { (Color.FromRgb(32, 32, 32), Colors.White), (Color.FromRgb(238, 238, 238), Color.FromRgb(26, 26, 28)) })
            {
                dc.DrawRectangle(new SolidColorBrush(back), null, new Rect(x, 0, 200, 56));
                var px = 8.0;
                foreach (var size in new[] { 16, 24, 32 })
                    foreach (var filled in new[] { false, true })
                    {
                        dc.DrawImage(Icons.Bitmap(size, ink, filled), new Rect(x + px, 12, size, size));
                        px += size + 6;
                    }
                x += 200;
            }
        }
        var tray = new RenderTargetBitmap(400, 56, 96, 96, PixelFormats.Pbgra32);
        tray.Render(strip);
        var trayEncoder = new PngBitmapEncoder();
        trayEncoder.Frames.Add(BitmapFrame.Create(tray));
        using (var file = File.Create(System.IO.Path.Combine(folder, "tray.png"))) trayEncoder.Save(file);
        return 0;
    }

    static void Render(List<string> paths, bool dark, string output, double width, int hoverIndex, int copiedIndex)
    {
        var palette = dark ? Palette.Dark : Palette.Light;
        var metrics = new LineMetrics(LineSize.Medium);
        var settings = new Settings();
        var temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Clipline Snapshot");
        var line = new Line(settings, System.IO.Path.Combine(temp, "Inbox"), System.IO.Path.Combine(temp, "line.json")) { Metrics = metrics };
        foreach (var path in paths)
        {
            var thumb = Thumbnails.Load(path, metrics.ThumbnailPixels(2))!;
            var size = Thumbnails.PixelSize(path)!.Value;
            line.Items.Add(new Pegged(path, thumb, size.Width, size.Height));
        }
        line.VisibleCount = metrics.VisibleCount(width);
        line.MaxItems = line.VisibleCount;

        var height = metrics.PanelHeight + 40;
        var scene = new Grid { Width = width, Height = height, ClipToBounds = true };
        scene.Children.Add(Desktop(width, height, dark));
        var surface = new LineSurface(line, new Host(), metrics, palette)
        {
            Width = width,
            Height = metrics.PanelHeight,
            VerticalAlignment = VerticalAlignment.Top,
        };
        scene.Children.Add(surface);
        scene.Measure(new Size(width, height));
        scene.Arrange(new Rect(0, 0, width, height));
        scene.UpdateLayout();

        surface.Sync(settle: true);
        surface.SetRevealed(true);
        var cards = surface.Cards.ToList();
        if (hoverIndex >= 0 && hoverIndex < cards.Count) surface.SetHover(cards[hoverIndex].Id);
        if (copiedIndex >= 0 && copiedIndex < cards.Count) cards[copiedIndex].SetCopied(true);
        scene.UpdateLayout();
        surface.SettleNow();
        scene.UpdateLayout();

        var bitmap = new RenderTargetBitmap((int)(width * 2), (int)(height * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(scene);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(output);
        encoder.Save(file);
        Console.WriteLine(output);
    }

    /// <summary>A wallpaper and the top of a window, so the line is seen where it lives.</summary>
    static UIElement Desktop(double width, double height, bool dark)
    {
        var canvas = new Canvas { Width = width, Height = height };
        canvas.Background = dark
            ? new LinearGradientBrush(Color.FromRgb(24, 30, 48), Color.FromRgb(58, 40, 72), 20)
            : new LinearGradientBrush(Color.FromRgb(188, 210, 236), Color.FromRgb(236, 214, 226), 20);
        var window = new Border
        {
            Width = width * 0.62,
            Height = height,
            CornerRadius = new CornerRadius(10, 10, 0, 0),
            Background = dark ? new SolidColorBrush(Color.FromRgb(32, 32, 34)) : Brushes.White,
            BorderBrush = new SolidColorBrush(dark ? Color.FromArgb(60, 255, 255, 255) : Color.FromArgb(30, 0, 0, 0)),
            BorderThickness = new Thickness(1),
        };
        var stack = new StackPanel { Margin = new Thickness(28, 64, 28, 0) };
        var ink = dark ? Color.FromArgb(46, 255, 255, 255) : Color.FromArgb(22, 0, 0, 0);
        foreach (var fraction in new[] { 0.55, 0.9, 0.8, 0.95, 0.6, 0.85, 0.7 })
            stack.Children.Add(new Rectangle { Height = 12, RadiusX = 6, RadiusY = 6, Width = window.Width * 0.85 * fraction, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 16), Fill = new SolidColorBrush(ink) });
        window.Child = stack;
        Canvas.SetLeft(window, width * 0.19);
        Canvas.SetTop(window, 26);
        canvas.Children.Add(window);
        return canvas;
    }
}
