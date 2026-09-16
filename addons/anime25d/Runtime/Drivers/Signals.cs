using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Anime25D.Runtime;

public static class SignalMap
{
    public static double Linear(double value, double scale, double offset = 0) => value * scale + offset;
    public static double SmoothStep(double value) { value = Math.Clamp(value, 0, 1); return value * value * (3 - 2 * value); }
    public static double Range(double value, double minimum, double maximum) => maximum > minimum ? Math.Clamp((value - minimum) / (maximum - minimum), 0, 1) : throw new ArgumentException("Empty mapping range.");
}
public sealed record SineBinding(string Parameter, double Amplitude, double AngularFrequency, double Phase = 0, double Offset = 0, BlendMode Blend = BlendMode.Add);
public sealed class SineDriver : ModelComponent
{
    private readonly (int Index, SineBinding Binding)[] bindings;
    public SineDriver(ModelDefinition model, params SineBinding[] bindings)
    {
        this.bindings = bindings.Select(b => (model.Animation.Parameters.IndexOf(b.Parameter), b)).ToArray();
        foreach (var (_, b) in this.bindings) if (!double.IsFinite(b.Amplitude) || !double.IsFinite(b.AngularFrequency) || !double.IsFinite(b.Phase) || !double.IsFinite(b.Offset) || !Enum.IsDefined(b.Blend)) throw new ArgumentException("Invalid sine.");
    }
    public override void EvaluateOutput(ComponentContext c)
    {
        foreach (var (i, b) in bindings)
        {
            double v = b.Offset + b.Amplitude * Math.Sin(c.Time * b.AngularFrequency + b.Phase);
            c.Pose[i] = b.Blend switch { BlendMode.Add => c.Pose[i] + v, BlendMode.Multiply => c.Pose[i] * v, _ => v };
        }
    }
}
public sealed class SmoothDriver : ModelComponent
{
    private readonly int[] indices;
    private readonly double[] current, defaults;
    private readonly double rate;
    public SmoothDriver(ModelDefinition model, IEnumerable<string> parameters, double rate)
    {
        if (!double.IsFinite(rate) || rate < 0) throw new ArgumentException("Invalid smoothing rate.");
        this.rate = rate; indices = parameters.Select(model.Animation.Parameters.IndexOf).ToArray();
        defaults = indices.Select(i => model.Animation.Parameters.Definitions[i].Default).ToArray(); current = defaults.ToArray();
    }
    public void SetImmediate(string parameter, ParameterLayout layout, double value)
    {
        int at = Array.IndexOf(indices, layout.IndexOf(parameter));
        if (!double.IsFinite(value)) throw new ArgumentException("Nonfinite parameter."); if (at >= 0) current[at] = value;
    }
    public override void AdvanceState(ComponentContext c, double delta)
    { double w = 1 - Math.Exp(-delta * rate); for (int j = 0; j < indices.Length; j++) current[j] += (c.Pose[indices[j]] - current[j]) * w; }
    public override void EvaluateOutput(ComponentContext c) { for (int j = 0; j < indices.Length; j++) c.Pose[indices[j]] = current[j]; }
    public override void Reset() => defaults.CopyTo(current, 0);
}
/// <summary>Reusable attack/hold/release envelope; clock advancement is independent of evaluation.</summary>
public sealed class EnvelopeDriver : ModelComponent
{
    private readonly int parameter;
    private readonly double attack, hold, release, interval;
    private double age;
    public EnvelopeDriver(ModelDefinition model, string parameter, double attack, double hold, double release, double interval)
    {
        if (new[] {attack, hold, release, interval}.Any(v => !double.IsFinite(v) || v < 0) || attack + hold + release + interval <= 0) throw new ArgumentException("Invalid envelope.");
        this.parameter = model.Animation.Parameters.IndexOf(parameter); this.attack = attack; this.hold = hold; this.release = release; this.interval = interval;
    }
    public override void AdvanceState(ComponentContext c, double delta) => age = (age + delta) % (attack + hold + release + interval);
    public override void EvaluateOutput(ComponentContext c) => c.Pose[parameter] = age < attack ? age / attack : age < attack + hold ? 1 : age < attack + hold + release ? 1 - (age - attack - hold) / release : 0;
    public override void Reset() => age = 0;
}
public sealed class ParameterMapDriver : ModelComponent
{
    private readonly int source, target;
    private readonly Func<double, double> map;
    private readonly BlendMode blend;
    public ParameterMapDriver(ModelDefinition model, string source, string target, Func<double, double> map, BlendMode blend = BlendMode.Override)
    {
        this.source = model.Animation.Parameters.IndexOf(source); this.target = model.Animation.Parameters.IndexOf(target);
        this.map = map ?? throw new ArgumentNullException(nameof(map)); this.blend = blend;
        if (!Enum.IsDefined(blend)) throw new ArgumentException("Invalid blend.");
    }
    public override void EvaluateOutput(ComponentContext c)
    { double value = map(c.Pose[source]); c.Pose[target] = blend switch { BlendMode.Add => c.Pose[target] + value, BlendMode.Multiply => c.Pose[target] * value, _ => value }; }
}
