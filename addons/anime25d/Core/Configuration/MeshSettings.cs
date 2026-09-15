using System;

namespace Anime25D.Core;

public sealed record MeshSettings
{
    public double BaseCellPixels { get; init; } = 42;
    public double PhysicsCellPixels { get; init; } = 30;
    public double ReferenceCanvasWidth { get; init; } = 768;
    public double MinimumScale { get; init; } = 0.6;
    public double SingleStrandSpacingPixels { get; init; } = 120;
    public double StrandInfluenceWidth { get; init; } = 0.6;
    public double FringeBoundaryFaceRatio { get; init; } = 0.22;
    public double FringeTransitionPixels { get; init; } = 36;
}
