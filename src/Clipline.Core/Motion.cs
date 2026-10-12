namespace Clipline.Core;

/// <summary>
/// A damped spring with unit mass. Everything that moves on the line moves
/// with one: a spring keeps its speed when its target changes halfway, so
/// motions blend into each other instead of restarting.
/// </summary>
public struct Spring
{
    public double Value;
    public double Velocity;
    public double Target;
    public double Stiffness;
    public double Damping;

    /// <summary>
    /// The way SwiftUI describes a spring: <paramref name="response"/> is roughly
    /// how long one swing takes, <paramref name="dampingFraction"/> how quickly it
    /// settles (1 settles without overshooting).
    /// </summary>
    public static Spring Response(double response, double dampingFraction, double value = 0)
    {
        var w = 2 * Math.PI / response;
        return new Spring
        {
            Value = value,
            Target = value,
            Stiffness = w * w,
            Damping = 4 * Math.PI * dampingFraction / response,
        };
    }

    /// <summary>A spring given by its raw stiffness and damping.</summary>
    public static Spring Raw(double stiffness, double damping, double value = 0) =>
        new() { Value = value, Target = value, Stiffness = stiffness, Damping = damping };

    /// <summary>Changes how the spring moves from now on, keeping where it is and how fast.</summary>
    public void Retune(Spring shape)
    {
        Stiffness = shape.Stiffness;
        Damping = shape.Damping;
    }

    public readonly bool IsAtRest(double epsilon) =>
        Math.Abs(Value - Target) < epsilon && Math.Abs(Velocity) < epsilon * 10;

    public void Snap(double value)
    {
        Value = Target = value;
        Velocity = 0;
    }

    /// <summary>
    /// Advances the spring. Small fixed steps keep a stiff spring stable even
    /// when a frame comes late. Returns whether it is still moving.
    /// </summary>
    public bool Step(double dt, double epsilon)
    {
        if (IsAtRest(epsilon))
        {
            Value = Target;
            Velocity = 0;
            return false;
        }
        const double maxStep = 1.0 / 240;
        while (dt > 0)
        {
            var h = Math.Min(dt, maxStep);
            var acceleration = -Stiffness * (Value - Target) - Damping * Velocity;
            Velocity += acceleration * h;
            Value += Velocity * h;
            dt -= h;
        }
        return true;
    }
}

public enum Ease { Linear, In, Out, InOut, InCubic, OutCubic, InOutCubic }

public static class Easing
{
    public static double Apply(Ease ease, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return ease switch
        {
            Ease.In => t * t,
            Ease.Out => 1 - (1 - t) * (1 - t),
            Ease.InOut => t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2,
            Ease.InCubic => t * t * t,
            Ease.OutCubic => 1 - Math.Pow(1 - t, 3),
            Ease.InOutCubic => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2,
            _ => t,
        };
    }

    /// <summary>Smoothstep between <paramref name="a"/> and <paramref name="b"/>.</summary>
    public static double Smooth(double x, double a, double b)
    {
        var t = Math.Clamp((x - a) / (b - a), 0, 1);
        return t * t * (3 - 2 * t);
    }

    public static double Lerp(double a, double b, double t) => a + (b - a) * t;
}

/// <summary>A timed move from one value to another, for motions that should not overshoot.</summary>
public struct Tween
{
    public double From;
    public double To;
    public double Elapsed;
    public double Duration;
    public Ease Ease;

    public Tween(double from, double to, double duration, Ease ease)
    {
        From = from;
        To = to;
        Duration = duration;
        Ease = ease;
        Elapsed = 0;
    }

    public readonly bool Done => Elapsed >= Duration;
    public readonly double Value => Duration <= 0 ? To : Easing.Lerp(From, To, Easing.Apply(Ease, Elapsed / Duration));

    public void Step(double dt) => Elapsed = Math.Min(Duration, Elapsed + dt);
}
