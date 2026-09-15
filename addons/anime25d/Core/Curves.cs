using System;
using System.Collections.Generic;
using System.Linq;

namespace Anime25D.Core;

public enum Interpolation { Linear, Step, Smooth, CubicHermite }
/// <param name="Interpolation">Interpolation from this key to the next key.</param>
/// <param name="InTangent">Incoming slope in value units per second.</param>
/// <param name="OutTangent">Outgoing slope in value units per second.</param>
public readonly record struct Keyframe(double Time, double Value, Interpolation Interpolation = Interpolation.Linear,
    double InTangent = 0, double OutTangent = 0);

/// <summary>Immutable, continuous-time curve. Values outside its keys hold the nearest endpoint.</summary>
public sealed class MotionCurve
{
    public IReadOnlyList<Keyframe> Keys { get; }
    public double EndTime => Keys[Keys.Count - 1].Time;
    public MotionCurve(params Keyframe[] keys)
    {
        if (keys.Length == 0) throw new ArgumentException("A curve needs at least one key.");
        var copy = keys.ToArray();
        for (int i = 0; i < copy.Length; i++)
        {
            var k = copy[i];
            if (!double.IsFinite(k.Time) || k.Time < 0 || !double.IsFinite(k.Value) ||
                !double.IsFinite(k.InTangent) || !double.IsFinite(k.OutTangent) || !Enum.IsDefined(k.Interpolation) ||
                (i > 0 && k.Time <= copy[i - 1].Time))
                throw new ArgumentException("MotionCurve keys must have finite values and strictly increasing nonnegative times.");
        }
        Keys = Array.AsReadOnly(copy);
    }
    public double Sample(double time)
    {
        if (!double.IsFinite(time)) throw new ArgumentException("MotionCurve time must be finite.");
        if (time <= Keys[0].Time) return Keys[0].Value;
        if (time >= EndTime) return Keys[Keys.Count - 1].Value;
        int low = 0, high = Keys.Count - 1;
        while (high - low > 1)
        {
            int mid = (low + high) / 2;
            if (Keys[mid].Time <= time) low = mid; else high = mid;
        }
        var a = Keys[low]; var b = Keys[high];
        double duration = b.Time - a.Time, t = (time - a.Time) / duration;
        double value = a.Interpolation switch
        {
            Interpolation.Step => a.Value,
            Interpolation.Smooth => a.Value * (1 - t * t * (3 - 2 * t)) + b.Value * t * t * (3 - 2 * t),
            Interpolation.CubicHermite => (2 * t * t * t - 3 * t * t + 1) * a.Value +
                (t * t * t - 2 * t * t + t) * duration * a.OutTangent +
                (-2 * t * t * t + 3 * t * t) * b.Value + (t * t * t - t * t) * duration * b.InTangent,
            _ => a.Value * (1 - t) + b.Value * t
        };
        if (!double.IsFinite(value)) throw new InvalidOperationException("MotionCurve evaluation overflowed.");
        return value;
    }
    public static MotionCurve Constant(double value) => new(new Keyframe(0, value));
    public static MotionCurve Linear(params (double Time, double Value)[] keys) =>
        new(keys.Select(k => new Keyframe(k.Time, k.Value)).ToArray());
    public static MotionCurve Smooth(params (double Time, double Value)[] keys) =>
        new(keys.Select(k => new Keyframe(k.Time, k.Value, Interpolation.Smooth)).ToArray());
    public static MotionCurve Step(params (double Time, double Value)[] keys) =>
        new(keys.Select(k => new Keyframe(k.Time, k.Value, Interpolation.Step)).ToArray());
}
