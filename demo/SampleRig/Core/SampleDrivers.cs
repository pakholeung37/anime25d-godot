using System;
using Anime25D.Runtime;
using static System.Math;
namespace Anime25D.Sample.Core;

internal sealed class SampleRandomDriver(RandomMotionSettings settings) : ModelComponent
{
    private double next;
    private readonly double[] values = new double[6];
    private static readonly string[] ids = ["HeadYaw", "HeadPitch", "HeadRoll", "BodyRoll", "GazeHorizontal", "GazeVertical"];
    public override void AdvanceState(ComponentContext c, double delta)
    {
        if (c.TimeMilliseconds <= next) return;
        next = c.TimeMilliseconds + settings.Interval.Sample(c.Random);
        double[] gains = [settings.HeadYaw, settings.HeadPitch, settings.HeadRoll, settings.BodyRoll, settings.GazeHorizontal, settings.GazeVertical];
        for (int i = 0; i < values.Length; i++) values[i] = (c.Random() * 2 - 1) * gains[i];
    }
    public override void Reset() { next = 0; Array.Clear(values); }
    public override void EvaluateOutput(ComponentContext c)
    { for (int i = 0; i < values.Length; i++) c.Pose[ids[i]] += values[i]; }
}
internal sealed class SampleTalkDriver(TalkSettings settings) : ModelComponent
{
    private double nextState, nextSyllable, openness, target;
    private bool speaking;
    public override void AdvanceState(ComponentContext c, double delta)
    {
        double now = c.TimeMilliseconds;
        if (now > nextState) { speaking = !speaking; nextState = now + (speaking ? settings.SpeakingInterval : settings.SilentInterval).Sample(c.Random); }
        if (speaking && now > nextSyllable) { nextSyllable = now + settings.SyllableInterval.Sample(c.Random); target = c.Random() < settings.ClosedSyllableProbability ? settings.ClosedOpenness : settings.MinimumOpenness + c.Random() * settings.OpennessSpread; }
        if (!speaking) target = 0;
        openness += (target - openness) * Min(1, delta * settings.ResponseRate);
    }
    public override void Reset() { nextState = nextSyllable = openness = target = 0; speaking = false; }
    public override void EvaluateOutput(ComponentContext c) => c.Pose["MouthOpenness"] = Max(c.Pose["MouthOpenness"], openness);
}
internal sealed class SampleBlinkDriver(BlinkSettings settings, bool alternateAvailable) : ModelComponent
{
    private double elapsed = -1, bounce = -1, next = settings.InitialDelayMilliseconds, openness = 1;
    private bool bounceStarted;
    private int variant = 1;
    public override void AdvanceState(ComponentContext c, double delta)
    {
        double now = c.TimeMilliseconds;
        if (elapsed < 0 && now > next)
        {
            elapsed = 0; bounceStarted = false;
            variant = alternateAvailable && c.Random() < settings.AlternateProbability ? 2 : 1;
            next = now + settings.Interval.Sample(c.Random);
            if (c.Random() < settings.DoubleBlinkProbability) next = now + settings.DoubleBlinkDelayMilliseconds;
        }
        if (elapsed >= 0)
        {
            elapsed += delta; double hold = variant == 2 ? settings.AlternateHoldSeconds : settings.HoldSeconds;
            if (elapsed < settings.CloseSeconds) openness = 1 - elapsed / settings.CloseSeconds;
            else if (elapsed < settings.CloseSeconds + hold) openness = 0;
            else if (elapsed < settings.CloseSeconds + hold + settings.OpenSeconds)
            {
                openness = (elapsed - settings.CloseSeconds - hold) / settings.OpenSeconds;
                if (!bounceStarted && openness > settings.IrisBounceTrigger) { bounceStarted = true; bounce = 0; }
            }
            else { openness = 1; elapsed = -1; }
        }
        if (bounce >= 0) { bounce += delta; if (bounce > settings.IrisBounceSeconds) bounce = -1; }
    }
    public override void EvaluateOutput(ComponentContext c)
    {
        c.Pose["LeftEyeOpenness"] = Min(c.Pose["LeftEyeOpenness"], openness);
        c.Pose["RightEyeOpenness"] = Min(c.Pose["RightEyeOpenness"], openness);
        c.Pose[SampleAnimations.EyeVariant] = variant;
        if (bounce >= 0)
        {
            double progress = bounce / settings.IrisBounceSeconds, damping = Exp(-settings.IrisBounceDamping * progress), phase = progress * PI * settings.IrisBounceCycles * 2;
            double scale = 1 + settings.IrisBounceAmplitude * Sin(phase) * damping, squash = settings.IrisSquashAmplitude * Sin(phase + PI / 2) * damping;
            c.Pose[SampleAnimations.IrisX] = scale * (1 + squash); c.Pose[SampleAnimations.IrisY] = scale * (1 - squash);
        }
    }
    public override void Reset() { elapsed = bounce = -1; next = settings.InitialDelayMilliseconds; openness = 1; variant = 1; bounceStarted = false; }
}
