using System;
using System.Collections.Generic;
using System.Linq;

namespace Anime25D.Core;

// Enum spelling intentionally matches the original public parameter names.
public enum Parameter
{
    angleX, angleY, angleZ, eyeOpenL, eyeOpenR, eyeX, eyeY, brow,
    mouthOpen, mouthForm, mouthCY, body, physAmp, soft,
    browAngL, browAngR, browAngSym, bangL, bangC, bangR,
    armY, armPos, bust, bustY, irisScale, mouthEase, eyeEase,
    fhAmp, fhSoft, eyeCY, eyeCAng, mouthCAng, eyeScaleL, eyeScaleR, mouthScale
}
public readonly record struct ParameterSpec(Parameter Key, double Default, double Min, double Max);

public sealed class Parameters
{
    public static readonly IReadOnlyList<ParameterSpec> Specs = Array.AsReadOnly(new ParameterSpec[] {
        new(Parameter.angleX,0,-1,1), new(Parameter.angleY,0,-1,1), new(Parameter.angleZ,0,-1,1),
        new(Parameter.eyeOpenL,1,0,1), new(Parameter.eyeOpenR,1,0,1), new(Parameter.eyeX,0,-1,1), new(Parameter.eyeY,0,-1,1), new(Parameter.brow,0,-1,1),
        new(Parameter.mouthOpen,0,0,1), new(Parameter.mouthForm,0,-1,1), new(Parameter.mouthCY,0,-1,1), new(Parameter.body,0,-1,1),
        new(Parameter.physAmp,2,0,3), new(Parameter.soft,2,0,3),
        new(Parameter.browAngL,0,-1,1), new(Parameter.browAngR,0,-1,1), new(Parameter.browAngSym,0,-1,1),
        new(Parameter.bangL,0,-1,1), new(Parameter.bangC,0,-1,1), new(Parameter.bangR,0,-1,1),
        new(Parameter.armY,0,-1,1), new(Parameter.armPos,0,-1,1), new(Parameter.bust,2.5,0,4), new(Parameter.bustY,1,-3,3),
        new(Parameter.irisScale,1,0.5,1.3), new(Parameter.mouthEase,0.72,0,1), new(Parameter.eyeEase,0.3,0,1),
        new(Parameter.fhAmp,2,0,3), new(Parameter.fhSoft,0.4,0,2), new(Parameter.eyeCY,0,-1,1), new(Parameter.eyeCAng,0,-1,1),
        new(Parameter.mouthCAng,0,-1,1), new(Parameter.eyeScaleL,1,0.5,1.5), new(Parameter.eyeScaleR,1,0.5,1.5), new(Parameter.mouthScale,1,0.5,1.5)
    });
    private readonly double[] values = new double[Specs.Count];
    public Parameters() => Reset();
    public double this[Parameter key]
    {
        get => values[(int)key];
        set { if (!double.IsFinite(value)) throw new ArgumentException("Parameter must be finite."); values[(int)key] = value; }
    }
    public void Set(Parameter key, double value)
    {
        if (!double.IsFinite(value)) throw new ArgumentException("Parameter must be finite.");
        var spec = Specs[(int)key]; this[key] = Math.Clamp(value, spec.Min, spec.Max);
    }
    public void Reset() { foreach (var spec in Specs) values[(int)spec.Key] = spec.Default; }
    public void CopyFrom(Parameters other) => other.values.CopyTo(values, 0);
    public Dictionary<string, double> Snapshot() => Specs.ToDictionary(s => s.Key.ToString(), s => this[s.Key]);
}

public sealed class FrameParameters
{
    public Parameters Values { get; } = new();
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
    public bool Mouse { get; set; }
    public bool Physics { get; set; } = true;
    public void DisableAll() { Idle = Blink = Random = Talk = Mouse = Physics = false; }
}
