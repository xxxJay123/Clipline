using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Effects;

namespace Clipline;

/// <summary>
/// Records a new shortcut: press it, and it is set. Escape cancels;
/// Backspace removes the shortcut altogether.
/// </summary>
internal sealed class ShortcutWindow : Window
{
    readonly Func<Hotkey?, bool> apply;
    readonly TextBlock keys = new();
    readonly TextBlock problem = new();
    readonly Palette palette = Palette.Current;
    bool done;

    /// <param name="apply">Tries the new shortcut; false when another app has it.</param>
    public ShortcutWindow(Hotkey? current, Func<Hotkey?, bool> apply, Action closed)
    {
        this.apply = apply;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Title = L("Change shortcut");
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);

        var dark = SystemTheme.AppsDark;
        var panel = new StackPanel { Width = 340 };
        panel.Children.Add(new TextBlock
        {
            Text = L("Press the new shortcut"),
            FontFamily = Palette.DisplayFont,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = palette.Ink,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        keys.Text = current?.Display ?? L("None");
        keys.FontFamily = Palette.DisplayFont;
        keys.FontSize = 22;
        keys.FontWeight = FontWeights.Medium;
        keys.Foreground = palette.Ink;
        keys.HorizontalAlignment = HorizontalAlignment.Center;
        panel.Children.Add(new Border
        {
            Margin = new Thickness(0, 16, 0, 14),
            Padding = new Thickness(18, 12, 18, 13),
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(dark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(14, 0, 0, 0)),
            BorderBrush = new SolidColorBrush(dark ? Color.FromArgb(30, 255, 255, 255) : Color.FromArgb(20, 0, 0, 0)),
            BorderThickness = new Thickness(1),
            Child = keys,
        });
        panel.Children.Add(new TextBlock
        {
            Text = L("Esc cancels. Backspace removes the shortcut."),
            FontFamily = Palette.Font,
            FontSize = 12.5,
            Foreground = palette.SecondaryInk,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        problem.FontFamily = Palette.Font;
        problem.FontSize = 12.5;
        problem.Foreground = new SolidColorBrush(dark ? Color.FromRgb(255, 140, 130) : Color.FromRgb(196, 43, 28));
        problem.HorizontalAlignment = HorizontalAlignment.Center;
        problem.Margin = new Thickness(0, 8, 0, 0);
        problem.Visibility = Visibility.Collapsed;
        panel.Children.Add(problem);

        Content = new Border
        {
            Margin = new Thickness(24),
            Padding = new Thickness(26, 22, 26, 22),
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(dark ? Color.FromRgb(40, 40, 44) : Color.FromRgb(250, 250, 251)),
            BorderBrush = new SolidColorBrush(dark ? Color.FromArgb(50, 255, 255, 255) : Color.FromArgb(28, 0, 0, 0)),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.3, BlurRadius = 36, ShadowDepth = 8, Direction = 270 },
            Child = panel,
        };

        PreviewKeyDown += OnKey;
        PreviewKeyUp += (_, _) => ShowHeld();
        Deactivated += (_, _) => Finish();
        Closed += (_, _) => closed();
        Loaded += (_, _) =>
        {
            // In the middle of the screen with the pointer.
            var screen = Screens.UnderPointer();
            var s = screen.Scale;
            Left = (screen.WorkArea.Left + screen.WorkArea.Width / 2.0) / s - ActualWidth / 2;
            Top = (screen.WorkArea.Top + screen.WorkArea.Height / 2.5) / s - ActualHeight / 2;
        };
    }

    static HotkeyModifiers Held()
    {
        var m = HotkeyModifiers.None;
        if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) m |= HotkeyModifiers.Control;
        if (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt)) m |= HotkeyModifiers.Alt;
        if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)) m |= HotkeyModifiers.Shift;
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) m |= HotkeyModifiers.Windows;
        return m;
    }

    void ShowHeld()
    {
        if (done) return;
        var m = Held();
        if (m == HotkeyModifiers.None) return;
        var preview = new Hotkey(m, 0).Display;
        keys.Text = preview.EndsWith("?", StringComparison.Ordinal) ? preview[..^1] + "…" : preview;
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Held();
        if (key == Key.Escape && modifiers == HotkeyModifiers.None)
        {
            Finish();
            return;
        }
        if (key == Key.Back && modifiers == HotkeyModifiers.None)
        {
            apply(null);
            Finish();
            return;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            ShowHeld();
            return;
        }
        var hotkey = new Hotkey(modifiers, KeyInterop.VirtualKeyFromKey(key));
        if (!hotkey.IsValid)
        {
            ShowHeld();
            return;
        }
        keys.Text = hotkey.Display;
        if (apply(hotkey))
        {
            Finish();
            return;
        }
        problem.Text = L("That shortcut is taken by another app.");
        problem.Visibility = Visibility.Visible;
    }

    void Finish()
    {
        if (done) return;
        done = true;
        Close();
    }
}
