using System;
using System.Collections.Generic;
using System.Linq;

namespace Anime25D.Sample.Core;

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

public static class SampleParameters
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
}
