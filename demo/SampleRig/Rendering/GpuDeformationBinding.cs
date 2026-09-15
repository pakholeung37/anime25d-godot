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
    private readonly PartState part;
    private readonly ShaderMaterial[] materials;
    private readonly Vector2[] displacements = new Vector2[6];
    private readonly ImageTexture weights;

    public GpuDeformationBinding(PartState part, SampleBehavior simulation, GpuPoseBuffer pose, params ShaderMaterial[] materials)
    {
        this.part = part;
        this.materials = materials;
        weights = CreateWeights(part);
        foreach (var material in materials)
            BindStatic(material, simulation, pose);
    }

    private void BindStatic(ShaderMaterial material, SampleBehavior simulation, GpuPoseBuffer pose)
    {
        var definition = part.Definition;
        var anchors = simulation.Definition.Anchors;
        material.SetShaderParameter("gpu_deformation", true);
        material.SetShaderParameter("pose_texture", pose.Texture);
        material.SetShaderParameter("weight_texture", weights);
        material.SetShaderParameter("part_role", (int)definition.Role);
        material.SetShaderParameter("part_group", (int)definition.Group);
        material.SetShaderParameter("part_side", (int)definition.Side);
        material.SetShaderParameter("fade_mode", (int)definition.Fade);
        material.SetShaderParameter("strand_count", part.Springs.Length);
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

    public void Update()
    {
        for (int index = 0; index < part.Springs.Length; index++)
            displacements[index] = new Vector2((float)part.Springs[index].Stiff.Displacement, (float)part.Springs[index].Soft.Displacement);
        foreach (var material in materials)
        {
            material.SetShaderParameter(DepthUniform, part.Depth);
            if (part.Springs.Length > 0)
                material.SetShaderParameter(SpringUniform, displacements);
        }
    }

    private static Vector4 Rectangle(Anchor anchor) => new((float)anchor.MinimumX, (float)anchor.MinimumY, (float)anchor.MaximumX, (float)anchor.MaximumY);
    private static Vector2 Center(Anchor anchor) => new((float)anchor.CenterX, (float)anchor.CenterY);

    private static ImageTexture CreateWeights(PartState part)
    {
        const int width = 256, texelsPerVertex = 3;
        int height = Math.Max(1, (part.Geometry.VertexCount * texelsPerVertex + width - 1) / width);
        var values = new float[width * height * 4];
        for (int vertex = 0; vertex < part.Geometry.VertexCount; vertex++)
        {
            int offset = vertex * texelsPerVertex * 4;
            for (int strand = 0; strand < part.Springs.Length; strand++)
                values[offset + strand] = part.Geometry.StrandWeights[vertex * part.Springs.Length + strand];
            if (part.Geometry.StrandPositions.Length > 0)
                values[offset + 6] = part.Geometry.StrandPositions[vertex];
            if (part.Geometry.FringeWeights.Length > 0)
                for (int zone = 0; zone < 3; zone++)
                    values[offset + 8 + zone] = part.Geometry.FringeWeights[vertex * 3 + zone];
        }
        using var image = Image.CreateFromData(width, height, false, Image.Format.Rgbaf, MemoryMarshal.AsBytes(values.AsSpan()).ToArray());
        return ImageTexture.CreateFromImage(image);
    }

    public void Dispose() => weights.Dispose();
}
