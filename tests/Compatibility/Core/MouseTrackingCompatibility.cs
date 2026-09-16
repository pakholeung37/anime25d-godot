using System;
using Anime25D.Sample.Core;
namespace Anime25D.Examples;
public static class MouseTrackingCompatibility {
    // Original pre-smoothing mapping retained for pinned numerical reference fixtures.
    public static void Apply(this MouseTrackingResponse response, Parameters parameters, double horizontal, double vertical)
    {
        if (!double.IsFinite(horizontal) || !double.IsFinite(vertical)) return;
        parameters.Set(Parameter.HeadYaw, horizontal * response.HeadYawGain);
        parameters.Set(Parameter.HeadPitch, -vertical * response.HeadPitchGain);
        parameters.Set(Parameter.GazeHorizontal, horizontal * response.HorizontalGazeGain);
        parameters.Set(Parameter.GazeVertical, -vertical * response.VerticalGazeGain);
    }
}
