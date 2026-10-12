using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Effects;

namespace Clipline;

/// <summary>Something drawn over a whole screen for a moment.</summary>
internal interface IOverlayMotion
{
    UIElement Visual { get; }
    /// <summary>Advances the motion; false once it has finished.</summary>
    bool Step(double dt);
}

/// <summary>
/// A transparent, click-through window over a whole screen, there only while
/// a capture flies up to the line or a photo falls off it, so neither is cut
/// off by the line's strip.
/// </summary>
internal sealed class OverlayWindow : Window
{
    static readonly Dictionary<PixelRect, OverlayWindow> Open = [];

    readonly Canvas stage = new();
    readonly List<IOverlayMotion> motions = [];
    readonly nint hwnd;
    TimeSpan lastFrame;
    bool running;

    OverlayWindow(Screen screen)
    {
        Screen = screen;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        IsHitTestVisible = false;
        Content = stage;
        hwnd = new WindowInteropHelper(this).EnsureHandle();
        Native.SetExStyle(hwnd, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT);
        Place();
    }

    public Screen Screen { get; }

    void Place()
    {
        var b = Screen.Bounds;
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, b.Left, b.Top, b.Width, b.Height, Native.SWP_NOACTIVATE);
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, b.Left, b.Top, b.Width, b.Height, Native.SWP_NOACTIVATE);
    }

    /// <summary>A point on this screen, in the overlay's units.</summary>
    public Rect ToLocal(PixelRect r)
    {
        var s = Screen.Scale;
        return new Rect((r.Left - Screen.Bounds.Left) / s, (r.Top - Screen.Bounds.Top) / s, r.Width / s, r.Height / s);
    }

    public static OverlayWindow For(Screen screen)
    {
        if (!Open.TryGetValue(screen.Bounds, out var window))
        {
            window = new OverlayWindow(screen);
            Open[screen.Bounds] = window;
        }
        return window;
    }

    public void Add(IOverlayMotion motion)
    {
        motions.Add(motion);
        stage.Children.Add(motion.Visual);
        motion.Step(0);
        if (!IsVisible) Show();
        if (running) return;
        running = true;
        lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnFrame;
    }

    void OnFrame(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        var dt = lastFrame == TimeSpan.Zero ? 1.0 / 60 : (now - lastFrame).TotalSeconds;
        if (dt <= 0) return;
        lastFrame = now;
        dt = Math.Min(dt, 1.0 / 20);
        foreach (var motion in motions.ToList())
        {
            if (motion.Step(dt)) continue;
            motions.Remove(motion);
            stage.Children.Remove(motion.Visual);
        }
        if (motions.Count > 0) return;
        CompositionTarget.Rendering -= OnFrame;
        running = false;
        Open.Remove(Screen.Bounds);
        Close();
    }

    /// <summary>How many captures are flying right now. Each holds a large image, so only a couple fly at once.</summary>
    public static int Flights => Open.Values.Sum(w => w.motions.Count(m => m is Flight));
}

/// <summary>A card drawn outside the line: glass, photo and clip, every part sized by hand each frame.</summary>
internal sealed class FlyingCard
{
    public readonly Canvas Root = new() { IsHitTestVisible = false };
    readonly Border glass = new() { BorderThickness = new Thickness(0.9) };
    readonly System.Windows.Controls.Image photo = new() { Stretch = Stretch.UniformToFill };
    readonly Border photoHolder = new() { ClipToBounds = true };
    readonly FrameworkElement clip = Parts.Clothespin();
    readonly RotateTransform rotate = new();
    readonly DropShadowEffect shadow = new() { Color = Colors.Black, Opacity = 0.24, BlurRadius = 20, ShadowDepth = 5, Direction = 270 };

    public FlyingCard(BitmapSource image, Palette palette)
    {
        glass.Background = palette.Glass;
        glass.BorderBrush = palette.GlassEdge;
        photo.Source = image;
        photo.SetValue(RenderOptions.BitmapScalingModeProperty, BitmapScalingMode.HighQuality);
        photoHolder.Child = photo;
        var body = new Canvas();
        body.Children.Add(glass);
        body.Children.Add(photoHolder);
        body.Effect = shadow;
        Root.Children.Add(body);
        Root.Children.Add(clip);
        Root.RenderTransform = rotate;
    }

    /// <summary>
    /// Places the card with its top centre at (<paramref name="cx"/>, <paramref name="top"/>).
    /// <paramref name="chrome"/> fades in the glass and the clip; the photo is always there.
    /// </summary>
    public void Set(double cx, double top, double w, double h, double angle, double radius, double inset, double chrome, double opacity)
    {
        Canvas.SetLeft(Root, cx - w / 2);
        Canvas.SetTop(Root, top);
        Root.Width = w;
        Root.Height = h;
        rotate.CenterX = w / 2;
        rotate.CenterY = 0;
        rotate.Angle = angle;
        Root.Opacity = opacity;

        glass.Width = w;
        glass.Height = h;
        glass.CornerRadius = new CornerRadius(radius);
        glass.Opacity = chrome;

        var pw = Math.Max(1, w - inset * 2);
        var ph = Math.Max(1, h - inset * 2);
        photoHolder.Width = pw;
        photoHolder.Height = ph;
        photo.Width = pw;
        photo.Height = ph;
        var inner = Math.Max(0, radius - inset);
        photoHolder.Clip = new RectangleGeometry(new Rect(0, 0, pw, ph), inner, inner);
        Canvas.SetLeft(photoHolder, inset);
        Canvas.SetTop(photoHolder, inset);

        // The clip grips the top edge: 26 tall, 12 of them over the card.
        Canvas.SetLeft(clip, w / 2 - LineMetrics.ClipWidth / 2);
        Canvas.SetTop(clip, -(LineMetrics.ClipHeight - LineMetrics.ClipOverlap));
        clip.Opacity = chrome;
        shadow.Opacity = 0.24 * Math.Max(chrome, 0.35);
    }
}

/// <summary>
/// The capture lifting off the screen and flying up to the line. It turns
/// into the hanging card on the way: it shrinks, tilts into place and grows
/// its glass frame and clip, so there is nothing left to change on landing.
/// </summary>
internal sealed class Flight(BitmapSource image, Rect from, Rect to, double tilt, Palette palette, Action landed) : IOverlayMotion
{
    const double Duration = 0.65;
    /// <summary>How high the gentle arc rises halfway.</summary>
    const double Arc = 30;
    const double FadeOut = 0.16;

    readonly FlyingCard card = new(image, palette);
    double elapsed;
    bool hasLanded;

    public UIElement Visual => card.Root;

    public bool Step(double dt)
    {
        elapsed += dt;
        var raw = Math.Min(1, elapsed / Duration);
        var k = Easing.Apply(Ease.InOutCubic, raw);
        var chrome = Easing.Smooth(k, 0.35, 1);
        double Lerp(double a, double b) => a + (b - a) * k;

        var w = Lerp(from.Width, to.Width);
        var h = Lerp(from.Height, to.Height);
        var cx = Lerp(from.X + from.Width / 2, to.X + to.Width / 2);
        var top = Lerp(from.Y, to.Y) - Math.Sin(Math.PI * k) * Arc;
        var fade = raw < 1 ? 1 : Math.Max(0, 1 - (elapsed - Duration) / FadeOut);
        card.Set(cx, top, w, h, tilt * k, Lerp(0, LineMetrics.FrameRadius), LineMetrics.FrameInset * k, chrome, fade);

        if (raw >= 1 && !hasLanded)
        {
            // The real card fades in underneath; this one fades out over it.
            hasLanded = true;
            landed();
        }
        return elapsed < Duration + FadeOut;
    }
}

/// <summary>
/// A discarded card falling off the line, drawn over the whole screen: down
/// it goes, tilting further and fading, in a little over half a second.
/// </summary>
internal sealed class Fall(BitmapSource image, Rect card, double tilt, Palette palette) : IOverlayMotion
{
    const double Duration = 0.55;
    readonly FlyingCard view = new(image, palette);
    double elapsed;

    public UIElement Visual => view.Root;

    public bool Step(double dt)
    {
        elapsed += dt;
        var raw = Math.Min(1, elapsed / Duration);
        var e = raw * raw * raw;
        var angle = tilt + (tilt * 7 + 20) * e;
        view.Set(card.X + card.Width / 2, card.Y + 520 * e, card.Width, card.Height, angle,
            LineMetrics.FrameRadius, LineMetrics.FrameInset, 1, 1 - e);
        return raw < 1;
    }
}
