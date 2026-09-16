using System;
using System.Collections.Generic;
using System.Linq;
using Anime25D.Runtime;
using Godot;
namespace Anime25D;
/// <summary>GPU program for ordered affine/vertex-offset sequences, up to 16 operations per layer.</summary>
public sealed class BuiltinGpuFactory : IGodotDeformationFactory
{
    public Shader ColorShader => GD.Load<Shader>("res://addons/anime25d/Godot/Shaders/composed_layer.gdshader");
    public Shader MaskShader => GD.Load<Shader>("res://addons/anime25d/Godot/Shaders/composed_mask.gdshader");
    public bool Supports(ModelDefinition model) => model.Plan.Deformers.All(d => d is AffineDeformer or VertexOffsetDeformer) && model.Layers.All(l => l.Mesh.VertexCount <= 16384 && model.Plan.Deformers.Count(d => d.Layers.Contains(l.Id)) <= 16);
    public IGodotDeformationBinding Create(ModelInstance instance) => new Binding(instance.Definition);
    private sealed class Binding(ModelDefinition model) : IGodotDeformationBinding
    {
        private readonly List<(ShaderMaterial Material, DeformerDefinition[] Operations)> materials = new();
        private readonly List<FloatTexture> textures = new();
        public void ConfigureLayer(int index, ShaderMaterial material, ModelRenderPass pass)
        {
            var layer = model.Layers[index]; var operations = model.Plan.Deformers.Where(d => d.Layers.Contains(layer.Id)).ToArray();
            var kinds = new int[16]; var matrices = new Vector4[16]; var translations = new Vector2[16];
            int width = layer.Mesh.VertexCount;
            var texture = new FloatTexture(width, 16); textures.Add(texture); var pixels = new float[width * 16 * 4];
            for (int i = 0; i < operations.Length; i++)
                if (operations[i] is AffineDeformer affine)
                { var m = affine.Transform; kinds[i] = 0; matrices[i] = new(m.M11, m.M12, m.M21, m.M22); translations[i] = new(m.M31, m.M32); }
                else if (operations[i] is VertexOffsetDeformer offsets)
                { kinds[i] = 1; for (int v = 0; v < width; v++) { pixels[(i * width + v) * 4] = offsets.Offsets[v * 2]; pixels[(i * width + v) * 4 + 1] = offsets.Offsets[v * 2 + 1]; } }
            texture.Upload(pixels);
            material.SetShaderParameter("compose_count", operations.Length); material.SetShaderParameter("compose_kinds", kinds);
            material.SetShaderParameter("compose_matrices", matrices); material.SetShaderParameter("compose_translations", translations);
            material.SetShaderParameter("compose_offsets", texture.Texture); materials.Add((material, operations));
        }
        public void UploadFrame(ModelFrame frame)
        {
            foreach (var (material, operations) in materials)
            {
                var weights = new float[16]; for (int i = 0; i < operations.Length; i++) if (operations[i] is VertexOffsetDeformer offset) weights[i] = (float)frame.Pose[offset.Parameter];
                material.SetShaderParameter("compose_weights", weights);
            }
        }
        public void Dispose() { foreach (var texture in textures) texture.Dispose(); textures.Clear(); materials.Clear(); }
    }
}
