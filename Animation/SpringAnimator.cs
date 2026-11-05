namespace DynamicIsland.Animation;

/// <summary>Integrador masa-resorte-amortiguador (semi-implicit Euler, paso fijo).</summary>
public sealed class SpringAnimator
{
    private const double Step = 1.0 / 240.0;

    public double Stiffness { get; set; } = 420;
    public double Damping { get; set; } = 28;
    public double Mass { get; set; } = 1;

    public double Value { get; private set; }
    public double Velocity { get; private set; }
    public double Target { get; set; }
    public bool IsSettled { get; private set; } = true;

    public SpringAnimator(double initial)
    {
        Value = initial;
        Target = initial;
    }

    public void SetTarget(double target)
    {
        Target = target;
        IsSettled = false;
    }

    /// <summary>Avanza la simulación dt segundos. Devuelve true mientras siga en movimiento.</summary>
    public bool Update(double dt)
    {
        if (IsSettled) return false;
        dt = Math.Min(dt, 0.05);
        double remaining = dt;
        while (remaining > 0)
        {
            double h = Math.Min(Step, remaining);
            Velocity += (-Stiffness * (Value - Target) - Damping * Velocity) / Mass * h;
            Value += Velocity * h;
            remaining -= h;
        }
        if (Math.Abs(Velocity) < 0.05 && Math.Abs(Value - Target) < 0.05)
        {
            Value = Target;
            Velocity = 0;
            IsSettled = true;
        }
        return !IsSettled;
    }
}
