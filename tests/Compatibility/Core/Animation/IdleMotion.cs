using System;
using static Anime25D.Sample.Core.Parameter;
using static System.Math;

namespace Anime25D.Sample.Core;

internal static class IdleMotion
{
    public static void Apply(Parameters target, IdleSettings settings, double seconds)
    {
        target[HeadYaw] += settings.YawPrimary.Sample(seconds) + settings.YawSecondary.Sample(seconds);
        target[HeadPitch] += settings.Pitch.Sample(seconds);
        target[HeadRoll] += settings.Roll.Sample(seconds);
        target[BodyRoll] += settings.BodyRoll.Sample(seconds);
    }
}
