using System;
using System.Collections.Generic;
using System.Linq;
using Anime25D;
using Anime25D.Runtime;
using Anime25D.Sample.Core;
using Godot;

namespace Anime25D.Sample.Rendering;

/// <summary>Only sample shader ABI and buffers. Generic renderer owns all draws and masks.</summary>
public sealed class SampleGpuFactory : IGodotDeformationFactory
{
    public Shader ColorShader => GD.Load<Shader>("res://demo/SampleRig/Shaders/part.gdshader");
    public Shader MaskShader => GD.Load<Shader>("res://demo/SampleRig/Shaders/eye_mask.gdshader");
    public bool Supports(ModelDefinition model) => model.Plan.Deformers.Count == 1 && model.Plan.Deformers[0] is SampleWarp warp && warp.Parts.All(p => (p.Definition.Strands?.Length ?? 0) <= 6);
    public IGodotDeformationBinding Create(ModelInstance instance) => new Binding(instance.Definition);
    private sealed class Binding : IGodotDeformationBinding
    {
        private readonly FrameTextureBinding pose;
        private readonly SampleWarp behavior;
        public Binding(ModelDefinition model) { behavior = (SampleWarp)model.Plan.Deformers[0]; pose = SampleGpuLayout.Create(model); }
        private readonly List<GpuDeformationBinding> bindings = new();
        public void ConfigureLayer(int layerIndex, ShaderMaterial material, ModelRenderPass pass) =>
            bindings.Add(new(behavior.Parts[layerIndex], behavior, pose, layerIndex, material));
        public void UploadFrame(ModelFrame frame)
        {
            pose.Upload(frame);
            foreach (var binding in bindings) binding.Update(frame);
        }
        public void Dispose() { foreach (var binding in bindings) binding.Dispose(); bindings.Clear(); pose.Dispose(); }
    }
}
