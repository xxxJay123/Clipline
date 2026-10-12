using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Clipline;

/// <summary>
/// Watches the folders Windows saves captures to and reports new pictures
/// once they are completely written. Clipline never takes screenshots itself:
/// you keep Win + Shift + S, Print Screen or any other tool, and the line
/// just picks them up.
/// </summary>
internal sealed class FolderWatcher : IDisposable
{
    readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    readonly List<FileSystemWatcher> watchers = [];
    readonly HashSet<string> settling = new(StringComparer.OrdinalIgnoreCase);
    readonly DispatcherTimer retry;
    List<string> folders = [];
    bool disposed;

    public FolderWatcher()
    {
        // A folder that does not exist yet, like Screen Recordings before the
        // first recording, is picked up once Windows creates it.
        retry = new DispatcherTimer(TimeSpan.FromSeconds(20), DispatcherPriority.Background, (_, _) => Rewatch(), dispatcher);
    }

    /// <summary>A new picture, written completely.</summary>
    public event Action<string>? Added;
    /// <summary>A picture changed in place, like an edit saved over it.</summary>
    public event Action<string>? Changed;
    /// <summary>A file was deleted or moved away.</summary>
    public event Action<string>? Gone;

    public IReadOnlyList<string> Folders => folders;

    public void Watch(IEnumerable<string> paths)
    {
        folders = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Rewatch();
    }

    void Rewatch()
    {
        foreach (var w in watchers) w.Dispose();
        watchers.Clear();
        var missing = false;
        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder))
            {
                missing = true;
                continue;
            }
            try
            {
                var w = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    InternalBufferSize = 64 * 1024,
                };
                w.Created += (_, e) => Post(() => Settle(e.FullPath, isNew: true));
                w.Changed += (_, e) => Post(() => Settle(e.FullPath, isNew: false));
                w.Renamed += (_, e) => Post(() =>
                {
                    Gone?.Invoke(e.OldFullPath);
                    Settle(e.FullPath, isNew: true);
                });
                w.Deleted += (_, e) => Post(() => Gone?.Invoke(e.FullPath));
                w.EnableRaisingEvents = true;
                watchers.Add(w);
            }
            catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                missing = true;
            }
        }
        if (missing) retry.Start();
        else retry.Stop();
    }

    void Post(Action action)
    {
        if (!disposed) dispatcher.BeginInvoke(action);
    }

    /// <summary>
    /// Waits until the file stops growing and nobody is writing it any more.
    /// Pictures take a moment; a recording is written for as long as it runs.
    /// </summary>
    async void Settle(string path, bool isNew)
    {
        if (!MediaKind.IsSupported(path) || MediaKind.IsTemporary(path)) return;
        if (!settling.Add(path)) return;
        try
        {
            var limit = DateTime.UtcNow + (MediaKind.IsRecording(path) ? TimeSpan.FromHours(3) : TimeSpan.FromMinutes(2));
            long last = -1;
            while (!disposed && DateTime.UtcNow < limit)
            {
                await Task.Delay(MediaKind.IsRecording(path) ? 1000 : 250);
                var previous = last;
                var (ready, size) = await Task.Run(() => Ready(path, previous));
                last = size;
                if (ready == null) return;
                if (ready == true) break;
            }
            if (disposed) return;
            if (isNew) Added?.Invoke(path);
            else Changed?.Invoke(path);
        }
        finally
        {
            settling.Remove(path);
        }
    }

    /// <summary>True when written completely, false while still being written, null when gone.</summary>
    static (bool? Ready, long Size) Ready(string path, long lastSize)
    {
        long size = -1;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return (null, size);
            if (info.Attributes.HasFlag(FileAttributes.Hidden) || info.Attributes.HasFlag(FileAttributes.ReparsePoint)) return (null, size);
            size = info.Length;
            if (size <= 0 || size != lastSize) return (false, size);
            // Sharing only with readers fails while any writer still has the file open.
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) { }
            return (true, size);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (false, size);
        }
    }

    public void Dispose()
    {
        disposed = true;
        retry.Stop();
        foreach (var w in watchers) w.Dispose();
        watchers.Clear();
    }
}

/// <summary>
/// Hangs images copied to the clipboard, like a capture from Win + Shift + S
/// or a picture copied in Paint. Each one is saved as a PNG in Clipline's own
/// folder, so on the line it behaves like any screenshot. Off until turned on.
/// Windows announces every clipboard change, so nothing is polled.
/// </summary>
internal sealed class ClipboardWatcher
{
    readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    readonly string inbox;
    uint ignoreUpTo;
    (string Hash, string Path)? last;
    bool enabled;

    public ClipboardWatcher(string inbox) => this.inbox = inbox;

    /// <summary>A copied image, saved in the inbox, with its fingerprint.</summary>
    public event Action<string, string?>? Saved;

    public bool Enabled
    {
        get => enabled;
        set
        {
            enabled = value;
            // Whatever is on the clipboard already is old news.
            if (value) ignoreUpTo = Native.GetClipboardSequenceNumber();
        }
    }

    public void OnClipboardChanged()
    {
        if (!enabled) return;
        var sequence = Native.GetClipboardSequenceNumber();
        if (sequence <= ignoreUpTo) return;
        ignoreUpTo = sequence;
        // Let the app that copied finish putting all its formats there.
        dispatcher.BeginInvoke(DispatcherPriority.Background, async () =>
        {
            await Task.Delay(120);
            await ReadAsync(sequence);
        });
    }

    async Task ReadAsync(uint sequence)
    {
        byte[]? png = null;
        BitmapSource? bitmap = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (!enabled || Native.GetClipboardSequenceNumber() != sequence) return;
            try
            {
                var data = Clipboard.GetDataObject();
                if (data == null) return;
                var formats = data.GetFormats(false);
                if (!ClipboardPolicy.IsPicture(formats, ReadDword(data, "CanIncludeInClipboardHistory"))) return;
                if (data.GetDataPresent("PNG", false) && data.GetData("PNG", false) is MemoryStream stream && stream.Length <= ClipboardPolicy.MaxBytes)
                    png = stream.ToArray();
                else if (Clipboard.ContainsImage())
                    bitmap = Clipboard.GetImage();
                break;
            }
            catch (COMException)
            {
                // Another app has the clipboard open for a moment.
                await Task.Delay(80);
            }
            catch (Exception e) when (e is OutOfMemoryException or InvalidOperationException or ArgumentException)
            {
                return;
            }
        }
        if (png == null && bitmap == null) return;
        bitmap?.Freeze();

        // Decoding a large image takes a moment, so it happens off the UI thread.
        var result = await Task.Run(() => Encode(png, bitmap));
        if (result == null || !enabled || Native.GetClipboardSequenceNumber() != sequence) return;
        var (bytes, hash) = result.Value;
        if (last is { } previous && previous.Hash == hash && File.Exists(previous.Path)) return;

        try
        {
            Directory.CreateDirectory(inbox);
            var path = Inbox.UniquePath(inbox, Inbox.ClipboardName(DateTime.Now));
            await File.WriteAllBytesAsync(path, bytes);
            last = (hash, path);
            Saved?.Invoke(path, hash);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Not saved; the next copy tries again.
        }
    }

    /// <summary>PNG bytes are kept as they are; a bitmap becomes PNG. Either way it must decode and have a sane size.</summary>
    static (byte[] Bytes, string Hash)? Encode(byte[]? png, BitmapSource? bitmap)
    {
        try
        {
            BitmapSource image;
            if (png != null)
            {
                var frame = BitmapDecoder.Create(new MemoryStream(png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
                image = frame;
            }
            else
            {
                image = bitmap!;
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using var output = new MemoryStream();
                encoder.Save(output);
                png = output.ToArray();
            }
            if (!ClipboardPolicy.IsSaneSize(image.PixelWidth, image.PixelHeight)) return null;
            var hash = Thumbnails.Fingerprint(image);
            return hash == null ? null : (png, hash);
        }
        catch (Exception e) when (e is NotSupportedException or FileFormatException or COMException or ArgumentException or InvalidOperationException or OverflowException)
        {
            return null;
        }
    }

    static uint? ReadDword(IDataObject data, string format)
    {
        try
        {
            if (!data.GetDataPresent(format, false)) return null;
            return data.GetData(format, false) switch
            {
                MemoryStream { Length: >= 4 } s => BitConverter.ToUInt32(s.ToArray(), 0),
                byte[] { Length: >= 4 } b => BitConverter.ToUInt32(b, 0),
                int i => (uint)i,
                uint u => u,
                _ => null,
            };
        }
        catch (Exception e) when (e is COMException or OutOfMemoryException or InvalidOperationException)
        {
            return null;
        }
    }
}

/// <summary>
/// Remembers the last few rectangles dragged with the left button, the way
/// a region snip is drawn, so that snip can fly up to the line from where it
/// was taken. Only the corners and the time are kept, in memory, for a few
/// seconds; nothing about what was clicked.
/// </summary>
internal sealed class DragTracker : IDisposable
{
    readonly object gate = new();
    readonly List<Drag> drags = [];
    readonly Thread thread;
    uint threadId;
    nint hook;
    Native.HookProc? proc;
    PixelPoint? down;

    public DragTracker()
    {
        // A hook of its own thread: a busy UI thread must never slow the pointer down.
        thread = new Thread(Run) { IsBackground = true, Name = "Clipline drag tracker" };
        thread.Start();
    }

    public IReadOnlyList<Drag> Recent()
    {
        lock (gate)
        {
            var cutoff = DateTime.UtcNow - CaptureMatch.DragWindow;
            drags.RemoveAll(d => d.EndedAt < cutoff);
            return drags.ToList();
        }
    }

    void Run()
    {
        threadId = Native.GetCurrentThreadId();
        proc = Callback;
        hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, proc, Native.GetModuleHandle(null), 0);
        while (Native.GetMessage(out _, 0, 0, 0) > 0) { }
        if (hook != 0) Native.UnhookWindowsHookEx(hook);
    }

    nint Callback(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            var message = (int)wParam;
            if (message is Native.WM_LBUTTONDOWN or Native.WM_LBUTTONUP)
            {
                var info = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);
                var point = new PixelPoint(info.pt.X, info.pt.Y);
                if (message == Native.WM_LBUTTONDOWN)
                {
                    down = point;
                }
                else if (down is { } start)
                {
                    down = null;
                    var rect = PixelRect.FromCorners(start, point);
                    if (rect.Width >= 8 && rect.Height >= 8)
                    {
                        lock (gate)
                        {
                            drags.Add(new Drag(rect, DateTime.UtcNow));
                            if (drags.Count > 8) drags.RemoveAt(0);
                        }
                    }
                }
            }
        }
        return Native.CallNextHookEx(hook, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (threadId != 0) Native.PostThreadMessage(threadId, Native.WM_QUIT, 0, 0);
    }
}

/// <summary>
/// Knows when to stay hidden: a video, a game or a presentation filling the
/// screen should not get a clothesline across the top.
/// </summary>
internal static class FullScreen
{
    static readonly HashSet<string> Shell = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow"];

    [DllImport("user32.dll")]
    static extern bool IsZoomed(nint hwnd);

    public static bool IsActive(Screen screen)
    {
        var window = Native.GetForegroundWindow();
        if (window == 0 || window == Native.GetShellWindow() || window == Native.GetDesktopWindow()) return false;
        if (Native.IsIconic(window) || IsZoomed(window)) return false;
        if (Shell.Contains(Native.ClassName(window))) return false;
        if (Native.DwmGetWindowAttribute(window, Native.DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0) return false;
        if (!Native.GetWindowRect(window, out var r)) return false;
        var b = screen.Bounds;
        return r.Left <= b.Left && r.Top <= b.Top && r.Right >= b.Right && r.Bottom >= b.Bottom;
    }
}
