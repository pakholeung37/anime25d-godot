using System;
using System.Collections.Generic;
using System.Linq;
namespace Anime25D.Sample.Core;
public sealed class Parameters
{
    public static readonly IReadOnlyList<ParameterSpec> Specs = SampleParameters.Specs;
    private readonly double[] values = new double[Specs.Count];
    public IReadOnlyList<ParameterSpec> Catalog { get; }
    public Parameters(IReadOnlyList<ParameterSpec>? catalog = null)
    {
        Catalog = catalog ?? Specs;
        Reset();
    }
    public double this[Parameter key]
    {
        get => values[(int)key];
        set
        {
            if (!double.IsFinite(value))
                throw new ArgumentException("Parameter must be finite.");
            values[(int)key] = value;
        }
    }
    public void Set(Parameter key, double value)
    {
        if (!double.IsFinite(value))
            throw new ArgumentException("Parameter must be finite.");
        var spec = Catalog[(int)key];
        this[key] = Math.Clamp(value, spec.Min, spec.Max);
    }
    public void Reset()
    {
        foreach (var spec in Catalog)
            values[(int)spec.Key] = spec.Default;
    }
    public void CopyFrom(Parameters other) => other.values.CopyTo(values, 0);
    public Dictionary<string, double> Snapshot() => Specs.ToDictionary(s => s.Key.ToString(), s => this[s.Key]);
}

public sealed class FrameParameters
{
    public Parameters Values { get; }
    public FrameParameters(IReadOnlyList<ParameterSpec>? catalog = null) => Values = new(catalog);
    public double this[Parameter key] { get => Values[key]; set => Values[key] = value; }
    public double Breath { get; set; }
    public double BreathHead { get; set; }
    public double IrisBounceX { get; set; } = 1;
    public double IrisBounceY { get; set; } = 1;
}

public sealed class AutomaticMotion
{
    public bool Idle { get; set; } = true;
    public bool Blink { get; set; } = true;
    public bool Random { get; set; } = true;
    public bool Talk { get; set; } = true;
    public bool Physics { get; set; } = true;
    public void DisableAll()
    {
        Idle = Blink = Random = Talk = Physics = false;
    }
}
