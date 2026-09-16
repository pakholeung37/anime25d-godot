using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Anime25D.Sample.Core;
using Godot;

namespace Anime25D.Sample.Rendering;

internal sealed class GpuDeformationBinding : IDisposable
{
    private static readonly StringName SpringUniform = new("spring_displacements");
    private static readonly StringName DepthUniform = new("layer_depth");
    private readonly SamplePart part;
    private readonly int layer, springOffset;
    private readonly ShaderMaterial[] materials;
    private readonly Vector2[] displacements = new Vector2[6];
    private readonly Anime25D.FloatTexture weights;

    public GpuDeformationBinding(SamplePart part, SampleWarp simulation, Anime25D.FrameTextureBinding pose, int layer, params ShaderMaterial[] materials)
    {
        this.part = part; this.layer = layer; springOffset = simulation.SpringOffset(layer);
        this.materials = materials;
        weights = CreateWeights(part);
        foreach (var material in materials)
            BindStatic(material, simulation, pose);
    }

    private void BindStatic(ShaderMaterial material, SampleWarp simulation, Anime25D.FrameTextureBinding pose)
    {
        var definition = part.Definition;
        var anchors = simulation.Rig.Anchors;
        material.SetShaderParameter("gpu_deformation", true);
        material.SetShaderParameter("pose_texture", pose.Texture);
        material.SetShaderParameter("weight_texture", weights.Texture);
        material.SetShaderParameter("part_role", (int)definition.Role);
        material.SetShaderParameter("part_group", (int)definition.Group);
        material.SetShaderParameter("part_side", (int)definition.Side);
        material.SetShaderParameter("fade_mode", (int)definition.Fade);
        material.SetShaderParameter("strand_count", (part.Definition.Strands?.Length ?? 0));
        material.SetShaderParameter("face_scale", anchors.FaceScale);
        material.SetShaderParameter("part_rectangle", new Vector4(definition.X, definition.Y, definition.Width, definition.Height));
        material.SetShaderParameter("face_rectangle", Rectangle(anchors.Face));
        material.SetShaderParameter("face_center", Center(anchors.Face));
        material.SetShaderParameter("mouth_rectangle", Rectangle(anchors.Mouth));
        material.SetShaderParameter("mouth_center", Center(anchors.Mouth));
        material.SetShaderParameter("neck_pivot", Center(anchors.NeckPivot));
        material.SetShaderParameter("body_pivot", Center(anchors.BodyPivot));
        material.SetShaderParameter("neck_limits", new Vector2((float)anchors.NeckTopY, (float)anchors.NeckBottomY));
        var eye = definition.Side == PartSide.Left ? anchors.LeftEye : definition.Side == PartSide.Right ? anchors.RightEye : null;
        material.SetShaderParameter("has_eye", eye is not null);
        if (eye is not null)
        {
            material.SetShaderParameter("eye_rectangle", Rectangle(eye));
            material.SetShaderParameter("iris_center", new Vector2((float)eye.IrisCenterX, (float)eye.IrisCenterY));
            material.SetShaderParameter("closed_eye_y", eye.ClosedEyeY);
        }
        // Reflection is confined to model loading; tuning is immutable during playback.
        foreach (var property in typeof(DeformationSettings).GetProperties().Where(property => property.PropertyType == typeof(double)))
        {
            string name = "tuning_" + Regex.Replace(property.Name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant();
            material.SetShaderParameter(name, (double)property.GetValue(simulation.Profile.Deformation)!);
        }
    }

    public void Update(Anime25D.Runtime.ModelFrame frame)
    {
        for (int index = 0; index < (part.Definition.Strands?.Length ?? 0); index++)
            displacements[index] = new Vector2((float)frame.Channels["spring-displacements"][springOffset + index * 2], (float)frame.Channels["spring-displacements"][springOffset + index * 2 + 1]);
        foreach (var material in materials)
        {
            material.SetShaderParameter(DepthUniform, frame.Pose[SampleWarp.DepthParameter(layer)]);
            if ((part.Definition.Strands?.Length ?? 0) > 0)
                material.SetShaderParameter(SpringUniform, displacements);
        }
    }

    private static Vector4 Rectangle(Anchor anchor) => new((float)anchor.MinimumX, (float)anchor.MinimumY, (float)anchor.MaximumX, (float)anchor.MaximumY);
    private static Vector2 Center(Anchor anchor) => new((float)anchor.CenterX, (float)anchor.CenterY);

    private static Anime25D.FloatTexture CreateWeights(SamplePart part)
    {
        const int width = 256, texelsPerVertex = 3;
        int height = Math.Max(1, (part.Geometry.VertexCount * texelsPerVertex + width - 1) / width);
        var values = new float[width * height * 4];
        for (int vertex = 0; vertex < part.Geometry.VertexCount; vertex++)
        {
            int offset = vertex * texelsPerVertex * 4;
            for (int strand = 0; strand < (part.Definition.Strands?.Length ?? 0); strand++)
                values[offset + strand] = part.Geometry.StrandWeights[vertex * (part.Definition.Strands?.Length ?? 0) + strand];
            if (part.Geometry.StrandPositions.Length > 0)
                values[offset + 6] = part.Geometry.StrandPositions[vertex];
            if (part.Geometry.FringeWeights.Length > 0)
                for (int zone = 0; zone < 3; zone++)
                    values[offset + 8 + zone] = part.Geometry.FringeWeights[vertex * 3 + zone];
        }
        var texture = new Anime25D.FloatTexture(width, height); texture.Upload(values); return texture;
    }

    public void Dispose() => weights.Dispose();
}
