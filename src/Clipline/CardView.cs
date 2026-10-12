using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace Clipline;

/// <summary>What a photo asks of the line when it is clicked, held, dragged or opened.</summary>
internal interface ICardHost
{
    void CardClicked(CardView card);
    void CardDoubleClicked(CardView card);
    void CardHeld(CardView card);
    void CardPreview(CardView card);
    void CardCross(CardView card);
    void CardMenu(CardView card);
    void CardDrag(CardView card);
    void CardPressChanged(CardView card, bool pressed);
}

/// <summary>
/// One photo with its clip. All the charm lives here: it drops onto the line,
/// swings, sways with the breeze and leans in when you reach for it.
/// </summary>
internal sealed class CardView : Canvas
{
    const double CrossHitSize = 26;
    const double HoverScale = 1.035;
    const double PressScale = 0.95;
    /// <summary>How long you hold before the editor opens: not a slow click, but deliberate.</summary>
    static readonly TimeSpan HoldDuration = TimeSpan.FromMilliseconds(450);

    readonly ICardHost host;
    LineMetrics metrics;
    Palette palette;

    readonly Canvas card = new();
    readonly Border glass = new() { CornerRadius = new CornerRadius(LineMetrics.FrameRadius), BorderThickness = new Thickness(0.75) };
    readonly System.Windows.Controls.Image photo = new() { Stretch = Stretch.Fill };
    readonly Border photoStroke = new() { BorderThickness = new Thickness(0.5), IsHitTestVisible = false };
    readonly Border hairline = new() { BorderThickness = new Thickness(0.5), IsHitTestVisible = false };
    readonly FrameworkElement clip = Parts.Clothespin();
    Border cross = null!;
    Border? play;
    Border copied = null!;
    readonly TranslateTransform copiedOffset = new();
    readonly ScaleTransform crossScale = new();
    readonly DropShadowEffect shadow = new() { Color = Colors.Black, Direction = 270, RenderingBias = RenderingBias.Performance };
    readonly ScaleTransform cardScale = new();
    readonly RotateTransform rotate = new();
    readonly TranslateTransform translate = new();
    bool crossRecycles;

    // Motion. Each is a spring or a timed move, stepped by the line every frame.
    public Spring X = Spring.Response(0.55, 0.78);
    Spring drop = Spring.Response(0.42, 0.72);
    Spring swing = Spring.Raw(46, 2.6);
    Tween? swingTween;
    double nudgeDelay = -1;
    double nudgeDegrees;
    Spring scale = Spring.Response(0.3, 0.6, 1);
    Tween? pressTween;
    Spring hover = Spring.Response(0.2, 1);
    Spring copiedShown = Spring.Response(0.22, 1);
    Spring landed = Spring.Response(0.16, 1, 1);
    Spring dragFade = Spring.Response(0.16, 1, 1);
    double edge = 1;

    // Gestures
    readonly DispatcherTimer holdTimer;
    Point downPoint;
    bool pressing;
    bool longPressed;
    bool dragStarted;

    public CardView(Pegged item, LineMetrics metrics, Palette palette, ICardHost host, bool crossRecycles)
    {
        Item = item;
        this.metrics = metrics;
        this.palette = palette;
        this.host = host;
        this.crossRecycles = crossRecycles;

        photo.SetValue(RenderOptions.BitmapScalingModeProperty, BitmapScalingMode.HighQuality);
        card.Children.Add(glass);
        card.Children.Add(photo);
        card.Children.Add(photoStroke);
        card.Children.Add(hairline);
        card.Effect = shadow;
        card.RenderTransform = cardScale;
        card.Background = Brushes.Transparent;
        Children.Add(card);
        Children.Add(clip);
        BuildBadges();

        var group = new TransformGroup();
        group.Children.Add(rotate);
        group.Children.Add(translate);
        RenderTransform = group;

        holdTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = HoldDuration };
        holdTimer.Tick += (_, _) => Held();
        card.MouseLeftButtonDown += OnLeftDown;
        card.MouseMove += OnMove;
        card.MouseLeftButtonUp += OnLeftUp;
        card.MouseDown += OnOtherDown;
        card.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            host.CardMenu(this);
        };
        card.LostMouseCapture += (_, _) => EndPress();

        ApplyPalette(palette);
        Relayout();
    }

    public Pegged Item { get; }
    public Guid Id => Item.Id;
    public bool Hovering { get; private set; }

    /// <summary>The glass card on the line, unrotated, in the view's own coordinates.</summary>
    public Rect CardBounds => new(Canvas.GetLeft(card), Canvas.GetTop(card), card.Width, card.Height);
    public FrameworkElement Card => card;

    void BuildBadges()
    {
        cross = Parts.Badge(20, crossRecycles ? Parts.Cross(palette.Ink) : Parts.Hide(palette.Ink), palette);
        cross.RenderTransform = crossScale;
        cross.RenderTransformOrigin = new Point(0.5, 0.5);
        cross.Opacity = 0;
        card.Children.Add(cross);
        Canvas.SetLeft(cross, 3);
        Canvas.SetTop(cross, 3);

        // A recording says so with a play button over its first frame.
        if (Item.IsRecording)
        {
            play = Parts.Badge(28, Parts.Play(palette.Ink), palette);
            card.Children.Add(play);
        }

        var label = new StackPanel { Orientation = Orientation.Horizontal };
        label.Children.Add(new Border { Width = 12, Height = 12, Margin = new Thickness(0, 0, 5, 0), Child = Parts.Check(palette.Ink) });
        label.Children.Add(new TextBlock
        {
            Text = L("Copied"),
            FontFamily = Palette.Font,
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = palette.Ink,
            VerticalAlignment = VerticalAlignment.Center,
        });
        copied = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 4, 11, 5),
            Background = palette.BadgeGlass,
            BorderBrush = palette.GlassEdge,
            BorderThickness = new Thickness(0.75),
            Child = label,
            Opacity = 0,
            IsHitTestVisible = false,
            RenderTransform = copiedOffset,
            Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.2, BlurRadius = 10, ShadowDepth = 2, Direction = 270 },
        };
        card.Children.Add(copied);
    }

    public void ApplyPalette(Palette next)
    {
        palette = next;
        glass.Background = palette.Glass;
        glass.BorderBrush = palette.GlassEdge;
        hairline.BorderBrush = palette.Hairline;
        photoStroke.BorderBrush = palette.PhotoStroke;
        card.Children.Remove(cross);
        if (play != null) card.Children.Remove(play);
        card.Children.Remove(copied);
        play = null;
        BuildBadges();
        Relayout();
    }

    /// <summary>The cross shows what it will do: an × when the file goes to the Recycle Bin, an eye when it only leaves the line.</summary>
    public void SetCrossRecycles(bool recycles)
    {
        if (recycles == crossRecycles) return;
        crossRecycles = recycles;
        ApplyPalette(palette);
    }

    public void SetMetrics(LineMetrics next)
    {
        metrics = next;
        Relayout();
    }

    /// <summary>Lays out the clip and the card for the photo's shape. Called again when the photo changes.</summary>
    public void Relayout()
    {
        var p = metrics.PhotoSize(Item.PixelWidth, Item.PixelHeight);
        var inset = LineMetrics.FrameInset;
        double w = p.Width + inset * 2, h = p.Height + inset * 2;
        var innerRadius = LineMetrics.FrameRadius - inset;

        Width = metrics.CardWidth;
        Height = LineMetrics.CardOffsetBelowTop + h + 32;
        SetLeft(clip, (metrics.CardWidth - LineMetrics.ClipWidth) / 2);
        SetTop(clip, 0);

        card.Width = w;
        card.Height = h;
        SetLeft(card, (metrics.CardWidth - w) / 2);
        SetTop(card, LineMetrics.CardOffsetBelowTop);
        cardScale.CenterX = w / 2;
        cardScale.CenterY = 0;

        glass.Width = w;
        glass.Height = h;

        photo.Source = Item.Thumb;
        photo.Width = p.Width;
        photo.Height = p.Height;
        photo.Clip = new RectangleGeometry(new Rect(0, 0, p.Width, p.Height), innerRadius, innerRadius);
        SetLeft(photo, inset);
        SetTop(photo, inset);

        photoStroke.Width = p.Width;
        photoStroke.Height = p.Height;
        photoStroke.CornerRadius = new CornerRadius(innerRadius);
        SetLeft(photoStroke, inset);
        SetTop(photoStroke, inset);

        hairline.Width = w + 1;
        hairline.Height = h + 1;
        hairline.CornerRadius = new CornerRadius(LineMetrics.FrameRadius + 0.5);
        SetLeft(hairline, -0.5);
        SetTop(hairline, -0.5);

        if (play != null)
        {
            SetLeft(play, w / 2 - 14);
            SetTop(play, h / 2 - 14);
        }

        copied.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        SetLeft(copied, (w - copied.DesiredSize.Width) / 2);
        SetTop(copied, h - copied.DesiredSize.Height + 16);
    }

    // Motion

    /// <summary>A photo dropping onto the line: it falls in from above and swings until it settles.</summary>
    public void Arrive()
    {
        if (Item.Flying)
        {
            // A capture that flies in is already in place; the flight did the arriving.
            drop.Snap(0);
            landed.Snap(0);
            return;
        }
        drop.Snap(-46);
        drop.Target = 0;
        swing = Spring.Raw(46, 2.6);
        swing.Value = 16;
        swing.Target = 0;
    }

    /// <summary>Already hanging when the app started: in place, no swing.</summary>
    public void Settle()
    {
        drop.Snap(0);
        swing.Snap(0);
    }

    /// <summary>Landing after the flight: no jump, just a small sway from rest.</summary>
    public void Land()
    {
        landed.Target = 1;
        Nudge(2.2);
    }

    /// <summary>A little wind: each photo moves on its own, a moment apart.</summary>
    public void Breeze()
    {
        nudgeDelay = Random.Shared.NextDouble() * 0.35;
        nudgeDegrees = 1.6 + Random.Shared.NextDouble() * 1.8;
    }

    public void Nudge(double degrees)
    {
        nudgeDelay = -1;
        swingTween = new Tween(swing.Value, degrees, 0.3, Ease.Out);
    }

    public void SetHover(bool on)
    {
        if (Hovering == on) return;
        Hovering = on;
        hover.Target = on ? 1 : 0;
        if (!pressing) scale.Target = on ? HoverScale : 1;
    }

    public void SetCopied(bool on)
    {
        copiedShown.Target = on ? 1 : 0;
        if (on) Nudge(3);
    }

    public void SetDragging(bool on) => dragFade.Target = on ? 0.45 : 1;

    public void SetEdgeFade(double value) => edge = value;

    /// <summary>Advances every motion. Returns whether anything is still moving.</summary>
    public bool Step(double dt)
    {
        var moving = false;
        moving |= X.Step(dt, 0.05);
        moving |= drop.Step(dt, 0.05);

        if (nudgeDelay >= 0)
        {
            nudgeDelay -= dt;
            moving = true;
            if (nudgeDelay < 0) Nudge(nudgeDegrees);
        }
        if (swingTween is { } tween)
        {
            tween.Step(dt);
            swing.Value = tween.Value;
            swingTween = tween;
            moving = true;
            if (tween.Done)
            {
                swingTween = null;
                swing = Spring.Raw(38, 2.4, tween.To);
                swing.Target = 0;
            }
        }
        else
        {
            moving |= swing.Step(dt, 0.01);
        }

        if (pressTween is { } press)
        {
            press.Step(dt);
            scale.Value = press.Value;
            scale.Velocity = 0;
            pressTween = press.Done ? null : press;
            moving |= !press.Done;
        }
        else
        {
            moving |= scale.Step(dt, 0.0005);
        }

        moving |= hover.Step(dt, 0.002);
        moving |= copiedShown.Step(dt, 0.002);
        moving |= landed.Step(dt, 0.002);
        moving |= dragFade.Step(dt, 0.002);
        return moving;
    }

    /// <summary>Puts the view where its motions say, hanging from the rope at its current position.</summary>
    public void Apply(double lineWidth)
    {
        var x = X.Value;
        var top = LineMetrics.RopeY(x, lineWidth) - LineMetrics.PinAbove + drop.Value;
        translate.X = x - metrics.CardWidth / 2;
        translate.Y = top;
        rotate.CenterX = metrics.CardWidth / 2;
        rotate.CenterY = 0;
        rotate.Angle = swing.Value + Item.Tilt;

        var arriving = drop.Value < 0 ? Math.Clamp(1 + drop.Value / 46, 0, 1) : 1;
        Opacity = Item.Falling ? 0 : arriving * edge * Math.Clamp(landed.Value, 0, 1);

        cardScale.ScaleX = cardScale.ScaleY = scale.Value;
        card.Opacity = Math.Clamp(dragFade.Value, 0, 1);

        var h = Math.Clamp(hover.Value, 0, 1);
        shadow.Opacity = 0.2 + 0.1 * h;
        shadow.BlurRadius = 18 + 10 * h;
        shadow.ShadowDepth = 5 + 3 * h;

        var showCross = DraggingNow ? 0 : h;
        cross.Opacity = showCross;
        crossScale.ScaleX = crossScale.ScaleY = 0.6 + 0.4 * showCross;

        var c = Math.Clamp(copiedShown.Value, 0, 1);
        copied.Opacity = c;
        copiedOffset.Y = -4 * (1 - c);
    }

    bool DraggingNow => dragFade.Target < 1;

    // Gestures

    void OnLeftDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var p = e.GetPosition(card);
        if (Hovering && p.X <= CrossHitSize && p.Y <= CrossHitSize)
        {
            host.CardCross(this);
            return;
        }
        if (e.ClickCount >= 2)
        {
            CancelGesture();
            host.CardDoubleClicked(this);
            return;
        }
        downPoint = e.GetPosition(this);
        pressing = true;
        longPressed = false;
        dragStarted = false;
        card.CaptureMouse();
        // Holding presses the photo in slowly, so a long press feels like it is building up to something.
        pressTween = new Tween(scale.Value, PressScale, 0.45, Ease.InOut);
        host.CardPressChanged(this, true);
        holdTimer.Start();
    }

    void OnMove(object sender, MouseEventArgs e)
    {
        if (!pressing || dragStarted || longPressed || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(this);
        if (Math.Abs(p.X - downPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(p.Y - downPoint.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        dragStarted = true;
        EndPress();
        card.ReleaseMouseCapture();
        host.CardDrag(this);
    }

    void OnLeftUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var click = pressing && !dragStarted && !longPressed;
        EndPress();
        card.ReleaseMouseCapture();
        if (click) host.CardClicked(this);
    }

    void OnOtherDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        e.Handled = true;
        host.CardPreview(this);
    }

    void Held()
    {
        holdTimer.Stop();
        if (!pressing || dragStarted) return;
        longPressed = true;
        EndPress();
        host.CardHeld(this);
    }

    void EndPress()
    {
        holdTimer.Stop();
        if (!pressing) return;
        pressing = false;
        pressTween = null;
        scale = Spring.Response(0.3, 0.6, scale.Value) with { Target = Hovering ? HoverScale : 1 };
        host.CardPressChanged(this, false);
    }

    void CancelGesture()
    {
        EndPress();
        longPressed = false;
        dragStarted = false;
    }
}
