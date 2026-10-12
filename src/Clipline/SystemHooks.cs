using System.Windows.Interop;

namespace Clipline;

/// <summary>
/// A hidden window that receives what Windows tells the app: the shortcut,
/// clipboard changes, clicks on the tray icon, theme and display changes.
/// It is a real top-level window, not message-only, so broadcasts reach it.
/// </summary>
internal sealed class MessageWindow : IDisposable
{
    public const int TrayMessage = Native.WM_APP + 1;
    static readonly int TaskbarCreatedMessage = Native.RegisterWindowMessage("TaskbarCreated");

    readonly HwndSource source;

    public MessageWindow()
    {
        source = new HwndSource(new HwndSourceParameters("Clipline")
        {
            WindowStyle = Native.WS_POPUP,
            ExtendedWindowStyle = Native.WS_EX_TOOLWINDOW,
            Width = 0,
            Height = 0,
            PositionX = -32000,
            PositionY = -32000,
        });
        source.AddHook(Hook);
    }

    public nint Handle => source.Handle;

    public event Action<int>? HotKey;
    public event Action? ClipboardChanged;
    public event Action<int, PixelPoint>? Tray;
    public event Action? TaskbarCreated;
    public event Action? ThemeChanged;
    public event Action? DisplayChanged;

    nint Hook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        switch (msg)
        {
            case Native.WM_HOTKEY:
                HotKey?.Invoke((int)wParam);
                handled = true;
                break;
            case Native.WM_CLIPBOARDUPDATE:
                ClipboardChanged?.Invoke();
                break;
            case TrayMessage:
                // Version 4: the event in the low word of lParam, the anchor point in wParam.
                var at = new PixelPoint((short)(wParam.ToInt64() & 0xFFFF), (short)((wParam.ToInt64() >> 16) & 0xFFFF));
                Tray?.Invoke((int)(lParam.ToInt64() & 0xFFFF), at);
                handled = true;
                break;
            case Native.WM_SETTINGCHANGE:
                if (lParam != 0 && System.Runtime.InteropServices.Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet") ThemeChanged?.Invoke();
                break;
            case Native.WM_DISPLAYCHANGE:
                DisplayChanged?.Invoke();
                break;
            default:
                if (msg == TaskbarCreatedMessage) TaskbarCreated?.Invoke();
                break;
        }
        return 0;
    }

    public void Dispose()
    {
        source.RemoveHook(Hook);
        source.Dispose();
    }
}

/// <summary>The global show-and-hide shortcut.</summary>
internal sealed class HotKeyRegistration(MessageWindow window) : IDisposable
{
    const int Id = 0xC11;
    const uint NoRepeat = 0x4000;
    bool registered;

    public Hotkey? Current { get; private set; }

    /// <summary>Registers the shortcut. Returns false when another app already owns it.</summary>
    public bool Set(Hotkey? hotkey)
    {
        Clear();
        if (hotkey == null || !hotkey.IsValid) return hotkey == null;
        registered = Native.RegisterHotKey(window.Handle, Id, (uint)hotkey.Modifiers | NoRepeat, (uint)hotkey.Key);
        if (registered) Current = hotkey;
        return registered;
    }

    public void Clear()
    {
        if (registered) Native.UnregisterHotKey(window.Handle, Id);
        registered = false;
        Current = null;
    }

    public bool Matches(int id) => id == Id;

    public void Dispose() => Clear();
}

/// <summary>
/// The icon in the notification area. On the Mac the line lives in the menu
/// bar; on Windows its menu lives here. A click shows or hides the line.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    const int Id = 1;
    readonly MessageWindow window;
    nint icon;
    string tip = "Clipline";
    bool added;

    public TrayIcon(MessageWindow window)
    {
        this.window = window;
        window.Tray += OnTray;
        window.TaskbarCreated += () =>
        {
            // Explorer restarted: the icon has to be added again.
            added = false;
            Add();
        };
    }

    public event Action? Clicked;
    public event Action<PixelPoint>? MenuRequested;

    public void Set(nint newIcon, string newTip)
    {
        var old = icon;
        icon = newIcon;
        tip = newTip;
        if (added)
        {
            var data = Data(Native.NIF_ICON | Native.NIF_TIP | Native.NIF_SHOWTIP);
            Native.Shell_NotifyIcon(Native.NIM_MODIFY, ref data);
        }
        else
        {
            Add();
        }
        if (old != 0) Native.DestroyIcon(old);
    }

    void Add()
    {
        if (icon == 0) return;
        var data = Data(Native.NIF_MESSAGE | Native.NIF_ICON | Native.NIF_TIP | Native.NIF_SHOWTIP);
        if (!Native.Shell_NotifyIcon(Native.NIM_ADD, ref data)) return;
        data.uVersion = Native.NOTIFYICON_VERSION_4;
        Native.Shell_NotifyIcon(Native.NIM_SETVERSION, ref data);
        added = true;
    }

    /// <summary>A notification from the icon, the way Windows shows them now: as a toast.</summary>
    public void Notify(string title, string text)
    {
        if (!added) return;
        var data = Data(Native.NIF_INFO);
        data.szInfoTitle = title;
        data.szInfo = text;
        data.dwInfoFlags = Native.NIIF_NONE | Native.NIIF_NOSOUND;
        Native.Shell_NotifyIcon(Native.NIM_MODIFY, ref data);
    }

    Native.NOTIFYICONDATA Data(int flags) => new()
    {
        cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.NOTIFYICONDATA>(),
        hWnd = window.Handle,
        uID = Id,
        uFlags = flags,
        uCallbackMessage = MessageWindow.TrayMessage,
        hIcon = icon,
        szTip = tip,
        szInfo = "",
        szInfoTitle = "",
    };

    void OnTray(int message, PixelPoint at)
    {
        switch (message)
        {
            case Native.NIN_SELECT:
            case Native.NIN_KEYSELECT:
                Clicked?.Invoke();
                break;
            case Native.WM_CONTEXTMENU:
                MenuRequested?.Invoke(at);
                break;
        }
    }

    public void Dispose()
    {
        if (added)
        {
            var data = Data(0);
            Native.Shell_NotifyIcon(Native.NIM_DELETE, ref data);
        }
        added = false;
        if (icon != 0) Native.DestroyIcon(icon);
        icon = 0;
    }
}

/// <summary>
/// Native Windows menus, built fresh each time they open. They look like the
/// rest of Windows, follow its dark mode, and need no window of their own.
/// </summary>
internal sealed class NativeMenu
{
    readonly List<Action> actions;
    readonly List<nint> submenus;

    public NativeMenu() : this([], []) { }

    NativeMenu(List<Action> actions, List<nint> submenus)
    {
        this.actions = actions;
        this.submenus = submenus;
        Handle = Native.CreatePopupMenu();
    }

    public nint Handle { get; }

    public NativeMenu Item(string text, Action action, bool enabled = true, bool check = false, bool radio = false,
        string? shortcut = null, bool bold = false)
    {
        actions.Add(action);
        var info = new Native.MENUITEMINFO
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MENUITEMINFO>(),
            fMask = Native.MIIM_FTYPE | Native.MIIM_STATE | Native.MIIM_ID | Native.MIIM_STRING,
            fType = Native.MFT_STRING | (radio ? Native.MFT_RADIOCHECK : 0),
            fState = (enabled ? Native.MFS_ENABLED : Native.MFS_DISABLED) | (check ? Native.MFS_CHECKED : 0) | (bold ? Native.MFS_DEFAULT : 0),
            wID = (uint)actions.Count,
            dwTypeData = shortcut == null ? text : text + "\t" + shortcut,
        };
        Native.InsertMenuItem(Handle, (uint)Native.GetMenuItemCount(Handle), true, ref info);
        return this;
    }

    /// <summary>A line of text that does nothing, like the current shortcut.</summary>
    public NativeMenu Label(string text) => Item(text, () => { }, enabled: false);

    public NativeMenu Separator()
    {
        var info = new Native.MENUITEMINFO
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MENUITEMINFO>(),
            fMask = Native.MIIM_FTYPE,
            fType = Native.MFT_SEPARATOR,
        };
        Native.InsertMenuItem(Handle, (uint)Native.GetMenuItemCount(Handle), true, ref info);
        return this;
    }

    public NativeMenu Submenu(string text, Action<NativeMenu> build, bool enabled = true)
    {
        var sub = new NativeMenu(actions, submenus);
        submenus.Add(sub.Handle);
        build(sub);
        var info = new Native.MENUITEMINFO
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MENUITEMINFO>(),
            fMask = Native.MIIM_FTYPE | Native.MIIM_STATE | Native.MIIM_SUBMENU | Native.MIIM_STRING,
            fType = Native.MFT_STRING,
            fState = enabled ? Native.MFS_ENABLED : Native.MFS_DISABLED,
            hSubMenu = sub.Handle,
            dwTypeData = text,
        };
        Native.InsertMenuItem(Handle, (uint)Native.GetMenuItemCount(Handle), true, ref info);
        return this;
    }

    /// <summary>Shows the menu and runs the chosen item. The menu is destroyed afterwards.</summary>
    public void Show(nint owner, PixelPoint at, bool above = false)
    {
        // Without this the menu would not close when clicking elsewhere.
        Native.SetForegroundWindow(owner);
        var flags = Native.TPM_RETURNCMD | Native.TPM_RIGHTBUTTON | Native.TPM_NONOTIFY
            | (above ? Native.TPM_BOTTOMALIGN | Native.TPM_RIGHTALIGN : Native.TPM_TOPALIGN | Native.TPM_LEFTALIGN);
        var chosen = Native.TrackPopupMenuEx(Handle, flags, at.X, at.Y, owner, 0);
        Native.PostMessage(owner, Native.WM_NULL, 0, 0);
        // Destroying the root destroys its submenus too.
        Native.DestroyMenu(Handle);
        if (chosen > 0 && chosen <= actions.Count) actions[chosen - 1]();
    }

    /// <summary>Lets native menus follow the dark app mode. Undocumented but stable since Windows 10 1903.</summary>
    public static unsafe void AllowDarkMode(nint owner)
    {
        var uxtheme = Native.LoadLibrary("uxtheme.dll");
        if (uxtheme == 0) return;
        var setPreferredAppMode = (delegate* unmanaged[Stdcall]<int, int>)Native.GetProcAddress(uxtheme, 135);
        var allowDarkModeForWindow = (delegate* unmanaged[Stdcall]<nint, int, int>)Native.GetProcAddress(uxtheme, 133);
        var flushMenuThemes = (delegate* unmanaged[Stdcall]<void>)Native.GetProcAddress(uxtheme, 136);
        if (setPreferredAppMode != null) setPreferredAppMode(1);
        if (allowDarkModeForWindow != null) allowDarkModeForWindow(owner, 1);
        if (flushMenuThemes != null) flushMenuThemes();
    }
}
