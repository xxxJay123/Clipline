using System.Windows.Interop;

namespace Clipline;

/// <summary>
/// A transparent strip along the top of a screen that floats over every app,
/// never takes focus, and lets clicks pass through everywhere except over
/// the photos.
/// </summary>
internal sealed class LineWindow : Window
{
    readonly nint hwnd;
    bool clickThrough = true;
    bool placing;
    PixelRect frame;

    public LineWindow(LineSurface surface)
    {
        Title = "Clipline";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        Content = surface;
        Surface = surface;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);

        hwnd = new WindowInteropHelper(this).EnsureHandle();
        // No taskbar button, no Alt+Tab entry, never activated by a click, and
        // invisible to the mouse until the pointer is over a photo.
        Native.SetExStyle(hwnd, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT);
        DpiChanged += (_, _) =>
        {
            // Windows suggests its own size for the new scale; the line keeps the screen's width.
            if (!placing && Screen != null) Dispatcher.BeginInvoke(() => Place(Screen, PanelHeight));
        };
    }

    public LineSurface Surface { get; }
    public nint Handle => hwnd;
    public Screen? Screen { get; private set; }
    public double PanelHeight { get; private set; }

    /// <summary>The window's place on screen, in physical pixels.</summary>
    public PixelRect Frame => frame;

    /// <summary>Hangs the line along the top of the screen's work area, above any maximised window.</summary>
    public void Place(Screen screen, double panelHeight)
    {
        Screen = screen;
        PanelHeight = panelHeight;
        var height = (int)Math.Ceiling(panelHeight * screen.Scale);
        var target = new PixelRect(screen.WorkArea.Left, screen.WorkArea.Top, screen.WorkArea.Width, height);
        placing = true;
        try
        {
            Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, target.Left, target.Top, target.Width, target.Height,
                Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
            // After a move to a screen with another scale, Windows may have resized it on the way.
            Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, target.Left, target.Top, target.Width, target.Height,
                Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
        }
        finally
        {
            placing = false;
        }
        frame = target;
    }

    /// <summary>The scale WPF is drawing this window at.</summary>
    public double Scale => VisualTreeHelper.GetDpi(this).DpiScaleX;

    /// <summary>Shows the strip without taking focus, above other always-on-top windows.</summary>
    public void ShowStrip()
    {
        // ShowActivated is off, so this shows the window without activating it.
        if (!IsVisible) Show();
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
    }

    /// <summary>
    /// Tucked away, the line leaves no window behind: an invisible strip over
    /// the top of the screen would still sit above other apps for anything
    /// that checks what is on top, like screen automation.
    /// </summary>
    public void HideStrip()
    {
        SetClickThrough(true);
        Hide();
    }

    public bool IsShown => Native.IsWindowVisible(hwnd);

    /// <summary>The strip spans the whole width, so it only takes the mouse while the pointer is over a photo.</summary>
    public void SetClickThrough(bool on)
    {
        if (clickThrough == on) return;
        clickThrough = on;
        if (on) Native.SetExStyle(hwnd, Native.WS_EX_TRANSPARENT);
        else Native.SetExStyle(hwnd, 0, Native.WS_EX_TRANSPARENT);
    }

    /// <summary>A point on screen in this window's own units.</summary>
    public Point ToLocal(PixelPoint p)
    {
        var s = Screen?.Scale ?? 1;
        return new Point((p.X - frame.Left) / s, (p.Y - frame.Top) / s);
    }

    /// <summary>A rectangle in this window's units on screen, in physical pixels.</summary>
    public PixelRect ToScreen(Rect r)
    {
        var s = Screen?.Scale ?? 1;
        return new PixelRect((int)Math.Round(frame.Left + r.X * s), (int)Math.Round(frame.Top + r.Y * s),
            (int)Math.Round(r.Width * s), (int)Math.Round(r.Height * s));
    }

    protected override void OnPreviewMouseWheel(System.Windows.Input.MouseWheelEventArgs e)
    {
        base.OnPreviewMouseWheel(e);
        MouseWheelScrolled?.Invoke(e.Delta);
        e.Handled = true;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Runs inside EnsureHandle, before the handle is stored.
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(Hook);
    }

    /// <summary>Sideways scrolling, from a tilting wheel or a touchpad.</summary>
    nint Hook(nint h, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == Native.WM_MOUSEHWHEEL)
        {
            var delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
            MouseWheelScrolled?.Invoke(-delta);
            handled = true;
        }
        return 0;
    }

    public event Action<int>? MouseWheelScrolled;
}
