using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Clipline;

internal sealed record Options(string DataFolder, bool Demo, bool Background);

/// <summary>
/// The app: the tray icon, the shortcut, the watchers, and when the line
/// comes down and goes back up.
/// </summary>
internal sealed class AppController : ICardHost, IDisposable
{
    static readonly Guid ScreenshotsFolderId = new("b7bede81-df94-4682-a7d8-57a52620b86f");
    /// <summary>How long the pointer rests against the top edge before the line comes down.</summary>
    static readonly TimeSpan RevealDelay = TimeSpan.FromMilliseconds(250);
    /// <summary>How long the pointer is away before the line tucks back up.</summary>
    static readonly TimeSpan RetractDelay = TimeSpan.FromMilliseconds(500);
    const int VK_LBUTTON = 0x01, VK_RBUTTON = 0x02, VK_MBUTTON = 0x04;

    readonly Options options;
    readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    readonly string settingsPath;
    readonly Settings settings;
    readonly Line line;
    readonly LineSurface surface;
    readonly LineWindow window;
    readonly MessageWindow messages;
    readonly TrayIcon tray;
    readonly HotKeyRegistration hotkey;
    readonly FolderWatcher folders = new();
    readonly ClipboardWatcher clipboard;
    readonly DragTracker? drags;
    readonly DispatcherTimer timer;
    readonly Dictionary<string, (string Path, DateTime At)> recentCaptures = [];
    LineMetrics metrics;
    ShortcutWindow? recorder;

    /// <summary>Whether the strip is allowed on screen. It can be present and still tucked away above the top edge.</summary>
    bool isPresent;
    /// <summary>Whether the line has slid down into view.</summary>
    bool isRevealed;
    /// <summary>Opened on purpose: it stays down until the pointer has visited it and left, or the shortcut is pressed again.</summary>
    bool pinned;
    /// <summary>Whether the line should be up, if nothing prevents it, like a full screen app.</summary>
    bool wanted;
    /// <summary>Set when the line is opened on purpose, so it stays up while empty.</summary>
    bool keepOpen;
    /// <summary>After a click in the top band the line stays away until the pointer leaves it, so it never comes down over a tab you meant to click.</summary>
    bool topSuppressed;
    bool dragging;
    bool menuOpen;
    bool buttonsWereDown;
    DateTime peekUntil = DateTime.MinValue;
    DateTime? hotZoneSince;
    DateTime? awaySince;

    public AppController(Options options)
    {
        this.options = options;
        Directory.CreateDirectory(options.DataFolder);
        settingsPath = Path.Combine(options.DataFolder, "settings.json");
        settings = JsonStore.Load<Settings>(settingsPath);
        Sounds.Enabled = settings.Sounds;
        metrics = new LineMetrics(settings.Size);

        line = new Line(settings, InboxFolder, Path.Combine(options.DataFolder, "line.json"));
        line.Metrics = metrics;
        surface = new LineSurface(line, this, metrics, Palette.Current);
        window = new LineWindow(surface);
        messages = new MessageWindow();
        NativeMenu.AllowDarkMode(messages.Handle);
        tray = new TrayIcon(messages);
        hotkey = new HotKeyRegistration(messages);
        clipboard = new ClipboardWatcher(InboxFolder);
        if (!options.Demo) drags = new DragTracker();

        timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(1000.0 / 60) };
        timer.Tick += (_, _) => Tick();
    }

    string InboxFolder => Path.Combine(options.DataFolder, "Inbox");

    public async void Start()
    {
        PlaceOn(Screens.UnderPointer());

        line.Changed += ItemsChanged;
        line.Falling += Fall;
        line.Gust += surface.Breeze;
        line.Copied += surface.SetCopied;
        line.Scrolled += surface.Retarget;
        line.Refreshed += surface.Refresh;
        surface.Tucked += () =>
        {
            if (!isRevealed) window.HideStrip();
        };
        window.MouseWheelScrolled += delta => line.ScrollBy(-delta / 120.0 * metrics.Spacing / 2);

        tray.Clicked += Toggle;
        tray.MenuRequested += at => ShowTrayMenu(at);
        UpdateTrayIcon();

        messages.HotKey += id =>
        {
            if (hotkey.Matches(id)) Toggle();
        };
        messages.ClipboardChanged += clipboard.OnClipboardChanged;
        messages.ThemeChanged += () =>
        {
            surface.ApplyPalette(Palette.Current);
            UpdateTrayIcon();
            NativeMenu.AllowDarkMode(messages.Handle);
        };
        messages.DisplayChanged += () => dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            PlaceOn(window.Screen is { } s ? Screens.At(new PixelPoint(s.Bounds.Left + s.Bounds.Width / 2, s.Bounds.Top + 1)) : Screens.UnderPointer());
        });
        if (!hotkey.Set(settings.Shortcut)) hotkey.Set(null);

        folders.Added += path => HangCapture(path, null);
        folders.Changed += path =>
        {
            if (line.FindLive(path) != null) _ = line.HangAsync(path, quietly: true);
        };
        folders.Gone += line.Removed;
        clipboard.Saved += (path, hash) => HangCapture(path, hash);
        if (!options.Demo)
        {
            WatchFolders();
            Native.AddClipboardFormatListener(messages.Handle);
            clipboard.Enabled = settings.HangCopiedImages;
        }

        await line.RestoreAsync();
        surface.Sync();
        // Photos kept from last time show themselves for a moment.
        if (line.LiveCount > 0) ComeDown(null);

        if (!settings.Welcomed && !options.Demo)
        {
            settings.Welcomed = true;
            SaveSettings();
            keepOpen = true;
            wanted = true;
            Refresh();
            Reveal(pinned: true);
            tray.Notify(L("Clipline is on the line"),
                L("Rest the pointer at the top edge of the screen to bring your screenshots down. Settings are in this tray icon."));
            After(5, () =>
            {
                if (line.LiveCount > 0) return;
                keepOpen = false;
                wanted = false;
                Refresh();
            });
        }
    }

    /// <summary>Hangs pictures without any watcher, for the demo.</summary>
    public async Task HangAsync(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            await line.HangAsync(path, quietly: true);
            await Task.Delay(140);
        }
        keepOpen = true;
        wanted = true;
        Refresh();
        Reveal(pinned: true);
    }

    // Folders

    IEnumerable<(string Path, bool Default)> CandidateFolders()
    {
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        var videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        yield return (Native.KnownFolder(ScreenshotsFolderId) ?? Path.Combine(pictures, "Screenshots"), true);
        yield return (Path.Combine(videos, "Screen Recordings"), true);
        yield return (Path.Combine(videos, "Captures"), true);
        foreach (var extra in settings.ExtraFolders) yield return (extra, false);
    }

    bool IsMuted(string folder) => settings.MutedFolders.Contains(folder, StringComparer.OrdinalIgnoreCase);

    void WatchFolders() =>
        folders.Watch(CandidateFolders().Where(f => !f.Default || !IsMuted(f.Path)).Select(f => f.Path));

    void AddFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = L("Choose a folder to watch") };
        if (dialog.ShowDialog() != true) return;
        var folder = dialog.FolderName;
        if (CandidateFolders().Any(f => string.Equals(f.Path, folder, StringComparison.OrdinalIgnoreCase)))
        {
            settings.MutedFolders.RemoveAll(m => string.Equals(m, folder, StringComparison.OrdinalIgnoreCase));
        }
        else if (settings.ExtraFolders.Count < Settings.MaxExtraFolders)
        {
            settings.ExtraFolders.Add(folder);
        }
        SaveSettings();
        WatchFolders();
    }

    void ToggleFolder(string folder, bool isDefault)
    {
        if (isDefault)
        {
            if (IsMuted(folder)) settings.MutedFolders.RemoveAll(m => string.Equals(m, folder, StringComparison.OrdinalIgnoreCase));
            else settings.MutedFolders.Add(folder);
        }
        else
        {
            settings.ExtraFolders.RemoveAll(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));
        }
        SaveSettings();
        WatchFolders();
    }

    // A capture arriving

    /// <summary>
    /// A new capture comes down the line on the screen it was taken on, and
    /// flies up from where it was taken when that can be worked out.
    /// </summary>
    async void HangCapture(string path, string? fingerprint)
    {
        if (line.FindLive(path) != null)
        {
            await line.HangAsync(path, quietly: true);
            return;
        }
        if (await IsTwin(path, fingerprint)) return;

        var size = await Task.Run(() => MediaKind.IsImage(path) ? Thumbnails.PixelSize(path) : null);
        PixelRect? from = null;
        if (size is { } s && drags != null)
            from = CaptureMatch.Find(s.Width, s.Height, DateTime.UtcNow, drags.Recent(), Screens.All().Select(x => x.Bounds).ToList(), Native.Cursor());
        var screen = from is { } f ? Screens.At(new PixelPoint(f.Left + f.Width / 2, f.Top + f.Height / 2)) : null;

        // Every capture brings the line down before it hangs. On a full line
        // the oldest photo then falls in view as the new one flies in.
        ComeDown(screen);
        var flying = from != null && isPresent && OverlayWindow.Flights < 2;
        var item = await line.HangAsync(path, flying: flying, fingerprint: fingerprint);
        if (item == null)
        {
            // Nothing hung after all, so an empty line goes away again.
            ItemsChanged();
            return;
        }
        if (flying && from != null) await Fly(item, from.Value);
    }

    /// <summary>
    /// With copied images hanging too, one Win + Shift + S arrives twice: on
    /// the clipboard and as a file in Screenshots. It hangs once, and the
    /// photo stands for the file.
    /// </summary>
    async Task<bool> IsTwin(string path, string? fingerprint)
    {
        if (!clipboard.Enabled) return false;
        fingerprint ??= await Task.Run(() => Thumbnails.FingerprintOf(path));
        if (fingerprint == null) return false;
        var now = DateTime.UtcNow;
        foreach (var stale in recentCaptures.Where(r => now - r.Value.At > TimeSpan.FromSeconds(30)).Select(r => r.Key).ToList())
            recentCaptures.Remove(stale);

        var thisIsCopy = Inbox.Owns(InboxFolder, path);
        if (!recentCaptures.TryGetValue(fingerprint, out var first))
        {
            recentCaptures[fingerprint] = (path, now);
            return false;
        }
        if (thisIsCopy)
        {
            TryDelete(path);
            return true;
        }
        if (!Inbox.Owns(InboxFolder, first.Path)) return true;
        recentCaptures[fingerprint] = (path, now);
        // The copy may still be on its way onto the line.
        for (var i = 0; i < 50; i++)
        {
            if (line.FindLive(first.Path) is { } copy)
            {
                line.Adopt(copy, path);
                return true;
            }
            await Task.Delay(100);
        }
        return false;
    }

    static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    async Task Fly(Pegged item, PixelRect from)
    {
        // Let the line come down and lay out before measuring the landing spot.
        await dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.Loaded);
        var screen = window.Screen;
        var target = surface.CardRect(item.Id, final: true);
        var pixels = Math.Clamp(Math.Max(from.Width, from.Height), 400, 1500);
        var image = await Thumbnails.LoadAsync(item.Path, pixels);
        if (!isPresent || !isRevealed || screen == null || target == null || image == null)
        {
            Landed(item.Id);
            return;
        }
        var overlay = OverlayWindow.For(screen);
        overlay.Add(new Flight(image, overlay.ToLocal(from), overlay.ToLocal(window.ToScreen(target.Value)), item.Tilt, Palette.Current,
            () => Landed(item.Id)));
    }

    void Landed(Guid id)
    {
        line.Land(id);
        surface.Land(id);
    }

    /// <summary>A discarded card falls over the whole screen, from where it hangs.</summary>
    void Fall(Pegged item)
    {
        if (!isPresent || !isRevealed || item.Flying || window.Screen is not { } screen) return;
        if (surface.CardRect(item.Id, final: false) is not { } rect) return;
        var overlay = OverlayWindow.For(screen);
        overlay.Add(new Fall(item.Thumb, overlay.ToLocal(window.ToScreen(rect)), item.Tilt, Palette.Current));
    }

    // Showing and hiding

    /// <summary>An empty line goes away, unless it was opened on purpose.</summary>
    void ItemsChanged()
    {
        surface.Sync();
        surface.RefreshCrosses();
        if (line.LiveCount > 0 || keepOpen) return;
        After(0.7, () =>
        {
            if (line.LiveCount > 0 || keepOpen) return;
            wanted = false;
            Refresh();
        });
    }

    /// <summary>The line comes down on that screen for a moment, then tucks away.</summary>
    void ComeDown(Screen? screen)
    {
        PlaceOn(screen ?? Screens.UnderPointer());
        wanted = true;
        Refresh();
        if (settings.PeekAtNew) Reveal(peekFor: 2.5);
    }

    void PlaceOn(Screen screen)
    {
        window.Place(screen, metrics.PanelHeight);
        line.ScreenScale = Screens.All().Max(s => s.Scale);
        UpdateCapacity();
    }

    void UpdateCapacity()
    {
        if (window.Screen is not { } screen) return;
        var width = screen.WorkArea.Width / screen.Scale;
        var fits = metrics.VisibleCount(width);
        line.VisibleCount = fits;
        line.MaxItems = Math.Max(fits, settings.Keep ?? fits);
        surface.Retarget();
    }

    /// <summary>Decides whether the strip may be on screen at all: something to show, and no full screen app.</summary>
    void Refresh()
    {
        var screen = window.Screen ?? Screens.UnderPointer();
        var blocked = !settings.OverFullScreen && FullScreen.IsActive(screen);
        if (wanted && !blocked) Present();
        else Dismiss();
        // The pointer is watched while there is a line, even tucked away, to notice it resting at the top edge.
        if (wanted) timer.Start();
        else
        {
            timer.Stop();
            window.SetClickThrough(true);
        }
    }

    void Present()
    {
        if (isPresent) return;
        isPresent = true;
        if (isRevealed) window.ShowStrip();
    }

    void Dismiss()
    {
        if (!isPresent) return;
        isPresent = false;
        SetRevealed(false);
    }

    void Reveal(bool pinned = false, double peekFor = 0)
    {
        if (!isPresent) return;
        if (pinned) this.pinned = true;
        if (peekFor > 0) peekUntil = DateTime.UtcNow.AddSeconds(peekFor);
        awaySince = null;
        SetRevealed(true);
    }

    void SetRevealed(bool on)
    {
        if (on == isRevealed) return;
        isRevealed = on;
        if (on)
        {
            // It always comes down showing the newest.
            line.ResetScroll();
            if (isPresent) window.ShowStrip();
            surface.SetRevealed(true);
        }
        else
        {
            pinned = false;
            peekUntil = DateTime.MinValue;
            window.SetClickThrough(true);
            surface.SetHover(null);
            surface.SetRevealed(false);
        }
    }

    void Toggle()
    {
        if (isRevealed)
        {
            SetRevealed(false);
            if (line.LiveCount == 0)
            {
                keepOpen = false;
                wanted = false;
                Refresh();
            }
        }
        else
        {
            keepOpen = true;
            wanted = true;
            PlaceOn(Screens.UnderPointer());
            Refresh();
            Reveal(pinned: true);
        }
    }

    void Tick()
    {
        var mouse = Native.Cursor();
        var now = DateTime.UtcNow;
        var screen = Screens.At(mouse);
        var inTop = screen.TopBand.Contains(mouse);
        if (!inTop) topSuppressed = false;

        var buttons = Native.IsDown(VK_LBUTTON) || Native.IsDown(VK_RBUTTON) || Native.IsDown(VK_MBUTTON);
        var pressed = buttons && !buttonsWereDown;
        buttonsWereDown = buttons;

        if (!isRevealed)
        {
            // A click at the top edge, on a tab or a title bar, keeps the line away until the pointer leaves the edge.
            if (pressed && inTop) topSuppressed = true;
            // Resting at the top edge brings the line down on that screen. Not
            // while a button is held: that is a window being dragged up to snap.
            if (settings.RevealAtTopEdge && inTop && !buttons && !topSuppressed
                && (settings.OverFullScreen || !FullScreen.IsActive(screen)))
            {
                hotZoneSince ??= now;
                if (now - hotZoneSince >= RevealDelay)
                {
                    hotZoneSince = null;
                    if (!screen.SameDisplay(window.Screen)) PlaceOn(screen);
                    Refresh();
                    Reveal();
                }
            }
            else
            {
                hotZoneSince = null;
            }
            return;
        }

        var over = UpdatePointer(mouse);
        if (pressed && inTop && over == null && !dragging && !menuOpen)
        {
            topSuppressed = true;
            hotZoneSince = null;
            SetRevealed(false);
            return;
        }

        // The line's zone runs from its lowest point up to the top of the screen, so moving up never hides it.
        var frame = window.Frame;
        var top = window.Screen?.Bounds.Top ?? frame.Top;
        var zone = new PixelRect(frame.Left, top, frame.Width, frame.Bottom - top);
        var inside = zone.Contains(mouse);
        if (inside && pinned) pinned = false;

        var busy = pinned || dragging || menuOpen || line.PressedId != null || now < peekUntil || recorder != null;
        if (inside || busy)
        {
            awaySince = null;
        }
        else
        {
            awaySince ??= now;
            if (now - awaySince >= RetractDelay)
            {
                awaySince = null;
                SetRevealed(false);
            }
        }
    }

    /// <summary>
    /// The strip spans the whole width of the screen, so it only takes the
    /// mouse while the pointer is over a photo. Everywhere else, clicks go to
    /// whatever is underneath. Returns the photo under the pointer.
    /// </summary>
    Guid? UpdatePointer(PixelPoint mouse)
    {
        if (dragging || menuOpen) return line.DraggingId;
        var local = window.ToLocal(mouse);
        Guid? over = null;
        var band = Rect.Empty;
        foreach (var (id, rect) in surface.HitRects())
        {
            var r = rect;
            r.Inflate(4, 4);
            if (r.Contains(local)) over = id;
            band.Union(rect);
        }
        var catches = over != null;
        // A line longer than the screen also takes the band the photos hang in, gaps included, so the wheel works anywhere along it.
        if (!catches && line.KeepsMore && line.Items.Count > line.VisibleCount && !band.IsEmpty)
            catches = local.Y >= band.Top - 4 && local.Y <= band.Bottom + 4 && local.X >= 0 && local.X <= window.ActualWidth;
        window.SetClickThrough(!catches);
        surface.SetHover(line.PressedId ?? over);
        return over;
    }

    // A photo asks

    public void CardClicked(CardView card) => line.Copy(card.Id);
    public void CardDoubleClicked(CardView card) => line.Open(card.Id);
    public void CardHeld(CardView card) => line.Edit(card.Id);
    public void CardCross(CardView card) => line.Discard(card.Id);
    public void CardPressChanged(CardView card, bool pressed) => line.PressedId = pressed ? card.Id : null;

    public void CardPreview(CardView card)
    {
        if (window.Screen is not { } screen) return;
        var live = line.Live.ToList();
        var index = live.FindIndex(i => i.Id == card.Id);
        var rect = surface.CardRect(card.Id, final: false);
        PreviewWindow.Present(live.Select(i => i.Path).ToList(), index, screen, rect is { } r ? window.ToScreen(r) : null);
    }

    public void CardDrag(CardView card)
    {
        if (line.Find(card.Id) is not { } item || window.Screen is not { } screen) return;
        dragging = true;
        line.DraggingId = item.Id;
        surface.SetDragging(item.Id, true);
        var photo = metrics.PhotoSize(item.PixelWidth, item.PixelHeight);
        var effect = DragOut.Run(item.Path, item.Thumb, new Size(photo.Width, photo.Height), screen.Scale);
        dragging = false;
        line.DraggingId = null;
        surface.SetDragging(item.Id, false);
        // Moved into a folder: it is kept where you wanted it. Explorer finishes a move a moment later.
        line.Prune();
        if (effect != Forms.DragDropEffects.Copy) After(0.6, line.Prune);
    }

    public void CardMenu(CardView card)
    {
        if (line.Find(card.Id) is not { } item) return;
        var id = item.Id;
        var menu = new NativeMenu()
            .Item(L("Copy"), () => line.Copy(id), bold: true)
            .Item(L("Open"), () => line.Open(id))
            .Item(L("Preview"), () => CardPreview(card))
            .Item(L("Edit"), () => line.Edit(id), enabled: !item.IsRecording)
            .Item(L("Show in File Explorer"), () => line.ShowInExplorer(id));
        if (line.IsInInbox(id)) menu.Item(L("Save to Desktop"), () => line.SaveToDesktop(id));
        menu.Separator();
        if (line.DiscardRecycles(id))
        {
            menu.Item(L("Discard"), () => line.Discard(id));
        }
        else
        {
            menu.Item(L("Take down"), () => line.Discard(id));
            menu.Item(L("Move to Recycle Bin"), () => line.Recycle(id));
        }
        menu.Item(L("Take down everything to the left"), () => line.TakeDown(id, true), enabled: line.Neighbours(id, true).Count > 0);
        menu.Item(L("Take down everything to the right"), () => line.TakeDown(id, false), enabled: line.Neighbours(id, false).Count > 0);
        // The menu reaches past the line, so the line waits for it.
        menuOpen = true;
        window.SetClickThrough(true);
        menu.Show(messages.Handle, Native.Cursor());
        menuOpen = false;
    }

    // The tray menu

    void ShowTrayMenu(PixelPoint at)
    {
        var menu = new NativeMenu();
        menu.Item(isRevealed ? L("Hide line") : L("Show line"), Toggle, shortcut: hotkey.Current?.Display, bold: true);
        menu.Item(L("Take everything down"), line.Clear, enabled: line.LiveCount > 0);
        menu.Separator();
        menu.Item(L("Hang copied images"), () =>
        {
            settings.HangCopiedImages = !settings.HangCopiedImages;
            clipboard.Enabled = settings.HangCopiedImages;
            SaveSettings();
            UpdateTrayIcon();
        }, check: settings.HangCopiedImages);
        menu.Submenu(L("Bring the line down"), m => m
            .Item(L("When the pointer rests at the top edge"), () => Set(s => s.RevealAtTopEdge = !s.RevealAtTopEdge), check: settings.RevealAtTopEdge)
            .Item(L("When something new hangs"), () => Set(s => s.PeekAtNew = !s.PeekAtNew), check: settings.PeekAtNew)
            .Item(L("Over full screen apps"), () =>
            {
                Set(s => s.OverFullScreen = !s.OverFullScreen);
                Refresh();
            }, check: settings.OverFullScreen));
        menu.Submenu(L("Watch folders"), m =>
        {
            foreach (var (folder, isDefault) in CandidateFolders())
            {
                var on = !isDefault || !IsMuted(folder);
                var exists = Directory.Exists(folder);
                m.Item(Path.GetFileName(folder.TrimEnd('\\')), () => ToggleFolder(folder, isDefault), check: on && exists, enabled: exists || !isDefault);
            }
            m.Separator();
            m.Item(L("Add folder…"), AddFolder, enabled: settings.ExtraFolders.Count < Settings.MaxExtraFolders);
        });
        menu.Submenu(L("Keep on line"), m =>
        {
            m.Item(L("As many as fit"), () => SetKeep(0), check: settings.Keep == null, radio: true);
            foreach (var n in Settings.KeepChoices)
                m.Item(L("{0} photos", n), () => SetKeep(n), check: settings.Keep == n, radio: true);
        });
        menu.Submenu(L("Size"), m =>
        {
            foreach (var (size, name) in new[] { (LineSize.Small, L("Small")), (LineSize.Medium, L("Medium")), (LineSize.Large, L("Large")) })
                m.Item(name, () => SetSize(size), check: settings.Size == size, radio: true);
        });
        menu.Submenu(L("Shortcut"), m => m
            .Label(settings.Shortcut?.Display ?? L("None"))
            .Item(L("Change shortcut…"), ChangeShortcut));
        menu.Item(L("Send discarded screenshots to the Recycle Bin"), () =>
        {
            Set(s => s.RecycleDiscarded = !s.RecycleDiscarded);
            surface.RefreshCrosses();
        }, check: settings.RecycleDiscarded);
        menu.Item(L("Open inbox folder"), () => ShellOps.OpenFolder(InboxFolder));
        menu.Separator();
        menu.Item(L("Sounds"), () =>
        {
            Set(s => s.Sounds = !s.Sounds);
            Sounds.Enabled = settings.Sounds;
        }, check: settings.Sounds);
        if (!options.Demo)
        {
            var startup = Startup.IsEnabled;
            menu.Item(L("Start with Windows"), () => Startup.Set(!startup), check: startup);
        }
        menu.Separator();
        menu.Item(L("Quit Clipline"), () => Application.Current.Shutdown());
        menuOpen = true;
        menu.Show(messages.Handle, at, above: true);
        menuOpen = false;
    }

    void Set(Action<Settings> change)
    {
        change(settings);
        SaveSettings();
    }

    void SetKeep(int n)
    {
        if (settings.KeepOnLine == n) return;
        Set(s => s.KeepOnLine = n);
        UpdateCapacity();
        line.Trim();
    }

    async void SetSize(LineSize size)
    {
        if (settings.Size == size) return;
        Set(s => s.Size = size);
        metrics = new LineMetrics(size);
        line.Metrics = metrics;
        surface.SetMetrics(metrics);
        if (window.Screen is { } screen) PlaceOn(screen);
        await line.ReloadThumbnailsAsync();
    }

    void ChangeShortcut()
    {
        if (recorder != null)
        {
            recorder.Activate();
            return;
        }
        // Off while recording, so pressing the current one records it instead of showing the line.
        hotkey.Clear();
        recorder = new ShortcutWindow(settings.Shortcut, next =>
        {
            if (next != null && !hotkey.Set(next)) return false;
            settings.Shortcut = next;
            SaveSettings();
            return true;
        }, () =>
        {
            recorder = null;
            if (!hotkey.Set(settings.Shortcut)) hotkey.Set(null);
        });
        recorder.Show();
        recorder.Activate();
    }

    void UpdateTrayIcon()
    {
        var on = settings.HangCopiedImages;
        tray.Set(Icons.Tray(on), on ? L("Clipline, hanging copied images") : L("Clipline"));
    }

    void SaveSettings()
    {
        try
        {
            JsonStore.Save(settingsPath, settings);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Kept in memory; written with the next change.
        }
    }

    void After(double seconds, Action action)
    {
        var t = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = TimeSpan.FromSeconds(seconds) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            action();
        };
        t.Start();
    }

    public void Dispose()
    {
        timer.Stop();
        line.Save();
        Native.RemoveClipboardFormatListener(messages.Handle);
        folders.Dispose();
        drags?.Dispose();
        hotkey.Dispose();
        tray.Dispose();
        messages.Dispose();
    }

    /// <summary>Another launch asked this one to show the line.</summary>
    public void ShowFromOutside()
    {
        if (!isRevealed) Toggle();
    }
}
