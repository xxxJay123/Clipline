namespace Clipline.Core;

public readonly record struct PixelPoint(int X, int Y);

/// <summary>A rectangle in physical screen pixels.</summary>
public readonly record struct PixelRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;
    public bool Contains(PixelPoint p) => p.X >= Left && p.X < Right && p.Y >= Top && p.Y < Bottom;

    public static PixelRect FromCorners(PixelPoint a, PixelPoint b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
}

/// <summary>A press, move and release of the left button, as a region snip is drawn.</summary>
public readonly record struct Drag(PixelRect Rect, DateTime EndedAt);

/// <summary>
/// Works out where on screen a screenshot was taken, so it can fly up to the
/// line from there. Windows does not record it on the file, so the size of
/// the image is compared with what just happened on screen:
/// <list type="bullet">
/// <item>A region snip is as big as the rectangle the pointer just dragged.</item>
/// <item>A full screen capture is as big as one of the screens.</item>
/// </list>
/// Anything else simply drops onto the line.
/// </summary>
public static class CaptureMatch
{
    /// <summary>How long after a drag the snip may still arrive. Snipping Tool saves within a second or two.</summary>
    public static readonly TimeSpan DragWindow = TimeSpan.FromSeconds(12);
    const int Tolerance = 3;

    public static PixelRect? Find(int width, int height, DateTime arrived, IReadOnlyList<Drag> drags,
        IReadOnlyList<PixelRect> screens, PixelPoint cursor)
    {
        if (width <= 2 || height <= 2) return null;

        for (var i = drags.Count - 1; i >= 0; i--)
        {
            var d = drags[i];
            var age = arrived - d.EndedAt;
            if (age < TimeSpan.FromSeconds(-1) || age > DragWindow) continue;
            if (Math.Abs(d.Rect.Width - width) <= Tolerance && Math.Abs(d.Rect.Height - height) <= Tolerance)
                return d.Rect with { Width = width, Height = height };
        }

        var same = screens.Where(s => s.Width == width && s.Height == height).ToList();
        if (same.Count == 0) return null;
        return same.FirstOrDefault(s => s.Contains(cursor), same[0]);
    }
}
