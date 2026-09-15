using static Anime25D.Sample.Core.Parameter;
using static System.Math;

namespace Anime25D.Sample.Core;

/// <summary>CPU integrates a few springs; render backends consume displacements, not solver state.</summary>
public sealed class PhysicsSolver
{
    public Spring Chest { get; } = new();
    private readonly PhysicsSettings settings;
    private readonly DeformationSettings deformation;
    private readonly RigAnchors anchors;

    public PhysicsSolver(PhysicsSettings settings, DeformationSettings deformation, RigAnchors anchors)
    {
        this.settings = settings;
        this.deformation = deformation;
        this.anchors = anchors;
    }

    public void Step(FrameParameters frame, PartState[] parts, bool windEnabled, double seconds, double deltaSeconds)
    {
        double faceScale = anchors.FaceScale;
        double headDisplacement = (frame[HeadYaw] * deformation.HeadYawPixels + frame[HeadRoll] * deformation.HeadRollRadians *
            (anchors.NeckPivot.CenterY - anchors.Face.CenterY)) * faceScale;
        foreach (var part in parts)
            foreach (var strand in part.Springs)
            {
                double wind = windEnabled
                    ? settings.WindPrimaryAmplitude * Sin(seconds * settings.WindPrimaryFrequency + strand.Phase)
                        + settings.WindSecondaryAmplitude * Sin(seconds * settings.WindSecondaryFrequency + strand.Phase * settings.WindSecondaryPhaseMultiplier)
                    : 0;
                double target = headDisplacement + wind * faceScale;
                Integrate(strand.Stiff, target, settings.StiffHair, deltaSeconds, settings.IntegrationStepSeconds);
                Integrate(strand.Soft, target, settings.SoftHair, deltaSeconds, settings.IntegrationStepSeconds);
            }
        double chestTarget = (frame.Breath * settings.ChestBreathDrive - frame[HeadPitch] * settings.ChestPitchDrive +
            frame[BodyRoll] * settings.ChestBodyDrive) * faceScale;
        Integrate(Chest, chestTarget, settings.Chest, deltaSeconds, settings.IntegrationStepSeconds);
    }
    private static void Integrate(Spring spring, double target, SpringSettings settings, double deltaSeconds, double integrationStep)
    {
        spring.Step(target, settings.Stiffness, settings.Damping, deltaSeconds, integrationStep);
        spring.Displacement = -(spring.Position - target) * settings.DisplacementScale;
    }
}
