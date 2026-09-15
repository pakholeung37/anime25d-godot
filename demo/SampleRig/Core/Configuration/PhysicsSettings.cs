using System;

namespace Anime25D.Sample.Core;

public sealed record SpringSettings(double Stiffness, double Damping, double DisplacementScale);
public sealed record PhysicsSettings
{
    public double IntegrationStepSeconds { get; init; } = 1.0 / 120;
    public SpringSettings StiffHair { get; init; } = new(70, 9, 2.2);
    public SpringSettings SoftHair { get; init; } = new(16, 1.3, 3);
    public SpringSettings Chest { get; init; } = new(140, 4.2, 3);
    public double WindPrimaryAmplitude { get; init; } = 1.8;
    public double WindPrimaryFrequency { get; init; } = 0.8;
    public double WindSecondaryAmplitude { get; init; } = 1;
    public double WindSecondaryFrequency { get; init; } = 1.9;
    public double WindSecondaryPhaseMultiplier { get; init; } = 2.3;
    public double StrandPhaseSpacing { get; init; } = 1.37;
    public double ChestBreathDrive { get; init; } = 3;
    public double ChestPitchDrive { get; init; } = 6;
    public double ChestBodyDrive { get; init; } = 4;
}
