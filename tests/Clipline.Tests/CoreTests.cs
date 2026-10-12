using Clipline.Core;
using Xunit;

namespace Clipline.Tests;

public class LayoutTests
{
    static readonly LineMetrics Medium = new(LineSize.Medium);

    [Fact]
    public void MediumKeepsTheOriginalProportions()
    {
        Assert.Equal(150, Medium.CardWidth);
        Assert.Equal(104, Medium.PhotoMaxHeight);
        Assert.Equal(174, Medium.Spacing);
        Assert.Equal(210, Medium.PanelHeight);
    }

    [Fact]
    public void SizesScaleThePhotoButNotTheClip()
    {
        var small = new LineMetrics(LineSize.Small);
        var large = new LineMetrics(LineSize.Large);
        Assert.True(small.CardWidth < Medium.CardWidth && Medium.CardWidth < large.CardWidth);
        Assert.Equal(26, LineMetrics.ClipHeight);
    }

    [Fact]
    public void RopeSagsMostInTheMiddleAndIsLevelAtTheEnds()
    {
        const double width = 1600;
        Assert.Equal(LineMetrics.RopeTop, LineMetrics.RopeY(0, width), 6);
        Assert.Equal(LineMetrics.RopeTop, LineMetrics.RopeY(width, width), 6);
        Assert.Equal(LineMetrics.RopeTop + LineMetrics.Sag(width), LineMetrics.RopeY(width / 2, width), 6);
        Assert.Equal(LineMetrics.RopeY(400, width), LineMetrics.RopeY(1200, width), 6);
    }

    [Fact]
    public void SagIsCapped() => Assert.Equal(30, LineMetrics.Sag(5000));

    [Fact]
    public void PhotosHangCentredAndSpacedEvenly()
    {
        const double width = 1600;
        var xs = Enumerable.Range(0, 3).Select(i => Medium.X(i, 3, width, 8, 0, false)).ToArray();
        Assert.Equal(width / 2, xs[1], 6);
        Assert.Equal(Medium.Spacing, xs[1] - xs[0], 6);
        Assert.Equal(Medium.Spacing, xs[2] - xs[1], 6);
    }

    [Fact]
    public void AFullLineSqueezesInsteadOfRunningOffTheScreen()
    {
        const double width = 1000;
        var count = 12;
        var first = Medium.X(0, count, width, 4, 0, false);
        var last = Medium.X(count - 1, count, width, 4, 0, false);
        Assert.True(first >= LineMetrics.EdgeRoom / 2 - 0.001);
        Assert.True(last <= width - LineMetrics.EdgeRoom / 2 + 0.001);
    }

    [Fact]
    public void ALongLineKeepsItsSpacingAndScrollsOlderPhotosIn()
    {
        const double width = 1600;
        var visible = Medium.VisibleCount(width);
        var count = visible + 5;
        var newest = Medium.X(count - 1, count, width, visible, 0, true);
        var older = Medium.X(count - 2, count, width, visible, 0, true);
        Assert.Equal(Medium.Spacing, newest - older, 6);
        var scrolled = Medium.X(count - 1, count, width, visible, Medium.Spacing, true);
        Assert.Equal(newest + Medium.Spacing, scrolled, 6);
    }

    [Theory]
    [InlineData(800, 3)]
    [InlineData(1707, 8)]
    [InlineData(5000, 12)]
    public void HowManyFit(double width, int expected) => Assert.Equal(expected, Medium.VisibleCount(width));

    [Fact]
    public void PhotoKeepsItsShapeInsideTheCard()
    {
        var wide = Medium.PhotoSize(1920, 1080);
        Assert.Equal(Medium.CardWidth - 14, wide.Width);
        Assert.Equal(Math.Round((Medium.CardWidth - 14) * 1080 / 1920), wide.Height);

        var tall = Medium.PhotoSize(1080, 2400);
        Assert.Equal(Medium.PhotoMaxHeight, tall.Height);
        Assert.True(tall.Width < tall.Height);

        var card = Medium.CardSize(1920, 1080);
        Assert.Equal(wide.Width + 2 * LineMetrics.FrameInset, card.Width);
    }

    [Fact]
    public void EdgeFadeHidesPhotosAtTheEdges()
    {
        Assert.Equal(0, LineMetrics.EdgeFade(0, 1000));
        Assert.Equal(1, LineMetrics.EdgeFade(500, 1000));
        Assert.Equal(0.5, LineMetrics.EdgeFade(45, 1000), 6);
    }
}

public class MotionTests
{
    [Fact]
    public void SpringSettlesOnItsTarget()
    {
        var spring = Spring.Response(0.42, 0.82, -222);
        spring.Target = 0;
        for (var i = 0; i < 240 && spring.Step(1.0 / 60, 0.1); i++) { }
        Assert.Equal(0, spring.Value, 6);
        Assert.Equal(0, spring.Velocity);
    }

    [Fact]
    public void ASwingOvershootsAndComesBack()
    {
        var swing = Spring.Raw(46, 2.6, 16);
        swing.Target = 0;
        var lowest = double.MaxValue;
        for (var i = 0; i < 600 && swing.Step(1.0 / 60, 0.01); i++) lowest = Math.Min(lowest, swing.Value);
        Assert.True(lowest < -5, "a lightly damped swing passes through rest to the other side");
        Assert.Equal(0, swing.Value, 6);
    }

    [Fact]
    public void CriticallyDampedSpringDoesNotOvershoot()
    {
        var spring = Spring.Response(0.3, 1, 0);
        spring.Target = 1;
        var highest = 0.0;
        for (var i = 0; i < 300 && spring.Step(1.0 / 120, 0.0005); i++) highest = Math.Max(highest, spring.Value);
        Assert.True(highest <= 1.001);
    }

    [Fact]
    public void ALateFrameStaysStable()
    {
        var spring = Spring.Response(0.3, 0.6, 0);
        spring.Target = 100;
        spring.Step(0.5, 0.01);
        Assert.InRange(spring.Value, 0, 200);
    }

    [Fact]
    public void TweenEndsExactlyOnItsTarget()
    {
        var tween = new Tween(0, 10, 0.3, Ease.Out);
        for (var i = 0; i < 30; i++) tween.Step(1.0 / 60);
        Assert.True(tween.Done);
        Assert.Equal(10, tween.Value);
    }

    [Theory]
    [InlineData(Ease.In)]
    [InlineData(Ease.Out)]
    [InlineData(Ease.InOut)]
    [InlineData(Ease.InCubic)]
    [InlineData(Ease.OutCubic)]
    [InlineData(Ease.InOutCubic)]
    public void EasingsStartAtZeroAndEndAtOne(Ease ease)
    {
        Assert.Equal(0, Easing.Apply(ease, 0), 9);
        Assert.Equal(1, Easing.Apply(ease, 1), 9);
    }
}

public class CaptureTests
{
    static readonly DateTime Now = new(2026, 10, 12, 20, 0, 0, DateTimeKind.Utc);
    static readonly PixelRect Main = new(0, 0, 2560, 1440);
    static readonly PixelRect Side = new(-1920, -119, 1920, 1080);

    [Fact]
    public void ASnipFliesFromTheRectangleJustDragged()
    {
        var drag = new Drag(new PixelRect(300, 200, 801, 449), Now.AddSeconds(-1.5));
        var found = CaptureMatch.Find(800, 450, Now, [drag], [Main, Side], new PixelPoint(900, 700));
        Assert.Equal(new PixelRect(300, 200, 800, 450), found);
    }

    [Fact]
    public void AnOldDragDoesNotCount()
    {
        var drag = new Drag(new PixelRect(300, 200, 800, 450), Now - CaptureMatch.DragWindow - TimeSpan.FromSeconds(1));
        Assert.Null(CaptureMatch.Find(800, 450, Now, [drag], [Main, Side], new PixelPoint(0, 0)));
    }

    [Fact]
    public void TheNewestMatchingDragWins()
    {
        var older = new Drag(new PixelRect(0, 0, 800, 450), Now.AddSeconds(-5));
        var newer = new Drag(new PixelRect(100, 100, 800, 450), Now.AddSeconds(-1));
        Assert.Equal(100, CaptureMatch.Find(800, 450, Now, [older, newer], [Main], default)!.Value.Left);
    }

    [Fact]
    public void AFullScreenCaptureFliesFromItsScreen()
    {
        Assert.Equal(Side, CaptureMatch.Find(1920, 1080, Now, [], [Main, Side], new PixelPoint(0, 0)));
        Assert.Equal(Main, CaptureMatch.Find(2560, 1440, Now, [], [Main, Side], new PixelPoint(0, 0)));
    }

    [Fact]
    public void TwoScreensOfTheSameSizePreferThePointer()
    {
        var left = new PixelRect(-1920, 0, 1920, 1080);
        var right = new PixelRect(0, 0, 1920, 1080);
        Assert.Equal(right, CaptureMatch.Find(1920, 1080, Now, [], [left, right], new PixelPoint(50, 50)));
    }

    [Fact]
    public void AnythingElseJustDropsIn() =>
        Assert.Null(CaptureMatch.Find(640, 480, Now, [], [Main, Side], default));
}

public class FileTests
{
    [Theory]
    [InlineData(@"C:\Users\a\AppData\Local\Clipline\Inbox\Clipboard 1.png", true)]
    [InlineData(@"C:\Users\a\AppData\Local\Clipline\Inbox\sub\x.png", false)]
    [InlineData(@"C:\Users\a\AppData\Local\Clipline\Inbox2\x.png", false)]
    [InlineData(@"C:\Users\a\Pictures\Screenshots\x.png", false)]
    public void OnlyFilesDirectlyInTheInboxAreOwned(string path, bool owned) =>
        Assert.Equal(owned, Inbox.Owns(@"C:\Users\a\AppData\Local\Clipline\Inbox", path));

    [Fact]
    public void UniqueNamesNeverOverwrite()
    {
        var folder = Directory.CreateTempSubdirectory("clipline-test").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "a.png"), "");
            File.WriteAllText(Path.Combine(folder, "a 2.png"), "");
            Assert.Equal(Path.Combine(folder, "a 3.png"), Inbox.UniquePath(folder, "a.png"));
            Assert.Equal(Path.Combine(folder, "b.png"), Inbox.UniquePath(folder, "b.png"));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Theory]
    [InlineData("Screenshot 2026-10-12 201530.png", true)]
    [InlineData("clip.JPG", true)]
    [InlineData("Recording 2026-10-12.mp4", true)]
    [InlineData("notes.txt", false)]
    [InlineData("download.png.crdownload", false)]
    public void SupportedMedia(string name, bool supported) =>
        Assert.Equal(supported, MediaKind.IsSupported(name) && !MediaKind.IsTemporary(name));

    [Fact]
    public void ClipboardNamesLookLikeWindowsScreenshots() =>
        Assert.Equal("Clipboard 2026-10-12 at 20.15.30.png", Inbox.ClipboardName(new DateTime(2026, 10, 12, 20, 15, 30)));
}

public class ClipboardPolicyTests
{
    [Fact]
    public void APlainPictureHangs() =>
        Assert.True(ClipboardPolicy.IsPicture(["PNG", "DeviceIndependentBitmap", "Bitmap"], null));

    [Fact]
    public void TextThatComesWithAPictureDoesNot() =>
        Assert.False(ClipboardPolicy.IsPicture(["UnicodeText", "Bitmap", "HTML Format"], null));

    [Fact]
    public void AFileFromExplorerDoesNot() =>
        Assert.False(ClipboardPolicy.IsPicture(["FileDrop", "Shell IDList Array", "Bitmap"], null));

    [Fact]
    public void PrivateCopiesDoNot()
    {
        Assert.False(ClipboardPolicy.IsPicture(["PNG", "ExcludeClipboardContentFromMonitorProcessing"], null));
        Assert.False(ClipboardPolicy.IsPicture(["PNG", "CanIncludeInClipboardHistory"], 0));
    }

    [Fact]
    public void CliplinesOwnCopiesDoNot() =>
        Assert.False(ClipboardPolicy.IsPicture(["PNG", ClipboardPolicy.SelfFormat], null));

    [Fact]
    public void NothingThatIsNotAPicture() =>
        Assert.False(ClipboardPolicy.IsPicture(["Locale"], null));

    [Fact]
    public void HugeImagesAreRefused()
    {
        Assert.True(ClipboardPolicy.IsSaneSize(3840, 2160));
        Assert.False(ClipboardPolicy.IsSaneSize(20000, 20000));
    }
}

public class SettingsTests
{
    [Fact]
    public void DefaultsMatchTheMac()
    {
        var s = new Settings();
        Assert.Equal(LineSize.Medium, s.Size);
        Assert.True(s.RevealAtTopEdge);
        Assert.True(s.PeekAtNew);
        Assert.False(s.OverFullScreen);
        Assert.False(s.HangCopiedImages);
        Assert.False(s.RecycleDiscarded);
        Assert.Equal(Hotkey.Default, s.Shortcut);
        Assert.Null(s.Keep);
    }

    [Fact]
    public void SettingsSurviveARoundTrip()
    {
        var folder = Directory.CreateTempSubdirectory("clipline-test").FullName;
        try
        {
            var path = Path.Combine(folder, "settings.json");
            JsonStore.Save(path, new Settings { Size = LineSize.Large, KeepOnLine = 50, Shortcut = null, ExtraFolders = [@"D:\Shots"] });
            var back = JsonStore.Load<Settings>(path);
            Assert.Equal(LineSize.Large, back.Size);
            Assert.Equal(50, back.Keep);
            Assert.Null(back.Shortcut);
            Assert.Equal([@"D:\Shots"], back.ExtraFolders);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void ADamagedFileStartsFromTheDefaults()
    {
        var folder = Directory.CreateTempSubdirectory("clipline-test").FullName;
        try
        {
            var path = Path.Combine(folder, "settings.json");
            File.WriteAllText(path, "{ not json");
            Assert.Equal(LineSize.Medium, JsonStore.Load<Settings>(path).Size);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void ShortcutsReadLikeWindowsWritesThem()
    {
        Assert.Equal("Ctrl + Alt + T", Hotkey.Default.Display);
        Assert.Equal("Win + Shift + F9", new Hotkey(HotkeyModifiers.Windows | HotkeyModifiers.Shift, 0x78).Display);
    }

    [Fact]
    public void AShortcutNeedsARealModifier()
    {
        Assert.False(new Hotkey(HotkeyModifiers.None, 'T').IsValid);
        Assert.False(new Hotkey(HotkeyModifiers.Shift, 'T').IsValid);
        Assert.True(new Hotkey(HotkeyModifiers.Control | HotkeyModifiers.Shift, 'T').IsValid);
        Assert.False(new Hotkey(HotkeyModifiers.Control, 0x10).IsValid);
    }
}

public class StringTests
{
    [Fact]
    public void EveryLanguageHasEveryKey()
    {
        foreach (var (language, table) in Strings.Tables)
            foreach (var key in Strings.Keys)
                Assert.True(table.ContainsKey(key), $"{language} is missing \"{key}\"");
    }

    [Fact]
    public void FormatsKeepTheirPlaceholders()
    {
        foreach (var (language, table) in Strings.Tables)
            foreach (var (key, text) in table)
                for (var i = 0; i < 3; i++)
                    Assert.True(key.Contains($"{{{i}}}") == text.Contains($"{{{i}}}"), $"{language}: \"{key}\"");
    }

    [Theory]
    [InlineData("zh-HK", "zh-Hant")]
    [InlineData("zh-TW", "zh-Hant")]
    [InlineData("zh-CN", "zh-Hans")]
    [InlineData("en-US", "en")]
    [InlineData("fr-FR", "en")]
    public void PicksTheLanguage(string culture, string expected) =>
        Assert.Equal(expected, Strings.Pick(new System.Globalization.CultureInfo(culture)));
}
