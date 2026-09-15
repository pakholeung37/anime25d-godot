using System;

namespace Anime25D.Core;

/// <summary>Pixel distances are in model space; only explicitly face-scaled terms scale with FaceScale.</summary>
public sealed record DeformationSettings
{
    public double EyeFadeOffset { get; init; } = 0.10;
    public double EyeFadeSensitivity { get; init; } = 0.45;
    public double EyeFadeWidth { get; init; } = 0.15;
    public double MouthFadeOffset { get; init; } = 0.05;
    public double MouthFadeSensitivity { get; init; } = 0.35;
    public double MouthFadeWidth { get; init; } = 0.12;
    public double HeadRollRadians { get; init; } = 0.07;
    public double BodyRollRadians { get; init; } = 0.028;
    public double ChestCenterFaceRatio { get; init; } = 0.60;
    public double ChestWidthFaceRatio { get; init; } = 0.60;
    public double ChestHeightFaceRatio { get; init; } = 0.45;
    public double GazeHorizontalPixels { get; init; } = 11;
    public double GazeVerticalPixels { get; init; } = 6;
    public double IrisCloseCompression { get; init; } = 0.80;
    public double IrisCloseThreshold { get; init; } = 0.32;
    public double EyeCloseCompression { get; init; } = 0.85;
    public double ClosedEyeLiftPixels { get; init; } = 3;
    public double ClosedEyeOffsetPixels { get; init; } = 14;
    public double ClosedEyeAngleRadians { get; init; } = 0.3;
    public double EyebrowLiftPixels { get; init; } = 9;
    public double EyebrowBlinkPixels { get; init; } = 3.5;
    public double EyebrowAngleRadians { get; init; } = 0.30;
    public double MouthMinimumHeight { get; init; } = 0.5;
    public double MouthOpeningHeight { get; init; } = 0.5;
    public double MouthCurvePaddingPixels { get; init; } = 4;
    public double MouthCurveExponent { get; init; } = 1.5;
    public double MouthCurvePixels { get; init; } = 6;
    public double MouthCurveOffset { get; init; } = 0.35;
    public double ClosedMouthOffsetPixels { get; init; } = 14;
    public double ClosedMouthAngleRadians { get; init; } = 0.35;
    public double JawOpenPixels { get; init; } = 6;
    public double BodyHeadInfluence { get; init; } = 0.16;
    public double NeckHeadInfluence { get; init; } = 0.55;
    public double HeadYawPixels { get; init; } = 14;
    public double HeadYawDepthPixels { get; init; } = 40;
    public double HeadYawShear { get; init; } = 0.028;
    public double HeadPitchPixels { get; init; } = 9;
    public double HeadPitchDepthPixels { get; init; } = 30;
    public double HeadPitchShear { get; init; } = 0.05;
    public double BodyBreathPixels { get; init; } = 2;
    public double HeadBreathPixels { get; init; } = 1.6;
    public double ChestBreathPixels { get; init; } = 2.2;
    public double ChestBreathFalloff { get; init; } = 2;
    public double ChestBreathExpansion { get; init; } = 0.003;
    public double ChestOffsetPixels { get; init; } = 70;
    public double ArmInfluenceScale { get; init; } = 1.15;
    public double ArmLiftPixels { get; init; } = 30;
    public double ArmOffsetPixels { get; init; } = 40;
    public double ArmSidewaysPixels { get; init; } = 6;
    public double FringeOffsetExponent { get; init; } = 1.4;
    public double FringeOffsetPixels { get; init; } = 22;
    public double FringeLengthScale { get; init; } = 1.6;
    public double FringeSwayExponent { get; init; } = 1.8;
    public double HairSwayExponent { get; init; } = 2.1;
    public double HairSoftnessExponent { get; init; } = 1.2;
    public double HairVerticalSway { get; init; } = 0.12;
}
