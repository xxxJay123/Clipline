using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace Clipline;

/// <summary>
/// A quick look at a photo, the rest of the line a key press away. It grows
/// out of the card it came from, over a dimmed screen, and goes as quickly.
/// </summary>
internal sealed class PreviewWindow : Window
{
    static PreviewWindow? current;

    readonly List<string> paths;
    readonly Screen screen;
    readonly Palette palette;
    readonly Grid root = new();
    readonly Border frame = new();
    readonly ContentControl content = new();
    readonly TextBlock title = new();
    readonly TextBlock counter = new();
    readonly ScaleTransform zoom = new(1, 1);
    readonly TranslateTransform shift = new();
    readonly nint hwnd;
    Rect? origin;
    int index;
    bool closing;

    PreviewWindow(List<string> paths, int index, Screen screen, Rect? origin)
    {
        this.paths = paths;
        this.index = index;
        this.screen = screen;
        this.origin = origin;
        palette = Palette.Current;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        Title = "Clipline";
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);

        root.Background = new SolidColorBrush(palette.Dim);
        root.Opacity = 0;
        frame.CornerRadius = new CornerRadius(14);
        frame.ClipToBounds = true;
        frame.Child = content;
        frame.Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.45, BlurRadius = 48, ShadowDepth = 12, Direction = 270 };
        frame.HorizontalAlignment = HorizontalAlignment.Center;
        frame.VerticalAlignment = VerticalAlignment.Center;
        var transforms = new TransformGroup();
        transforms.Children.Add(zoom);
        transforms.Children.Add(shift);
        frame.RenderTransform = transforms;
        frame.RenderTransformOrigin = new Point(0.5, 0.5);
        frame.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            if (e.ClickCount == 2) OpenCurrent();
        };

        title.FontFamily = Palette.DisplayFont;
        title.FontSize = 14;
        title.FontWeight = FontWeights.SemiBold;
        title.Foreground = Brushes.White;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        counter.FontFamily = Palette.Font;
        counter.FontSize = 12;
        counter.Foreground = new SolidColorBrush(Color.FromArgb(190, 255, 255, 255));

        var caption = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 28) };
        title.HorizontalAlignment = HorizontalAlignment.Center;
        counter.HorizontalAlignment = HorizontalAlignment.Center;
        caption.Children.Add(title);
        caption.Children.Add(counter);
        caption.Children.Add(new TextBlock
        {
            Text = L("Arrow keys to browse · Enter to open · Esc to close"),
            FontFamily = Palette.Font,
            FontSize = 11.5,
            Foreground = new SolidColorBrush(Color.FromArgb(140, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0),
        });

        root.Children.Add(frame);
        root.Children.Add(caption);
        root.MouseLeftButtonDown += (_, _) => Dismiss();
        Content = root;

        hwnd = new WindowInteropHelper(this).EnsureHandle();
        Native.SetExStyle(hwnd, Native.WS_EX_TOOLWINDOW);
        var b = screen.Bounds;
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, b.Left, b.Top, b.Width, b.Height, 0);
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, b.Left, b.Top, b.Width, b.Height, 0);

        PreviewKeyDown += OnKey;
        Deactivated += (_, _) => Dismiss();
        Loaded += (_, _) => ShowItem(animateFromOrigin: true);
    }

    /// <param name="origin">The card it opens from, in physical pixels, so it can grow out of it.</param>
    public static void Present(List<string> paths, int index, Screen screen, PixelRect? origin)
    {
        current?.Dismiss();
        if (paths.Count == 0) return;
        Rect? local = origin is { } o
            ? new Rect((o.Left - screen.Bounds.Left) / screen.Scale, (o.Top - screen.Bounds.Top) / screen.Scale, o.Width / screen.Scale, o.Height / screen.Scale)
            : null;
        current = new PreviewWindow(paths, Math.Clamp(index, 0, paths.Count - 1), screen, local);
        current.Show();
        current.Activate();
    }

    public static bool IsOpen => current != null;

    void ShowItem(bool animateFromOrigin)
    {
        var path = paths[index];
        title.Text = Path.GetFileName(path);
        counter.Text = paths.Count > 1 ? L("{0} of {1}", index + 1, paths.Count) : "";

        var area = new Size(ActualWidth * 0.86, (ActualHeight - 120) * 0.9);
        Size natural;
        if (content.Content is MediaElement old) old.Close();
        if (MediaKind.IsRecording(path))
        {
            var media = new MediaElement { Source = new Uri(path), LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Close, Stretch = Stretch.Uniform };
            media.MediaEnded += (_, _) =>
            {
                media.Position = TimeSpan.Zero;
                media.Play();
            };
            media.MediaOpened += (_, _) =>
            {
                if (media.NaturalVideoWidth > 0) Fit(new Size(media.NaturalVideoWidth / screen.Scale, media.NaturalVideoHeight / screen.Scale), area);
            };
            content.Content = media;
            media.Play();
            natural = new Size(1280, 720);
        }
        else
        {
            var image = Thumbnails.Load(path, (int)Math.Ceiling(Math.Max(area.Width, area.Height) * screen.Scale));
            content.Content = new System.Windows.Controls.Image { Source = image, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode((DependencyObject)content.Content, BitmapScalingMode.HighQuality);
            var pixels = Thumbnails.PixelSize(path);
            natural = pixels is { } p ? new Size(p.Width / screen.Scale, p.Height / screen.Scale) : new Size(image?.Width ?? 800, image?.Height ?? 600);
        }
        Fit(natural, area);

        frame.Background = new SolidColorBrush(Color.FromRgb(24, 24, 26));
        Fade(root, 1, 0.18);
        if (animateFromOrigin && origin is { } o)
        {
            // Grow out of the card on the line.
            UpdateLayout();
            var centre = new Point(ActualWidth / 2, ActualHeight / 2);
            var from = new Point(o.X + o.Width / 2, o.Y + o.Height / 2);
            var s = Math.Max(0.05, o.Width / Math.Max(1, frame.Width));
            Animate(zoom, ScaleTransform.ScaleXProperty, s, 1, 0.32);
            Animate(zoom, ScaleTransform.ScaleYProperty, s, 1, 0.32);
            Animate(shift, TranslateTransform.XProperty, from.X - centre.X, 0, 0.32);
            Animate(shift, TranslateTransform.YProperty, from.Y - centre.Y, 0, 0.32);
        }
        else
        {
            frame.Opacity = 0;
            Fade(frame, 1, 0.14);
        }
    }

    void Fit(Size natural, Size area)
    {
        var scale = Math.Min(1, Math.Min(area.Width / natural.Width, area.Height / natural.Height));
        frame.Width = Math.Max(160, natural.Width * scale);
        frame.Height = Math.Max(90, natural.Height * scale);
        frame.Margin = new Thickness(0, 0, 0, 72);
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
            case Key.Space:
                Dismiss();
                break;
            case Key.Left when index > 0:
                index--;
                ShowItem(false);
                break;
            case Key.Right when index < paths.Count - 1:
                index++;
                ShowItem(false);
                break;
            case Key.Enter:
                OpenCurrent();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    void OpenCurrent()
    {
        ShellOps.Open(paths[index]);
        Dismiss();
    }

    void Dismiss()
    {
        if (closing) return;
        closing = true;
        if (current == this) current = null;
        if (content.Content is MediaElement media) media.Stop();
        var fade = new DoubleAnimation(0, TimeSpan.FromSeconds(0.14)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        fade.Completed += (_, _) => Close();
        root.BeginAnimation(OpacityProperty, fade);
    }

    static void Fade(UIElement element, double to, double seconds) =>
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromSeconds(seconds)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

    static void Animate(Animatable target, DependencyProperty property, double from, double to, double seconds) =>
        target.BeginAnimation(property, new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
}
