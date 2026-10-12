using System.Runtime.InteropServices;

namespace Clipline;

/// <summary>
/// The tray icon, drawn in code at the exact size the taskbar wants: a photo
/// pegged on a line. The photo fills in while copied images are being hung,
/// so it shows at a glance that the clipboard is watched.
/// </summary>
internal static class Icons
{
    public static nint Tray(bool filled)
    {
        var dpi = Native.GetDpiForSystem();
        var size = Native.GetSystemMetricsForDpi(Native.SM_CXSMICON, dpi);
        if (size <= 0) size = 16;
        var ink = SystemTheme.TaskbarDark ? Colors.White : Color.FromRgb(26, 26, 28);
        return ToIcon(Bitmap(size, ink, filled));
    }

    public static BitmapSource Bitmap(int size, Color ink, bool filled)
    {
        var target = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        target.Render(Glyph(size, ink, filled));
        target.Freeze();
        return target;
    }

    /// <summary>The glyph on a 16-unit grid, scaled to the icon size.</summary>
    static DrawingVisual Glyph(int size, Color ink, bool filled)
    {
        var visual = new DrawingVisual();
        var brush = new SolidColorBrush(ink);
        var u = size / 16.0;
        using var dc = visual.RenderOpen();
        dc.PushTransform(new ScaleTransform(u, u));

        // The line, sagging a little.
        var rope = new PathGeometry([new PathFigure(new Point(0.6, 2.6),
            [new QuadraticBezierSegment(new Point(8, 5.6), new Point(15.4, 2.6), true)], false)]);
        dc.DrawGeometry(null, new Pen(brush, 1.25) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, rope);

        // The photo, slightly crooked, hanging from its clip.
        dc.PushTransform(new RotateTransform(-6, 8, 4.2));
        var card = new Rect(3.6, 5.6, 8.8, 8.6);
        if (filled)
        {
            dc.DrawRoundedRectangle(brush, null, card, 1.6, 1.6);
        }
        else
        {
            var inset = card;
            inset.Inflate(-0.6, -0.6);
            dc.DrawRoundedRectangle(null, new Pen(brush, 1.2), inset, 1.2, 1.2);
            // A little landscape inside the frame.
            var hills = new PathGeometry([new PathFigure(new Point(5.2, 12.2),
                [new LineSegment(new Point(7.4, 9.6), true), new LineSegment(new Point(8.8, 11), true),
                 new LineSegment(new Point(9.9, 10), true), new LineSegment(new Point(11, 12.2), true)], true)]);
            dc.DrawGeometry(brush, null, hills);
        }
        // The clip, drawn over the line and the top of the photo.
        dc.DrawRoundedRectangle(brush, null, new Rect(7.1, 2.3, 1.8, 4.6), 0.7, 0.7);
        dc.Pop();
        dc.Pop();
        return visual;
    }

    static nint ToIcon(BitmapSource target)
    {
        var size = target.PixelWidth;
        // Icons hold straight, not premultiplied, alpha.
        var straight = new FormatConvertedBitmap(target, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[size * size * 4];
        straight.CopyPixels(pixels, size * 4, 0);

        var header = new Native.BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<Native.BITMAPINFOHEADER>(),
            biWidth = size,
            biHeight = -size,
            biPlanes = 1,
            biBitCount = 32,
        };
        var color = Native.CreateDIBSection(0, ref header, 0, out var bits, 0, 0);
        Marshal.Copy(pixels, 0, bits, pixels.Length);
        var mask = Native.CreateBitmap(size, size, 1, 1, new byte[((size + 15) / 16) * 2 * size]);
        var info = new Native.ICONINFO { fIcon = true, hbmColor = color, hbmMask = mask };
        var icon = Native.CreateIconIndirect(ref info);
        Native.DeleteObject(color);
        Native.DeleteObject(mask);
        return icon;
    }
}
