using System;
using static Anime25D.Core.Parameter;
using static System.Math;

namespace Anime25D.Core;

internal sealed class TalkMotion
{
    private double nextStateMilliseconds, nextSyllableMilliseconds, openness, targetOpenness;
    private bool speaking;
    public void Apply(Parameters target, TalkSettings settings, double now, double deltaSeconds, Func<double> random)
    {
        if (now > nextStateMilliseconds)
        {
            speaking = !speaking;
            nextStateMilliseconds = now + (speaking ? settings.SpeakingInterval : settings.SilentInterval).Sample(random);
        }
        if (speaking && now > nextSyllableMilliseconds)
        {
            nextSyllableMilliseconds = now + settings.SyllableInterval.Sample(random);
            targetOpenness = random() < settings.ClosedSyllableProbability
                ? settings.ClosedOpenness : settings.MinimumOpenness + random() * settings.OpennessSpread;
        }
        if (!speaking)
            targetOpenness = 0;
        openness += (targetOpenness - openness) * Min(1, deltaSeconds * settings.ResponseRate);
        target[MouthOpenness] = Max(target[MouthOpenness], openness);
    }
}
