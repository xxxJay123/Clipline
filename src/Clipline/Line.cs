using System.Windows.Threading;

namespace Clipline;

/// <summary>One picture hanging on the line.</summary>
internal sealed class Pegged(string path, BitmapSource thumb, int pixelWidth, int pixelHeight)
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Path { get; set; } = path;
    public BitmapSource Thumb { get; set; } = thumb;
    /// <summary>The size of the original, which sets the shape of its card.</summary>
    public int PixelWidth { get; set; } = pixelWidth;
    public int PixelHeight { get; set; } = pixelHeight;
    /// <summary>Every photo hangs a little crooked, like on a real line.</summary>
    public double Tilt { get; } = Random.Shared.NextDouble() * 5 - 2.5;
    public bool Falling { get; set; }
    /// <summary>Still flying in from where it was captured; the card waits hidden.</summary>
    public bool Flying { get; set; }
    public string? Fingerprint { get; set; }
    public DateTime HungAt { get; } = DateTime.UtcNow;
    public bool IsRecording => MediaKind.IsRecording(Path);
}

/// <summary>
/// The line itself: what hangs on it and what you can do with each photo.
/// The files never move unless you move them. The line is only a view onto them.
/// </summary>
internal sealed class Line
{
    readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    readonly Settings settings;
    readonly string statePath;
    readonly DispatcherTimer gustTimer;
    DispatcherTimer? saveTimer;
    LineMetrics metrics;

    public Line(Settings settings, string inbox, string statePath)
    {
        this.settings = settings;
        this.statePath = statePath;
        Inbox = inbox;
        metrics = new LineMetrics(settings.Size);
        gustTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher);
        gustTimer.Tick += (_, _) =>
        {
            ScheduleGust();
            if (LiveCount > 0 && DraggingId == null) Gust?.Invoke();
        };
        ScheduleGust();
    }

    public string Inbox { get; }
    public List<Pegged> Items { get; } = [];
    public IEnumerable<Pegged> Live => Items.Where(i => !i.Falling);
    public int LiveCount => Items.Count(i => !i.Falling);

    /// <summary>How many photos the line keeps before the oldest falls off.</summary>
    public int MaxItems { get; set; } = 8;
    /// <summary>How many fit across the screen. With Keep on line set, the rest are reached by scrolling.</summary>
    public int VisibleCount { get; set; } = 8;
    /// <summary>How far the line is scrolled towards older photos; 0 shows the newest.</summary>
    public double Scroll { get; private set; }
    public bool KeepsMore => settings.Keep != null;

    public Guid? CopiedId { get; private set; }
    public Guid? DraggingId { get; set; }
    public Guid? PressedId { get; set; }

    /// <summary>The list changed: something hung, started falling or left.</summary>
    public event Action? Changed;
    /// <summary>Just before a photo starts falling, so the fall can be drawn over the whole screen.</summary>
    public event Action<Pegged>? Falling;
    public event Action? Gust;
    public event Action<Guid>? Copied;
    public event Action? Scrolled;
    /// <summary>A thumbnail changed after the file was edited.</summary>
    public event Action<Pegged>? Refreshed;

    public LineMetrics Metrics
    {
        get => metrics;
        set => metrics = value;
    }

    /// <summary>Decodes thumbnails sharp enough for the sharpest screen.</summary>
    public double ScreenScale { get; set; } = 1;

    public Pegged? Find(Guid id) => Items.FirstOrDefault(i => i.Id == id);
    public Pegged? FindLive(string path) => Items.FirstOrDefault(i => !i.Falling && SamePath(i.Path, path));

    static bool SamePath(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    // Hanging and dropping

    /// <summary>
    /// Pegs a picture on the line. A file already hanging just gets its
    /// thumbnail refreshed, which is what happens when it is edited and saved.
    /// </summary>
    public async Task<Pegged?> HangAsync(string path, bool quietly = false, bool flying = false, string? fingerprint = null)
    {
        if (FindLive(path) is { } existing)
        {
            await RefreshAsync(existing);
            return null;
        }
        var loaded = await LoadAsync(path);
        if (loaded == null || FindLive(path) != null) return null;
        var (thumb, w, h) = loaded.Value;
        var item = new Pegged(path, thumb, w, h) { Flying = flying, Fingerprint = fingerprint };
        Items.Add(item);
        Scroll = 0;
        // A full line lets the oldest photo fall off the far end.
        if (LiveCount > MaxItems && Live.FirstOrDefault() is { } oldest) LetGo(oldest.Id);
        Changed?.Invoke();
        SaveSoon();
        if (!quietly) Sounds.Tink();
        return item;
    }

    async Task<(BitmapSource Thumb, int Width, int Height)?> LoadAsync(string path)
    {
        var pixels = metrics.ThumbnailPixels(ScreenScale);
        var result = await Task.Run(() =>
        {
            var thumb = Thumbnails.Load(path, pixels);
            if (thumb == null) return ((BitmapSource, int, int)?)null;
            var size = MediaKind.IsImage(path) ? Thumbnails.PixelSize(path) : null;
            return (thumb, size?.Width ?? thumb.PixelWidth, size?.Height ?? thumb.PixelHeight);
        });
        return result;
    }

    async Task RefreshAsync(Pegged item)
    {
        var loaded = await LoadAsync(item.Path);
        if (loaded == null || item.Falling) return;
        (item.Thumb, item.PixelWidth, item.PixelHeight) = loaded.Value;
        Refreshed?.Invoke(item);
    }

    /// <summary>After a change of size, every photo is redrawn sharp at the new one.</summary>
    public async Task ReloadThumbnailsAsync()
    {
        foreach (var item in Live.ToList()) await RefreshAsync(item);
    }

    /// <summary>The capture has reached the line: the real card takes over.</summary>
    public void Land(Guid id)
    {
        if (Find(id) is not { } item || !item.Flying) return;
        item.Flying = false;
        Changed?.Invoke();
    }

    /// <summary>
    /// A clipboard copy of a capture that Snipping Tool also saved as a file:
    /// the photo keeps its place and now stands for the file.
    /// </summary>
    public void Adopt(Pegged item, string path)
    {
        var old = item.Path;
        item.Path = path;
        SaveSoon();
        if (Clipline.Core.Inbox.Owns(Inbox, old)) TryDelete(old);
    }

    static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public void Drop(Guid id, bool quietly = false)
    {
        if (Find(id) is not { } item || item.Falling) return;
        Falling?.Invoke(item);
        item.Falling = true;
        if (CopiedId == id) CopiedId = null;
        Changed?.Invoke();
        SaveSoon();
        if (!quietly) Sounds.Pop();
        After(0.6, () =>
        {
            Items.Remove(item);
            Changed?.Invoke();
        });
    }

    /// <summary>Take everything down: every photo goes the way of its corner cross.</summary>
    public void Clear() => TakeDown(Live.ToList());

    /// <summary>The photos hung before (left) or after (right) this one.</summary>
    public List<Pegged> Neighbours(Guid id, bool toTheLeft)
    {
        var live = Live.ToList();
        var i = live.FindIndex(p => p.Id == id);
        if (i < 0) return [];
        return toTheLeft ? live[..i] : live[(i + 1)..];
    }

    public void TakeDown(Guid id, bool toTheLeft) => TakeDown(Neighbours(id, toTheLeft));

    /// <summary>One after another, and only the first makes a sound.</summary>
    void TakeDown(List<Pegged> items)
    {
        for (var n = 0; n < items.Count; n++)
        {
            var item = items[n];
            var quietly = n > 0;
            After(0.06 * n, () => Discard(item.Id, quietly));
        }
    }

    /// <summary>Photos whose file was deleted or moved away fall off by themselves.</summary>
    public void Prune()
    {
        foreach (var item in Live.ToList())
            if (!File.Exists(item.Path)) Drop(item.Id, quietly: true);
    }

    public void Removed(string path)
    {
        if (FindLive(path) is { } item && !File.Exists(item.Path)) Drop(item.Id, quietly: true);
    }

    // Actions on one photo

    public void Copy(Guid id)
    {
        if (Find(id) is not { } item) return;
        var image = item.IsRecording ? null : ShellOps.FullImage(item.Path);
        if (!ShellOps.Copy(item.Path, image))
        {
            ShellOps.Beep();
            return;
        }
        CopiedId = id;
        Copied?.Invoke(id);
        After(1.2, () =>
        {
            if (CopiedId != id) return;
            CopiedId = null;
            Copied?.Invoke(id);
        });
    }

    public void Open(Guid id)
    {
        if (Find(id) is { } item) ShellOps.Open(item.Path);
    }

    public void Edit(Guid id)
    {
        if (Find(id) is { } item) ShellOps.Edit(item.Path);
    }

    public void ShowInExplorer(Guid id)
    {
        if (Find(id) is { } item) ShellOps.ShowInExplorer(item.Path);
    }

    /// <summary>Sends the file to the Recycle Bin and takes the photo off the line.</summary>
    public bool Recycle(Guid id, bool quietly = false)
    {
        if (Find(id) is not { } item) return false;
        if (ShellOps.Recycle(item.Path))
        {
            if (!quietly) Sounds.Recycle();
            Drop(id, quietly: true);
            return true;
        }
        if (!quietly) ShellOps.Beep();
        return false;
    }

    /// <summary>Whether the file lives in Clipline's own folder, where nobody else would ever clear it out.</summary>
    public bool IsInInbox(Guid id) => Find(id) is { } item && Clipline.Core.Inbox.Owns(Inbox, item.Path);

    /// <summary>
    /// What the corner cross does: files from Clipline's folder go to the
    /// Recycle Bin, and so do screenshots when that is turned on. Anything
    /// else only leaves the line.
    /// </summary>
    public bool DiscardRecycles(Guid id) => IsInInbox(id) || settings.RecycleDiscarded;

    public void Discard(Guid id, bool quietly = false)
    {
        if (DiscardRecycles(id)) Recycle(id, quietly);
        else Drop(id, quietly);
    }

    /// <summary>Keeps a file from Clipline's folder by moving it to the Desktop.</summary>
    public void SaveToDesktop(Guid id)
    {
        if (Find(id) is not { } item) return;
        if (ShellOps.MoveTo(item.Path, ShellOps.Desktop) == null)
        {
            ShellOps.Beep();
            return;
        }
        Drop(id, quietly: true);
    }

    /// <summary>The oldest photo falling off a full line. Like the cross, Clipline's own files go to the Recycle Bin.</summary>
    void LetGo(Guid id)
    {
        if (IsInInbox(id) && Recycle(id, quietly: true)) return;
        Drop(id, quietly: true);
    }

    /// <summary>After Keep on line is lowered, the oldest photos past it go the way of a full line.</summary>
    public void Trim()
    {
        while (LiveCount > MaxItems && Live.FirstOrDefault() is { } oldest) LetGo(oldest.Id);
        Scroll = 0;
        Scrolled?.Invoke();
    }

    /// <summary>Moves along a line that holds more than fit.</summary>
    public void ScrollBy(double delta)
    {
        var most = Math.Max(0, Items.Count - VisibleCount) * metrics.Spacing;
        var next = Math.Clamp(Scroll + delta, 0, most);
        if (Math.Abs(next - Scroll) < 0.01) return;
        Scroll = next;
        Scrolled?.Invoke();
    }

    public void ResetScroll()
    {
        if (Scroll == 0) return;
        Scroll = 0;
        Scrolled?.Invoke();
    }

    // Breeze

    /// <summary>Every so often a little wind moves the line. It is the detail that makes it feel like an object.</summary>
    void ScheduleGust()
    {
        gustTimer.Stop();
        gustTimer.Interval = TimeSpan.FromSeconds(7 + Random.Shared.NextDouble() * 9);
        gustTimer.Start();
    }

    // Persistence

    void SaveSoon()
    {
        saveTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (_, _) =>
        {
            saveTimer!.Stop();
            Save();
        }, dispatcher);
        saveTimer.Stop();
        saveTimer.Start();
    }

    public void Save()
    {
        try
        {
            JsonStore.Save(statePath, new LineState { Hanging = Live.Select(i => i.Path).ToList() });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Not saved this time; the next change tries again.
        }
    }

    /// <summary>
    /// Everything comes back as it was, without sound and without letting
    /// photos fall off: the capacity is not known until the line knows its screen.
    /// </summary>
    public async Task RestoreAsync()
    {
        var state = JsonStore.Load<LineState>(statePath);
        foreach (var path in state.Hanging.Where(File.Exists))
        {
            if (FindLive(path) != null) continue;
            var loaded = await LoadAsync(path);
            if (loaded == null) continue;
            var (thumb, w, h) = loaded.Value;
            Items.Add(new Pegged(path, thumb, w, h));
        }
        if (Items.Count > 0) Changed?.Invoke();
    }

    void After(double seconds, Action action)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = TimeSpan.FromSeconds(seconds) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            action();
        };
        timer.Start();
    }
}
