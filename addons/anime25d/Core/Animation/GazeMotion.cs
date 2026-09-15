using System;
using static Anime25D.Core.Parameter;
using static System.Math;

namespace Anime25D.Core;

internal static class GazeMotion
{
    public static void Apply(Parameters target, GazeSettings settings, double horizontal, double vertical)
    {
        target.Set(HeadYaw, horizontal * settings.HeadHorizontal);
        target.Set(HeadPitch, -vertical * settings.HeadVertical);
        target.Set(GazeHorizontal, horizontal * settings.EyeHorizontal);
        target.Set(GazeVertical, -vertical * settings.EyeVertical);
    }
}
