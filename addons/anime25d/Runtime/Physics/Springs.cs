using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Anime25D.Runtime;

public sealed class SpringState
{
    public double Position { get; private set; }
    public double Velocity { get; private set; }
    public void Step(double target, double stiffness, double damping, double delta, double maximumStep = 1.0 / 120)
    {
        if (!double.IsFinite(target) || !double.IsFinite(stiffness) || stiffness < 0 || !double.IsFinite(damping) || damping < 0 || !double.IsFinite(delta) || delta < 0 || !double.IsFinite(maximumStep) || maximumStep <= 0) throw new ArgumentException("Invalid spring input.");
        double required = Math.Ceiling(delta / maximumStep);
        if (required > 10000) throw new InvalidOperationException("Spring step budget exceeded; time was not discarded.");
        int count = Math.Max(1, (int)required); double step = delta / count;
        for (int i = 0; i < count; i++) { Velocity += (-stiffness * (Position - target) - damping * Velocity) * step; Position += Velocity * step; }
    }
    public void Reset() { Position = Velocity = 0; }
}
public sealed record SpringBinding(double Stiffness, double Damping, double DisplacementScale = 1);
public sealed class SpringBank : ModelComponent
{
    private readonly string input, output;
    private readonly SpringBinding[] bindings;
    private readonly SpringState[] states;
    private readonly double maximumStep;
    public SpringBank(string input, string output, IEnumerable<SpringBinding> bindings, double maximumStep = 1.0 / 120)
    {
        this.input = input; this.output = output; this.bindings = bindings.ToArray(); this.maximumStep = maximumStep;
        if (!double.IsFinite(maximumStep) || maximumStep <= 0 || this.bindings.Any(b => !double.IsFinite(b.Stiffness) || b.Stiffness < 0 || !double.IsFinite(b.Damping) || b.Damping < 0 || !double.IsFinite(b.DisplacementScale))) throw new ArgumentException("Invalid spring configuration.");
        states = this.bindings.Select(_ => new SpringState()).ToArray();
    }
    private void Validate(ComponentContext c) { if (c.Channels[input].Length != states.Length || c.Output(output).Length != states.Length) throw new InvalidOperationException("Spring channel length mismatch."); }
    public override void AdvanceState(ComponentContext c, double delta)
    { Validate(c); for (int i = 0; i < states.Length; i++) states[i].Step(c.Channels[input][i], bindings[i].Stiffness, bindings[i].Damping, delta, maximumStep); }
    public override void EvaluateOutput(ComponentContext c)
    { Validate(c); var target = c.Channels[input]; var values = c.Output(output); for (int i = 0; i < states.Length; i++) values[i] = -(states[i].Position - target[i]) * bindings[i].DisplacementScale; }
    public override void Reset() { foreach (var state in states) state.Reset(); }
}
