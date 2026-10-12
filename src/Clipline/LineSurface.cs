using System.Windows.Controls;
using System.Windows.Media.Effects;
using Path = System.Windows.Shapes.Path;

namespace Clipline;

/// <summary>
/// Everything inside the strip at the top of the screen: the rope, the hint
/// on an empty line, and the photos. It runs the motion of all of them, and
/// only while something moves.
/// </summary>
internal sealed class LineSurface : Canvas
{
    readonly Line line;
    readonly ICardHost host;
    readonly Canvas stage = new();
    readonly TranslateTransform slide = new();
    readonly Canvas rope = new() { IsHitTestVisible = false };
    readonly Border hint;
    readonly Dictionary<Guid, CardView> cards = [];
    LineMetrics metrics;
    Palette palette;
    Spring reveal;
    Tween? tuck;
    Spring hintShown = Spring.Response(0.3, 1);
    bool running;
    TimeSpan lastFrame;
    double ropeWidth = -1;
    bool laidOut;
    bool revealed;

    public LineSurface(Line line, ICardHost host, LineMetrics metrics, Palette palette)
    {
        this.line = line;
        this.host = host;
        this.metrics = metrics;
        this.palette = palette;
        ClipToBounds = false;
        Background = Brushes.Transparent;
        stage.RenderTransform = slide;
        Children.Add(stage);
        stage.Children.Add(rope);
        hint = BuildHint();
        stage.Children.Add(hint);
        reveal = Spring.Response(0.42, 0.82, Hidden);
        slide.Y = Hidden;
        SizeChanged += (_, _) => Layout();
    }

    double Hidden => -(metrics.PanelHeight + 12);

    /// <summary>The slide finished taking the line up: the window can leave the screen.</summary>
    public event Action? Tucked;

    public bool Revealed => revealed;
    public bool IsMoving => running;

    public IEnumerable<CardView> Cards => cards.Values;
    public CardView? Card(Guid id) => cards.GetValueOrDefault(id);

    public void SetRevealed(bool on)
    {
        if (on == revealed) return;
        revealed = on;
        if (on)
        {
            // Out from under the top edge, the way an auto-hiding taskbar slides in.
            tuck = null;
            reveal.Target = 0;
            Sync();
        }
        else
        {
            tuck = new Tween(reveal.Value, Hidden, 0.22, Ease.In);
        }
        Run();
    }

    public void SetMetrics(LineMetrics next)
    {
        metrics = next;
        foreach (var card in cards.Values) card.SetMetrics(next);
        if (!revealed) reveal.Snap(Hidden);
        ropeWidth = -1;
        Layout();
    }

    public void ApplyPalette(Palette next)
    {
        palette = next;
        foreach (var card in cards.Values) card.ApplyPalette(next);
        var text = (TextBlock)hint.Child;
        hint.Background = palette.BadgeGlass;
        hint.BorderBrush = palette.GlassEdge;
        text.Foreground = palette.SecondaryInk;
    }

    /// <summary>Makes the views match what hangs on the line.</summary>
    public void Sync(bool settle = false)
    {
        foreach (var gone in cards.Keys.Where(id => line.Find(id) == null).ToList())
        {
            stage.Children.Remove(cards[gone]);
            cards.Remove(gone);
        }
        foreach (var item in line.Items)
        {
            if (cards.ContainsKey(item.Id)) continue;
            var view = new CardView(item, metrics, palette, host, line.DiscardRecycles(item.Id));
            cards[item.Id] = view;
            stage.Children.Add(view);
            Retarget();
            view.X.Snap(TargetX(item));
            if (settle) view.Settle();
            else view.Arrive();
        }
        Retarget();
        Run();
    }

    /// <summary>A thumbnail changed: the card takes the new shape.</summary>
    public void Refresh(Pegged item)
    {
        if (cards.TryGetValue(item.Id, out var view)) view.Relayout();
        Run();
    }

    public void RefreshCrosses()
    {
        foreach (var view in cards.Values) view.SetCrossRecycles(line.DiscardRecycles(view.Id));
    }

    public void Breeze()
    {
        if (!revealed) return;
        foreach (var view in cards.Values) view.Breeze();
        Run();
    }

    public void SetCopied(Guid id)
    {
        if (cards.TryGetValue(id, out var view)) view.SetCopied(line.CopiedId == id);
        Run();
    }

    public void SetHover(Guid? id)
    {
        var changed = false;
        foreach (var view in cards.Values)
        {
            var on = view.Id == id;
            if (view.Hovering != on)
            {
                view.SetHover(on);
                changed = true;
            }
        }
        if (changed) Run();
    }

    public void SetDragging(Guid id, bool on)
    {
        if (cards.TryGetValue(id, out var view)) view.SetDragging(on);
        Run();
    }

    public void Land(Guid id)
    {
        if (cards.TryGetValue(id, out var view)) view.Land();
        Run();
    }

    /// <summary>Where each photo should hang, in the order they were hung.</summary>
    public void Retarget()
    {
        var width = ActualWidth;
        foreach (var view in cards.Values) view.X.Target = TargetX(view.Item);
        foreach (var view in cards.Values) view.SetEdgeFade(line.KeepsMore ? LineMetrics.EdgeFade(view.X.Target, width) : 1);
        UpdateHint();
        Run();
    }

    double TargetX(Pegged item)
    {
        var index = line.Items.IndexOf(item);
        return metrics.X(index, line.Items.Count, ActualWidth, line.VisibleCount, line.Scroll, line.KeepsMore);
    }

    void UpdateHint()
    {
        hintShown.Target = line.LiveCount == 0 ? 1 : 0;
        hint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var width = ActualWidth;
        SetLeft(hint, (width - hint.DesiredSize.Width) / 2);
        SetTop(hint, LineMetrics.RopeY(width / 2, width) + 34 - hint.DesiredSize.Height / 2);
    }

    /// <summary>
    /// The glass cards as they are on screen right now, in this surface's
    /// coordinates. The window only catches the pointer over these.
    /// </summary>
    public IEnumerable<(Guid Id, Rect Rect)> HitRects()
    {
        foreach (var view in cards.Values)
        {
            if (view.Item.Falling || view.Opacity < 0.05) continue;
            var transform = view.Card.TransformToAncestor(this);
            yield return (view.Id, transform.TransformBounds(new Rect(0, 0, view.Card.Width, view.Card.Height)));
        }
    }

    /// <summary>
    /// Where a card will hang once the line is down, unrotated: the landing
    /// spot of a capture flying up, and the start of a fall.
    /// </summary>
    public Rect? CardRect(Guid id, bool final)
    {
        if (!cards.TryGetValue(id, out var view)) return null;
        var width = ActualWidth;
        var x = final ? view.X.Target : view.X.Value;
        var size = metrics.CardSize(view.Item.PixelWidth, view.Item.PixelHeight);
        var top = LineMetrics.RopeY(x, width) - LineMetrics.PinAbove + LineMetrics.CardOffsetBelowTop + (final ? 0 : slide.Y);
        return new Rect(x - size.Width / 2, top, size.Width, size.Height);
    }

    Border BuildHint()
    {
        var text = new TextBlock
        {
            Text = L("Take a screenshot with Win + Shift + S and it will hang here"),
            FontFamily = Palette.Font,
            FontSize = 12.5,
            FontWeight = FontWeights.Medium,
            Foreground = palette.SecondaryInk,
        };
        return new Border
        {
            Child = text,
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(13, 6, 13, 7),
            Background = palette.BadgeGlass,
            BorderBrush = palette.GlassEdge,
            BorderThickness = new Thickness(0.75),
            Opacity = 0,
            IsHitTestVisible = false,
            Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.16, BlurRadius = 12, ShadowDepth = 2, Direction = 270 },
        };
    }

    void Layout()
    {
        var width = ActualWidth;
        if (width <= 0) return;
        var first = !laidOut;
        laidOut = true;
        if (Math.Abs(width - ropeWidth) > 0.5)
        {
            ropeWidth = width;
            DrawRope(width);
        }
        Retarget();
        // Photos hung before the strip had a width start where they belong, not sliding in from the corner.
        if (first) foreach (var view in cards.Values) view.X.Snap(view.X.Target);
        foreach (var view in cards.Values) view.Apply(width);
    }

    /// <summary>
    /// A thin, neutral line: a mid grey core with a faint highlight and a soft
    /// shadow, so it reads on light and dark backgrounds alike. It fades out
    /// at both ends so it seems to come from beyond the screen.
    /// </summary>
    void DrawRope(double width)
    {
        rope.Children.Clear();
        var top = LineMetrics.RopeTop;
        var geometry = new PathGeometry([new PathFigure(new Point(-20, top),
            [new QuadraticBezierSegment(new Point(width / 2, top + 2 * LineMetrics.Sag(width)), new Point(width + 20, top), true)], false)]);
        geometry.Freeze();

        rope.Children.Add(new Path
        {
            Data = geometry,
            Stroke = new SolidColorBrush(Color.FromArgb(56, 0, 0, 0)),
            StrokeThickness = 1.6,
            RenderTransform = new TranslateTransform(0, 1.3),
            Effect = new BlurEffect { Radius = 2.4, RenderingBias = RenderingBias.Quality },
        });
        rope.Children.Add(new Path { Data = geometry, Stroke = new SolidColorBrush(Color.FromRgb(140, 140, 140)), StrokeThickness = 1.25 });
        rope.Children.Add(new Path
        {
            Data = geometry,
            Stroke = new SolidColorBrush(Color.FromArgb(115, 255, 255, 255)),
            StrokeThickness = 0.45,
            RenderTransform = new TranslateTransform(0, -0.35),
        });
        rope.Width = width;
        rope.Height = metrics.PanelHeight;
        rope.OpacityMask = new LinearGradientBrush(new GradientStopCollection
        {
            new(Colors.Transparent, 0),
            new(Colors.Black, 0.08),
            new(Colors.Black, 0.92),
            new(Colors.Transparent, 1),
        }, 0)
        { MappingMode = BrushMappingMode.RelativeToBoundingBox };
    }

    // The frame loop

    /// <summary>Starts stepping the motions on every frame, until they all come to rest.</summary>
    public void Run()
    {
        if (running) return;
        running = true;
        lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnFrame;
    }

    void OnFrame(object? sender, EventArgs e)
    {
        var now = e is RenderingEventArgs r ? r.RenderingTime : TimeSpan.Zero;
        var dt = lastFrame == TimeSpan.Zero ? 1.0 / 60 : (now - lastFrame).TotalSeconds;
        if (dt <= 0) return;
        lastFrame = now;
        dt = Math.Min(dt, 1.0 / 20);

        var moving = false;
        if (tuck is { } t)
        {
            t.Step(dt);
            reveal.Value = t.Value;
            reveal.Velocity = 0;
            tuck = t.Done ? null : t;
            if (t.Done)
            {
                reveal.Snap(Hidden);
                Tucked?.Invoke();
            }
            else moving = true;
        }
        else
        {
            moving |= reveal.Step(dt, 0.1);
        }
        slide.Y = reveal.Value;

        moving |= hintShown.Step(dt, 0.002);
        hint.Opacity = Math.Clamp(hintShown.Value, 0, 1);

        var width = ActualWidth;
        foreach (var view in cards.Values)
        {
            moving |= view.Step(dt);
            view.Apply(width);
        }

        if (!moving)
        {
            running = false;
            CompositionTarget.Rendering -= OnFrame;
        }
    }

    /// <summary>For a picture of the line: everything in place at once, nothing left to move.</summary>
    public void SettleNow()
    {
        reveal.Snap(revealed ? 0 : Hidden);
        tuck = null;
        slide.Y = reveal.Value;
        hintShown.Snap(hintShown.Target);
        hint.Opacity = hintShown.Value;
        var width = ActualWidth;
        foreach (var view in cards.Values)
        {
            for (var i = 0; i < 600 && view.Step(1.0 / 60); i++) { }
            view.Apply(width);
        }
    }
}
