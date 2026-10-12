namespace Clipline.Core;

/// <summary>
/// How big the photos hang. The clip, the rope and the glass frame keep their
/// size; the photos and the room they take grow or shrink.
/// </summary>
public enum LineSize { Small, Medium, Large }

public readonly record struct SizeD(double Width, double Height);

/// <summary>
/// Every measure of the line, in device independent pixels. Medium is the
/// original size; the others scale the photo, never the clip or the rope.
/// </summary>
public readonly record struct LineMetrics(LineSize Size)
{
    public const double RopeTop = 10;
    /// <summary>How far above the rope the top of a hanging photo (its clip) starts.</summary>
    public const double PinAbove = 9.5;
    public const double FrameRadius = 16;
    public const double FrameInset = 4;
    public const double ClipWidth = 9;
    public const double ClipHeight = 26;
    public const double ClipOverlap = 12;
    /// <summary>Distance from the top of a hanging photo (the clip) to its card.</summary>
    public const double CardOffsetBelowTop = ClipHeight - ClipOverlap;
    /// <summary>The room left free at the edges of the screen when working out how many fit.</summary>
    public const double EdgeRoom = 200;

    public double Scale => Size switch
    {
        LineSize.Small => 0.8,
        LineSize.Large => 1.35,
        _ => 1.0,
    };

    public double PhotoMaxHeight => Math.Round(104 * Scale);
    public double CardWidth => Math.Round(150 * Scale);
    public double Spacing => CardWidth + 24;
    public double PanelHeight => PhotoMaxHeight + 106;

    /// <summary>The longest side a thumbnail needs, in pixels, at this scale factor of the screen.</summary>
    public int ThumbnailPixels(double screenScale) => (int)Math.Ceiling(Math.Max(CardWidth, PhotoMaxHeight) * Math.Max(1, screenScale) * 1.5);

    /// <summary>The rope hangs as a parabola from edge to edge of the screen.</summary>
    public static double Sag(double width) => Math.Min(30, width * 0.018);

    public static double RopeY(double x, double width)
    {
        if (width <= 0) return RopeTop;
        var f = x / width;
        return RopeTop + 4 * Sag(width) * f * (1 - f);
    }

    /// <summary>
    /// Photos hang <see cref="Spacing"/> apart, centred. When there are more than
    /// the <paramref name="visible"/> that fit: with Keep on line set, they keep their
    /// spacing, the newest stays where the last one would hang on a full line,
    /// and <paramref name="scroll"/> brings older ones in from the left. Otherwise
    /// they move closer together so none is past the edge.
    /// </summary>
    public double X(int index, int count, double width, int visible, double scroll, bool keepsMore)
    {
        if (keepsMore && count > visible)
        {
            var newest = width / 2 + (visible - 1) * Spacing / 2;
            return newest - (count - 1 - index) * Spacing + scroll;
        }
        var gaps = Math.Max(count - 1, 0);
        var step = gaps > 0 ? Math.Min(Spacing, Math.Max(0, width - EdgeRoom) / gaps) : Spacing;
        return width / 2 - gaps * step / 2 + index * step;
    }

    /// <summary>How many photos fit across a screen of this width.</summary>
    public int VisibleCount(double width) => Math.Clamp((int)((width - EdgeRoom) / Spacing), 3, 12);

    /// <summary>Photos scrolled towards an edge fade out before they reach it.</summary>
    public static double EdgeFade(double x, double width) => Math.Clamp(Math.Min(x, width - x) / 90, 0, 1);

    /// <summary>
    /// The photo fits inside the card area keeping its proportions, so the
    /// frame hugs it whether the screenshot is wide or tall.
    /// </summary>
    public SizeD PhotoSize(double pixelWidth, double pixelHeight)
    {
        var maxW = CardWidth - 14;
        var maxH = PhotoMaxHeight;
        if (pixelWidth <= 0 || pixelHeight <= 0) return new SizeD(maxW, maxH);
        var scale = Math.Min(maxW / pixelWidth, maxH / pixelHeight);
        return new SizeD(Math.Round(pixelWidth * scale), Math.Round(pixelHeight * scale));
    }

    /// <summary>The card around the photo: the photo plus the glass inset.</summary>
    public SizeD CardSize(double pixelWidth, double pixelHeight)
    {
        var p = PhotoSize(pixelWidth, pixelHeight);
        return new SizeD(p.Width + FrameInset * 2, p.Height + FrameInset * 2);
    }
}
