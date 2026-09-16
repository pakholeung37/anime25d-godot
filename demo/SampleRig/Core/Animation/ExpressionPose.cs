using System.Collections.Generic;

namespace Anime25D.Sample.Core;

/// <summary>Named fields replace positional preset arrays; a profile can replace or add expressions.</summary>
public sealed record ExpressionPose(double LeftEyeOpenness, double RightEyeOpenness, double EyebrowHeight,
    double MouthOpenness, double MouthShape, double IrisScale)
{
    public static IReadOnlyDictionary<string, ExpressionPose> Defaults
    { get; } = new Dictionary<string, ExpressionPose>
    {
        ["neutral"] = new(1, 1, 0, 0, 0, 1),
        ["smile"] = new(0, 0, 0.45, 0, 0.9, 1),
        ["usume"] = new(0.5, 0.5, 0.35, 1, 0.8, 1),
        ["surprise"] = new(1, 1, 1, 0.75, -0.1, 0.7),
        ["jito"] = new(0.4, 0.4, -0.6, 0, -0.4, 1),
        ["winkL"] = new(0, 1, 0.2, 0.4, 0.7, 1),
        ["winkR"] = new(1, 0, 0.2, 0.4, 0.7, 1)
    };

}
