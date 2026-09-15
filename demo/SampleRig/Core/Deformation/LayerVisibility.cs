using static Anime25D.Sample.Core.Parameter;
using static Anime25D.Sample.Core.RigMath;

namespace Anime25D.Sample.Core;

public static class LayerVisibility
{
    // Numerical/rendering cutoff, not an artistic control.
    public const double RenderThreshold = 0.004;

    public static void Update(PartState[] parts, FrameParameters frame, int blinkVariant, DeformationSettings settings)
    {
        bool alternateLeft = false, alternateRight = false, alternateUnspecified = false;
        if (blinkVariant == 2)
        {
            foreach (var part in parts)
            {
                if (part.Definition.Fade != FadeMode.AlternateClosedEye || !part.Visible || part.Opacity <= 0)
                    continue;
                alternateLeft |= part.Definition.Side == PartSide.Left;
                alternateRight |= part.Definition.Side == PartSide.Right;
                alternateUnspecified |= part.Definition.Side == PartSide.None;
            }
        }
        foreach (var part in parts)
        {
            bool alternate = part.Definition.Side switch
            {
                PartSide.Left => alternateLeft,
                PartSide.Right => alternateRight,
                _ => alternateUnspecified
            };
            part.Alpha = part.Visible
                ? Evaluate(part.Definition, frame, settings, alternate) * System.Math.Clamp(part.Opacity, 0, 1) : 0;
        }
    }

    private static double Evaluate(PartDefinition definition, FrameParameters frame, DeformationSettings settings, bool alternate)
    {
        double openness = definition.Side == PartSide.Left ? frame[LeftEyeOpenness] : frame[RightEyeOpenness];
        double eyeFade = Smooth((openness - (settings.EyeFadeOffset + frame[EyeTransition] * settings.EyeFadeSensitivity)) / settings.EyeFadeWidth);
        switch (definition.Fade)
        {
            case FadeMode.OpenEye:
                return eyeFade;
            case FadeMode.ClosedEye:
            case FadeMode.AlternateClosedEye:
                return (definition.Fade == FadeMode.AlternateClosedEye) != alternate ? 0 : 1 - eyeFade;
            case FadeMode.OpenMouth:
            case FadeMode.ClosedMouth:
                double mouthFade = Smooth((frame[MouthOpenness] - (settings.MouthFadeOffset + frame[MouthTransition] * settings.MouthFadeSensitivity)) / settings.MouthFadeWidth);
                return definition.Fade == FadeMode.OpenMouth ? mouthFade : 1 - mouthFade;
            default:
                return 1;
        }
    }
}
