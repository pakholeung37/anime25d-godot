using System;
using static Anime25D.Core.Parameter;
using static System.Math;

namespace Anime25D.Core;

internal sealed class RandomMotion
{
    private double nextChangeMilliseconds;
    private double yaw, pitch, roll, bodyRoll, gazeHorizontal, gazeVertical;
    public void Apply(Parameters target, RandomMotionSettings settings, double now, Func<double> random)
    {
        if (now > nextChangeMilliseconds)
        {
            nextChangeMilliseconds = now + settings.Interval.Sample(random);
            yaw = (random() * 2 - 1) * settings.HeadYaw;
            pitch = (random() * 2 - 1) * settings.HeadPitch;
            roll = (random() * 2 - 1) * settings.HeadRoll;
            bodyRoll = (random() * 2 - 1) * settings.BodyRoll;
            gazeHorizontal = (random() * 2 - 1) * settings.GazeHorizontal;
            gazeVertical = (random() * 2 - 1) * settings.GazeVertical;
        }
        target.Set(HeadYaw, target[HeadYaw] + yaw);
        target.Set(HeadPitch, target[HeadPitch] + pitch);
        target.Set(HeadRoll, target[HeadRoll] + roll);
        target.Set(BodyRoll, target[BodyRoll] + bodyRoll);
        target.Set(GazeHorizontal, target[GazeHorizontal] + gazeHorizontal);
        target.Set(GazeVertical, target[GazeVertical] + gazeVertical);
    }
}
