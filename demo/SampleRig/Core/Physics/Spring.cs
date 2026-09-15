using System;

namespace Anime25D.Sample.Core;

public sealed class Spring
{
    public double Position, Velocity, Displacement;
    public void Step(double target, double stiffness, double damping, double deltaSeconds, double integrationStepSeconds = 1.0 / 120)
    {
        int count = Math.Max(1, (int)Math.Ceiling(deltaSeconds / integrationStepSeconds));
        double stepSeconds = deltaSeconds / count;
        for (int i = 0; i < count; i++)
        {
            Velocity += (-stiffness * (Position - target) - damping * Velocity) * stepSeconds;
            Position += Velocity * stepSeconds;
        }
    }
}
public sealed class StrandState(double phase)
{
    public Spring Stiff { get; } = new();
    public Spring Soft { get; } = new();
    public double Phase { get; } = phase;
}
