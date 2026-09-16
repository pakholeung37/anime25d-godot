using System;
using static Anime25D.Sample.Core.Parameter;
using static System.Math;

namespace Anime25D.Sample.Core;

internal sealed class BlinkMotion
{
    private readonly BlinkSettings settings;
    private readonly bool alternateAvailable;
    private double elapsedSeconds = -1, bounceSeconds = -1, nextBlinkMilliseconds;
    private bool bounceStarted;
    public int Variant { get; private set; } = 1;
    public BlinkMotion(BlinkSettings settings, bool alternateAvailable)
    {
        this.settings = settings;
        this.alternateAvailable = alternateAvailable;
        nextBlinkMilliseconds = settings.InitialDelayMilliseconds;
    }
    public void Reset()
    {
        elapsedSeconds = bounceSeconds = -1;
        Variant = 1;
    }
    public void Apply(Parameters target, double now, double deltaSeconds, Func<double> random)
    {
        if (elapsedSeconds < 0 && now > nextBlinkMilliseconds)
        {
            elapsedSeconds = 0;
            bounceStarted = false;
            Variant = alternateAvailable && random() < settings.AlternateProbability ? 2 : 1;
            nextBlinkMilliseconds = now + settings.Interval.Sample(random);
            if (random() < settings.DoubleBlinkProbability)
                nextBlinkMilliseconds = now + settings.DoubleBlinkDelayMilliseconds;
        }
        if (elapsedSeconds < 0)
            return;
        elapsedSeconds += deltaSeconds;
        double hold = Variant == 2 ? settings.AlternateHoldSeconds : settings.HoldSeconds;
        double openness;
        if (elapsedSeconds < settings.CloseSeconds)
            openness = 1 - elapsedSeconds / settings.CloseSeconds;
        else if (elapsedSeconds < settings.CloseSeconds + hold)
            openness = 0;
        else if (elapsedSeconds < settings.CloseSeconds + settings.OpenSeconds + hold)
        {
            openness = (elapsedSeconds - settings.CloseSeconds - hold) / settings.OpenSeconds;
            if (!bounceStarted && openness > settings.IrisBounceTrigger)
            {
                bounceStarted = true;
                bounceSeconds = 0;
            }
        }
        else
        {
            openness = 1;
            elapsedSeconds = -1;
        }
        target[LeftEyeOpenness] = Min(target[LeftEyeOpenness], openness);
        target[RightEyeOpenness] = Min(target[RightEyeOpenness], openness);
    }
    public void AdvanceBounce(double deltaSeconds)
    {
        if (bounceSeconds < 0)
            return;
        bounceSeconds += deltaSeconds;
        if (bounceSeconds > settings.IrisBounceSeconds)
            bounceSeconds = -1;
    }
    public void ApplyBounce(FrameParameters frame)
    {
        frame.IrisBounceX = frame.IrisBounceY = 1;
        if (bounceSeconds < 0)
            return;
        double progress = bounceSeconds / settings.IrisBounceSeconds;
        double damping = Exp(-settings.IrisBounceDamping * progress);
        double phase = progress * PI * (settings.IrisBounceCycles * 2);
        double scale = 1 + settings.IrisBounceAmplitude * Sin(phase) * damping;
        double squash = settings.IrisSquashAmplitude * Sin(phase + PI / 2) * damping;
        frame.IrisBounceX = scale * (1 + squash);
        frame.IrisBounceY = scale * (1 - squash);
    }
}
