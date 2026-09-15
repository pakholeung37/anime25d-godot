using System;
using System.Collections.Generic;
using System.Linq;

namespace Anime25D.Core;

// Stable indices are shared with the GPU pose buffer. Legacy names live only in the catalog.
public enum Parameter
{
    HeadYaw, HeadPitch, HeadRoll, LeftEyeOpenness, RightEyeOpenness, GazeHorizontal, GazeVertical, EyebrowHeight,
    MouthOpenness, MouthShape, ClosedMouthOffsetY, BodyRoll, HairSwayAmplitude, HairSoftness,
    LeftEyebrowAngle, RightEyebrowAngle, SymmetricEyebrowAngle, LeftFringeOffset, CenterFringeOffset, RightFringeOffset,
    ArmLift, ArmOffsetY, ChestBounceAmplitude, ChestBounceOffsetY, IrisScale, MouthTransition, EyeTransition,
    FringeSwayAmplitude, FringeSoftness, ClosedEyeOffsetY, ClosedEyeAngle, ClosedMouthAngle, LeftEyeScale, RightEyeScale, MouthScale
}
public readonly record struct ParameterSpec(Parameter Key, double Default, double Min, double Max)
{
    public string LegacyName => ParameterNames.Legacy[(int)Key];
}

public static class ParameterNames
{
    internal static readonly string[] Legacy = [
        "angleX", "angleY", "angleZ", "eyeOpenL", "eyeOpenR", "eyeX", "eyeY", "brow",
        "mouthOpen", "mouthForm", "mouthCY", "body", "physAmp", "soft", "browAngL", "browAngR",
        "browAngSym", "bangL", "bangC", "bangR", "armY", "armPos", "bust", "bustY", "irisScale",
        "mouthEase", "eyeEase", "fhAmp", "fhSoft", "eyeCY", "eyeCAng", "mouthCAng", "eyeScaleL", "eyeScaleR", "mouthScale"
    ];
    public static Parameter Parse(string name)
    {
        if (Enum.TryParse<Parameter>(name, out var key) && Enum.IsDefined(key))
            return key;
        int index = Array.IndexOf(Legacy, name);
        return index >= 0 ? (Parameter)index : throw new ArgumentException($"Unknown parameter: {name}");
    }
}

public sealed class Parameters
{
    public static readonly IReadOnlyList<ParameterSpec> Specs = Array.AsReadOnly(new ParameterSpec[] {
        new(Parameter.HeadYaw,0,-1,1),
        new(Parameter.HeadPitch,0,-1,1),
        new(Parameter.HeadRoll,0,-1,1),
        new(Parameter.LeftEyeOpenness,1,0,1),
        new(Parameter.RightEyeOpenness,1,0,1),
        new(Parameter.GazeHorizontal,0,-1,1),
        new(Parameter.GazeVertical,0,-1,1),
        new(Parameter.EyebrowHeight,0,-1,1),
        new(Parameter.MouthOpenness,0,0,1),
        new(Parameter.MouthShape,0,-1,1),
        new(Parameter.ClosedMouthOffsetY,0,-1,1),
        new(Parameter.BodyRoll,0,-1,1),
        new(Parameter.HairSwayAmplitude,2,0,3),
        new(Parameter.HairSoftness,2,0,3),
        new(Parameter.LeftEyebrowAngle,0,-1,1),
        new(Parameter.RightEyebrowAngle,0,-1,1),
        new(Parameter.SymmetricEyebrowAngle,0,-1,1),
        new(Parameter.LeftFringeOffset,0,-1,1),
        new(Parameter.CenterFringeOffset,0,-1,1),
        new(Parameter.RightFringeOffset,0,-1,1),
        new(Parameter.ArmLift,0,-1,1),
        new(Parameter.ArmOffsetY,0,-1,1),
        new(Parameter.ChestBounceAmplitude,2.5,0,4),
        new(Parameter.ChestBounceOffsetY,1,-3,3),
        new(Parameter.IrisScale,1,0.5,1.3),
        new(Parameter.MouthTransition,0.72,0,1),
        new(Parameter.EyeTransition,0.3,0,1),
        new(Parameter.FringeSwayAmplitude,2,0,3),
        new(Parameter.FringeSoftness,0.4,0,2),
        new(Parameter.ClosedEyeOffsetY,0,-1,1),
        new(Parameter.ClosedEyeAngle,0,-1,1),
        new(Parameter.ClosedMouthAngle,0,-1,1),
        new(Parameter.LeftEyeScale,1,0.5,1.5),
        new(Parameter.RightEyeScale,1,0.5,1.5),
        new(Parameter.MouthScale,1,0.5,1.5)
    });
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
    public bool Mouse { get; set; }
    public bool Physics { get; set; } = true;
    public void DisableAll()
    {
        Idle = Blink = Random = Talk = Mouse = Physics = false;
    }
}
