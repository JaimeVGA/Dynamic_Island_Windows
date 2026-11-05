namespace DynamicIsland.Animation;

/// <summary>Integrador masa-resorte-amortiguador (semi-implicit Euler, paso fijo).</summary>
public sealed class SpringAnimator
{
    private const double Step = 1.0 / 240.0;

    public double Stiffness { get; set; } = 420;
    public double Damping { get; set; } = 28;
    public double Mass { get; set; } = 1;

    /// <summary>Umbral de reposo (en unidades del valor animado).</summary>
    public double Epsilon { get; set; } = 0.001;

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

    /// <summary>Coloca el valor al instante, sin animar.</summary>
    public void Snap(double value)
    {
        Value = value;
        Target = value;
        Velocity = 0;
        IsSettled = true;
    }

    /// <summary>Avanza la simulaciÃ³n dt segundos. Devuelve true mientras siga en movimiento.</summary>
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
        if (Math.Abs(Velocity) < Epsilon && Math.Abs(Value - Target) < Epsilon)
        {
            Value = Target;
            Velocity = 0;
            IsSettled = true;
        }
        return !IsSettled;
    }
}

