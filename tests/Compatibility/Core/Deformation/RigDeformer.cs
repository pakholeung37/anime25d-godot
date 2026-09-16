using System;
using static Anime25D.Sample.Core.Parameter;
using static Anime25D.Sample.Core.RigMath;
using static System.Math;

namespace Anime25D.Sample.Core;

public readonly record struct LegacyDeformationInput(RigAnchors Anchors, FrameParameters Pose,
    DeformationSettings Settings, double ChestDisplacement, bool HairPhysicsEnabled);

/// <summary>Double-precision CPU oracle and fallback. GPU parity tests cover the matching shader stages.</summary>
public static class LegacyRigDeformer
{
    public static void Deform(PartState part, in LegacyDeformationInput input, Span<float> output)
    {
        var evaluator = new VertexEvaluator(part, input);
        var rest = part.Geometry.RestPositions;
        for (int index = 0; index < rest.Length; index += 2)
        {
            double x = rest[index];
            double y = rest[index + 1];
            evaluator.ApplyFeatures(ref x, ref y);
            evaluator.ApplyHead(ref x, ref y);
            evaluator.ApplyBody(ref x, ref y);
            evaluator.ApplyHair(ref x, ref y, index / 2);
            // Preserve the original Float32 boundary before the final body rotation.
            output[index] = (float)x;
            output[index + 1] = (float)y;
        }
        evaluator.RotateBody(output);
    }

    private readonly struct VertexEvaluator
    {
        private readonly PartState part;
        private readonly double chestDisplacement;
        private readonly bool hairPhysicsEnabled;
        private readonly DeformationSettings settings;
        private readonly PartDefinition definition;
        private readonly RigAnchors anchors;
        private readonly FrameParameters frame;
        private readonly Anchor neckPivot, bodyPivot;
        private readonly Anchor? eye;
        private readonly PartRole role;
        private readonly bool head, frontHair;
        private readonly double faceScale, headCosine, headSine, bodyRotation, bodyCosine, bodySine;
        private readonly double eyeOpenness, mouthOpenness, mouthHalfWidth, centerX, centerY;
        private readonly double chestX, chestY, chestRadiusX, chestRadiusY;
        private readonly int strandCount;

        public VertexEvaluator(PartState part, in LegacyDeformationInput input)
        {
            this.part = part;
            chestDisplacement = input.ChestDisplacement;
            hairPhysicsEnabled = input.HairPhysicsEnabled;
            settings = input.Settings;
            definition = part.Definition;
            anchors = input.Anchors;
            frame = input.Pose;
            neckPivot = anchors.NeckPivot;
            bodyPivot = anchors.BodyPivot;
            faceScale = anchors.FaceScale;
            double headRotation = frame[HeadRoll] * settings.HeadRollRadians;
            headCosine = Cos(headRotation);
            headSine = Sin(headRotation);
            bodyRotation = frame[BodyRoll] * settings.BodyRollRadians;
            bodyCosine = Cos(bodyRotation);
            bodySine = Sin(bodyRotation);
            role = definition.Role;
            head = definition.Group == PartGroup.Head;
            frontHair = role == PartRole.Fringe;
            eye = definition.Side == PartSide.Left ? anchors.LeftEye : definition.Side == PartSide.Right ? anchors.RightEye : null;
            eyeOpenness = definition.Side == PartSide.Left ? frame[LeftEyeOpenness] : frame[RightEyeOpenness];
            mouthOpenness = frame[MouthOpenness];
            mouthHalfWidth = (anchors.Mouth.MaximumX - anchors.Mouth.MinimumX) / 2;
            centerX = definition.X + definition.Width / 2.0;
            centerY = definition.Y + definition.Height / 2.0;
            chestX = neckPivot.CenterX;
            chestY = anchors.NeckBottomY + (anchors.Face.MaximumY - anchors.Face.MinimumY) * settings.ChestCenterFaceRatio;
            chestRadiusX = Max(1, (anchors.Face.MaximumX - anchors.Face.MinimumX) * settings.ChestWidthFaceRatio);
            chestRadiusY = Max(1, (anchors.Face.MaximumY - anchors.Face.MinimumY) * settings.ChestHeightFaceRatio);
            strandCount = part.Springs.Length;
        }

        public void ApplyFeatures(ref double x, ref double y)
        {
            if (eye is not null && role is PartRole.ClosedEye or PartRole.AlternateClosedEye)
            {
                double scale = definition.Side == PartSide.Left ? frame[LeftEyeScale] : frame[RightEyeScale];
                if (scale != 1)
                {
                    double cx = (eye.MinimumX + eye.MaximumX) / 2, cy = (eye.MinimumY + eye.MaximumY) / 2;
                    x = cx + (x - cx) * scale;
                    y = cy + (y - cy) * scale;
                }
            }
            if (role is PartRole.OpenMouth or PartRole.ClosedMouth)
            {
                double scale = frame[MouthScale];
                if (scale != 1)
                {
                    x = anchors.Mouth.CenterX + (x - anchors.Mouth.CenterX) * scale;
                    y = anchors.Mouth.CenterY + (y - anchors.Mouth.CenterY) * scale;
                }
            }
            if (definition.Fade == FadeMode.OpenEye && eye is not null)
            {
                if (role == PartRole.Iris)
                {
                    x = eye.IrisCenterX + (x - eye.IrisCenterX) * frame[IrisScale] * frame.IrisBounceX;
                    y = eye.IrisCenterY + (y - eye.IrisCenterY) * frame[IrisScale] * frame.IrisBounceY;
                    x += frame[GazeHorizontal] * settings.GazeHorizontalPixels * faceScale;
                    y += frame[GazeVertical] * settings.GazeVerticalPixels * faceScale;
                    y = eye.ClosedEyeY + (y - eye.ClosedEyeY) * (1 - settings.IrisCloseCompression * Smooth((settings.IrisCloseThreshold - eyeOpenness) / settings.IrisCloseThreshold));
                }
                else
                    y = eye.ClosedEyeY + (y - eye.ClosedEyeY) * (1 - settings.EyeCloseCompression * (1 - eyeOpenness));
            }
            if (definition.Fade is FadeMode.ClosedEye or FadeMode.AlternateClosedEye && eye is not null)
            {
                y -= eyeOpenness * settings.ClosedEyeLiftPixels;
                y += frame[ClosedEyeOffsetY] * settings.ClosedEyeOffsetPixels * faceScale;
                Rotate(ref x, ref y, centerX, centerY, frame[ClosedEyeAngle] * settings.ClosedEyeAngleRadians * (definition.Side == PartSide.Left ? 1 : -1));
            }
            if (role == PartRole.Eyebrow)
            {
                y += (-frame[EyebrowHeight] * settings.EyebrowLiftPixels + (1 - eyeOpenness) * settings.EyebrowBlinkPixels) * faceScale;
                double angle = (definition.Side == PartSide.Left ? frame[LeftEyebrowAngle] + frame[SymmetricEyebrowAngle] : frame[RightEyebrowAngle] - frame[SymmetricEyebrowAngle]) * settings.EyebrowAngleRadians;
                Rotate(ref x, ref y, centerX, centerY, angle);
            }
            if (definition.Fade == FadeMode.OpenMouth)
            {
                y = anchors.Mouth.MinimumY + (y - anchors.Mouth.MinimumY) * (settings.MouthMinimumHeight + settings.MouthOpeningHeight * mouthOpenness);
                double mouthCurvature = Pow(Abs(x - anchors.Mouth.CenterX) / (mouthHalfWidth + settings.MouthCurvePaddingPixels), settings.MouthCurveExponent);
                y -= frame[MouthShape] * settings.MouthCurvePixels * faceScale * (mouthCurvature - settings.MouthCurveOffset);
            }
            if (definition.Fade == FadeMode.ClosedMouth)
            {
                y += frame[ClosedMouthOffsetY] * settings.ClosedMouthOffsetPixels * faceScale;
                Rotate(ref x, ref y, anchors.Mouth.CenterX, anchors.Mouth.CenterY, frame[ClosedMouthAngle] * settings.ClosedMouthAngleRadians);
            }
            if (role == PartRole.Face && y > anchors.Mouth.CenterY)
                y += mouthOpenness * settings.JawOpenPixels * faceScale * Smooth((y - anchors.Mouth.CenterY) / (anchors.Face.MaximumY - anchors.Mouth.CenterY));
        }

        public void ApplyHead(ref double x, ref double y)
        {
            double headWeight = head ? 1 : definition.Group == PartGroup.Body ? settings.BodyHeadInfluence : 0;
            if (role == PartRole.Neck)
                headWeight = settings.NeckHeadInfluence * Smooth((anchors.NeckBottomY - y) / Max(1, anchors.NeckBottomY - anchors.NeckTopY));
            if (headWeight > 0)
            {
                double rx = x - neckPivot.CenterX, ry = y - neckPivot.CenterY, rx2 = rx * headCosine - ry * headSine, ry2 = rx * headSine + ry * headCosine;
                x += (rx2 - rx) * headWeight;
                y += (ry2 - ry) * headWeight;
                double depth = part.Depth;
                x += headWeight * faceScale * (frame[HeadYaw] * (settings.HeadYawPixels + settings.HeadYawDepthPixels * (depth - 1)) + frame[HeadYaw] * (neckPivot.CenterY - y) * settings.HeadYawShear);
                y += headWeight * faceScale * (-frame[HeadPitch] * (settings.HeadPitchPixels + settings.HeadPitchDepthPixels * (depth - 1)) - frame[HeadPitch] * (depth - 1) * (y - anchors.Face.CenterY) * settings.HeadPitchShear);
            }
        }

        public void ApplyBody(ref double x, ref double y)
        {
            y -= (definition.Group == PartGroup.Body ? frame.Breath * settings.BodyBreathPixels : frame.BreathHead * settings.HeadBreathPixels) * faceScale;
            if (role == PartRole.UpperClothing && y < chestY)
                y -= frame.Breath * settings.ChestBreathPixels * faceScale * Smooth((chestY - y) / (chestRadiusY * settings.ChestBreathFalloff));
            if (role == PartRole.UpperClothing)
            {
                x = neckPivot.CenterX + (x - neckPivot.CenterX) * (1 + frame.Breath * settings.ChestBreathExpansion);
                double gx = (x - chestX) / chestRadiusX, gy = (y - (chestY + frame[ChestBounceOffsetY] * settings.ChestOffsetPixels * faceScale)) / chestRadiusY;
                y += chestDisplacement * frame[ChestBounceAmplitude] * Exp(-gx * gx - gy * gy);
            }
            if (role == PartRole.ArmClothing)
            {
                double w = Smooth((y - definition.Y) / definition.Height * settings.ArmInfluenceScale);
                y -= frame[ArmLift] * settings.ArmLiftPixels * faceScale * w;
                y += frame[ArmOffsetY] * settings.ArmOffsetPixels * faceScale;
                x += frame[ArmLift] * settings.ArmSidewaysPixels * faceScale * w * (x < neckPivot.CenterX ? 1 : -1);
            }
        }

        public void ApplyHair(ref double x, ref double y, int vertexIndex)
        {
            if (part.Geometry.FringeWeights.Length > 0)
            {
                double fringeOffset = Pow(part.Geometry.StrandPositions[vertexIndex], settings.FringeOffsetExponent) * settings.FringeOffsetPixels * faceScale;
                x += (frame[LeftFringeOffset] * part.Geometry.FringeWeights[vertexIndex * 3] + frame[CenterFringeOffset] * part.Geometry.FringeWeights[vertexIndex * 3 + 1] + frame[RightFringeOffset] * part.Geometry.FringeWeights[vertexIndex * 3 + 2]) * fringeOffset;
            }
            if (strandCount > 0 && hairPhysicsEnabled)
            {
                double u = frontHair ? Min(1, part.Geometry.StrandPositions[vertexIndex] * settings.FringeLengthScale) : part.Geometry.StrandPositions[vertexIndex];
                double amplitude = Pow(u, frontHair ? settings.FringeSwayExponent : settings.HairSwayExponent) * (frontHair ? frame[FringeSwayAmplitude] : frame[HairSwayAmplitude]);
                double softnessBlend = Pow(u, settings.HairSoftnessExponent) * (frontHair ? frame[FringeSoftness] : frame[HairSoftness]), dx = 0;
                for (int s = 0; s < strandCount; s++)
                {
                    double w = part.Geometry.StrandWeights[vertexIndex * strandCount + s];
                    if (w < 0.001)
                        continue;
                    var strand = part.Springs[s];
                    dx += w * (strand.Stiff.Displacement * (1 - softnessBlend) + strand.Soft.Displacement * softnessBlend);
                }
                x += dx * amplitude;
                y += Abs(dx) * amplitude * settings.HairVerticalSway;
            }
        }

        public void RotateBody(Span<float> output)
        {
            if (Abs(bodyRotation) <= 1e-4)
                return;
            for (int index = 0; index < output.Length; index += 2)
            {
                double relativeX = output[index] - bodyPivot.CenterX;
                double relativeY = output[index + 1] - bodyPivot.CenterY;
                output[index] = (float)(bodyPivot.CenterX + relativeX * bodyCosine - relativeY * bodySine);
                output[index + 1] = (float)(bodyPivot.CenterY + relativeX * bodySine + relativeY * bodyCosine);
            }
        }
    }

    private static void Rotate(ref double x, ref double y, double centerX, double centerY, double angle)
    {
        if (angle == 0)
            return;
        double cosine = Cos(angle), sine = Sin(angle), relativeX = x - centerX, relativeY = y - centerY;
        x = centerX + relativeX * cosine - relativeY * sine;
        y = centerY + relativeX * sine + relativeY * cosine;
    }
}
