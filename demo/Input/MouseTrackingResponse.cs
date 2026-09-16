using System;
using Anime25D.Sample.Core;

namespace Anime25D.Examples;

/// <summary>Demo pointer-to-pose mapping; no input or mouse concepts belong to the rig core.</summary>
public sealed record MouseTrackingResponse
{
    public double HeadYawGain { get; init; } = 0.9;
    public double HeadPitchGain { get; init; } = 0.7;
    public double HorizontalGazeGain { get; init; } = 1.2;
    public double VerticalGazeGain { get; init; } = 0.8;

    public void Apply(Anime25D.Runtime.ParameterSet parameters, double horizontal, double vertical)
    {
        if (!double.IsFinite(horizontal) || !double.IsFinite(vertical)) return;
        parameters[nameof(Parameter.HeadYaw)] = horizontal * HeadYawGain;
        parameters[nameof(Parameter.HeadPitch)] = -vertical * HeadPitchGain;
        parameters[nameof(Parameter.GazeHorizontal)] = horizontal * HorizontalGazeGain;
        parameters[nameof(Parameter.GazeVertical)] = -vertical * VerticalGazeGain;
    }

}
