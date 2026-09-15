using System;

namespace Anime25D.Core;

public sealed record Wave(double Amplitude, double AngularFrequency, double Phase = 0)
{
    public double Sample(double seconds) => Amplitude * Math.Sin(seconds * AngularFrequency + Phase);
}
public sealed record DelayRange(double MinimumMilliseconds, double SpreadMilliseconds)
{
    public double Sample(Func<double> random) => MinimumMilliseconds + random() * SpreadMilliseconds;
}
public sealed record MotionSettings
{
    public double MaximumDeltaSeconds { get; init; } = 0.05;
    public double SmoothingRate { get; init; } = 14;
    public IdleSettings Idle { get; init; } = new();
    public RandomMotionSettings Random { get; init; } = new();
    public TalkSettings Talk { get; init; } = new();
    public BlinkSettings Blink { get; init; } = new();
    public BreathSettings Breath { get; init; } = new();
}
public sealed record IdleSettings
{
    public Wave YawPrimary { get; init; } = new(0.13, 0.42);
    public Wave YawSecondary { get; init; } = new(0.05, 1.13);
    public Wave Pitch { get; init; } = new(0.08, 0.31, 1.7);
    public Wave Roll { get; init; } = new(0.07, 0.23, 0.5);
    public Wave BodyRoll { get; init; } = new(0.10, 0.19, 2.1);
}
public sealed record RandomMotionSettings
{
    public DelayRange Interval { get; init; } = new(1400, 2600);
    public double HeadYaw { get; init; } = 0.55;
    public double HeadPitch { get; init; } = 0.40;
    public double HeadRoll { get; init; } = 0.35;
    public double BodyRoll { get; init; } = 0.30;
    public double GazeHorizontal { get; init; } = 0.60;
    public double GazeVertical { get; init; } = 0.35;
}
public sealed record TalkSettings
{
    public DelayRange SpeakingInterval { get; init; } = new(1200, 2200);
    public DelayRange SilentInterval { get; init; } = new(600, 1800);
    public DelayRange SyllableInterval { get; init; } = new(70, 110);
    public double ClosedSyllableProbability { get; init; } = 0.25;
    public double ClosedOpenness { get; init; } = 0.04;
    public double MinimumOpenness { get; init; } = 0.25;
    public double OpennessSpread { get; init; } = 0.75;
    public double ResponseRate { get; init; } = 22;
}
public sealed record BlinkSettings
{
    public double InitialDelayMilliseconds { get; init; } = 1800;
    public DelayRange Interval { get; init; } = new(1600, 3800);
    public double AlternateProbability { get; init; } = 0.2;
    public double DoubleBlinkProbability { get; init; } = 0.18;
    public double DoubleBlinkDelayMilliseconds { get; init; } = 280;
    public double CloseSeconds { get; init; } = 0.08;
    public double HoldSeconds { get; init; } = 0.34;
    public double AlternateHoldSeconds { get; init; } = 3.4;
    public double OpenSeconds { get; init; } = 0.16;
    public double IrisBounceTrigger { get; init; } = 0.12;
    public double IrisBounceSeconds { get; init; } = 0.52;
    public double IrisBounceDamping { get; init; } = 2.2;
    public double IrisBounceCycles { get; init; } = 2;
    public double IrisBounceAmplitude { get; init; } = 0.18;
    public double IrisSquashAmplitude { get; init; } = 0.10;
}
public sealed record BreathSettings
{
    public double PeriodSeconds { get; init; } = 3.4;
    public double HeadPhaseLagRadians { get; init; } = 0.6;
}
