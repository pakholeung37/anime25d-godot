using System.Collections.Generic;
using System.Linq;
using Anime25D.Core;

namespace Anime25D.Sample.Core;

/// <summary>Example assets authored entirely in code. These channel names belong to this rig only.</summary>
public static class SampleAnimations
{
    public const string Breath = "Breath", BreathHead = "BreathHead", IrisX = "IrisBounceX", IrisY = "IrisBounceY", EyeVariant = "ClosedEyeVariant";
    public static AnimationModel Create(RigProfile profile)
    {
        var parameters = profile.CreateCatalog().Select(p => new ParameterDefinition(p.Key.ToString(), p.Default, p.Min, p.Max)).ToList();
        parameters.AddRange(new[] {
            new ParameterDefinition(Breath, 0, 0, 1), new ParameterDefinition(BreathHead, 0, 0, 1),
            new ParameterDefinition(IrisX, 1, 0, 4), new ParameterDefinition(IrisY, 1, 0, 4),
            new ParameterDefinition(EyeVariant, 1, 1, 2)
        });
        var motions = new Dictionary<string, MotionDefinition>
        {
            ["nod"] = new(1.2, new[] { new MotionTrack(nameof(Parameter.HeadPitch), MotionCurve.Smooth((0, 0), (0.35, 0.6), (0.8, -0.15), (1.2, 0))) }),
            ["sway"] = new(3, new[] {
                new MotionTrack(nameof(Parameter.HeadRoll), MotionCurve.Smooth((0, 0), (0.75, 0.3), (2.25, -0.3), (3, 0))),
                new MotionTrack(nameof(Parameter.BodyRoll), MotionCurve.Smooth((0, 0), (0.75, 0.2), (2.25, -0.2), (3, 0)))
            }, loop: true)
        };
        var expressions = profile.Expressions.ToDictionary(pair => pair.Key, pair =>
        {
            var e = pair.Value;
            return new ExpressionDefinition(new[] {
                new ExpressionValue(nameof(Parameter.LeftEyeOpenness), e.LeftEyeOpenness, BlendMode.Multiply),
                new ExpressionValue(nameof(Parameter.RightEyeOpenness), e.RightEyeOpenness, BlendMode.Multiply),
                new ExpressionValue(nameof(Parameter.EyebrowHeight), e.EyebrowHeight),
                new ExpressionValue(nameof(Parameter.MouthOpenness), e.MouthOpenness, BlendMode.Add),
                new ExpressionValue(nameof(Parameter.MouthShape), e.MouthShape),
                new ExpressionValue(nameof(Parameter.IrisScale), e.IrisScale, BlendMode.Multiply)
            });
        });
        return new(parameters, motions, expressions);
    }
}
