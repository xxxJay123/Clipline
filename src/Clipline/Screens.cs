using Microsoft.Win32;

namespace Clipline;

/// <summary>One display, in physical pixels, with its own scale factor.</summary>
internal sealed record Screen(nint Handle, PixelRect Bounds, PixelRect WorkArea, double Scale, bool Primary)
{
    /// <summary>
    /// The strip along the top edge that brings the line down when the pointer
    /// rests in it. Normally a few pixels; with the taskbar at the top, the
    /// whole taskbar, the way the menu bar works on the Mac.
    /// </summary>
    public PixelRect TopBand
    {
        get
        {
            var height = Math.Max(WorkArea.Top - Bounds.Top, (int)Math.Ceiling(3 * Scale));
            return new PixelRect(Bounds.Left, Bounds.Top, Bounds.Width, height);
        }
    }

    public bool SameDisplay(Screen? other) => other != null && other.Bounds == Bounds;
}

internal static class Screens
{
    public static IReadOnlyList<Screen> All()
    {
        var list = new List<Screen>();
        Native.EnumDisplayMonitors(0, 0, (nint monitor, nint _, ref Native.RECT _, nint _) =>
        {
            if (Describe(monitor) is { } screen) list.Add(screen);
            return true;
        }, 0);
        return list;
    }

    public static Screen At(PixelPoint point)
    {
        var monitor = Native.MonitorFromPoint(new Native.POINT { X = point.X, Y = point.Y }, Native.MONITOR_DEFAULTTONEAREST);
        return Describe(monitor) ?? All().First();
    }

    /// <summary>The screen you are using: the one with the pointer.</summary>
    public static Screen UnderPointer() => At(Native.Cursor());

    public static Screen? Of(nint hwnd) => Describe(Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST));

    static Screen? Describe(nint monitor)
    {
        var info = new Native.MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFOEX>() };
        if (monitor == 0 || !Native.GetMonitorInfo(monitor, ref info)) return null;
        var dpi = Native.GetDpiForMonitor(monitor, 0, out var x, out _) == 0 ? x : 96u;
        return new Screen(monitor, ToRect(info.rcMonitor), ToRect(info.rcWork), dpi / 96.0, (info.dwFlags & 1) != 0);
    }

    static PixelRect ToRect(Native.RECT r) => new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
}

/// <summary>Light or dark, following the Windows app setting.</summary>
internal static class SystemTheme
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool AppsDark => Read("AppsUseLightTheme") == 0;

    /// <summary>The taskbar can be dark while apps are light; the tray icon follows the taskbar.</summary>
    public static bool TaskbarDark => Read("SystemUsesLightTheme") == 0;

    static int Read(string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Key);
            return key?.GetValue(name) is int value ? value : 1;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return 1;
        }
    }
}
