using System;
using System.Collections.Generic;
using Anime25D;
using Anime25D.Runtime;
using Anime25D.Sample.Core;
using Godot;

namespace Anime25D.Sample.Rendering;

/// <summary>Only sample shader ABI and buffers. Generic renderer owns all draws and masks.</summary>
public sealed class LegacyGpuFactory : IGodotDeformationFactory
{
    public Shader ColorShader => GD.Load<Shader>("res://demo/SampleRig/Shaders/part.gdshader");
    public Shader MaskShader => GD.Load<Shader>("res://demo/SampleRig/Shaders/eye_mask.gdshader");
    public bool Supports(ModelDefinition model) => model.Plan.Deformers.Count == 1 && model.Plan.Deformers[0].Id == "legacy";
    public IGodotDeformationBinding Create(ModelInstance instance) => new Binding(instance.Component<LegacyPlan.LegacyState>("legacy").Behavior);
    private sealed class Binding(SampleBehavior behavior) : IGodotDeformationBinding
    {
        private readonly LegacyPoseBuffer pose = new();
        private readonly List<LegacyGpuBinding> bindings = new();
        public void ConfigureLayer(int layerIndex, ShaderMaterial material, ModelRenderPass pass) =>
            bindings.Add(new(behavior.Parts[layerIndex], behavior, pose, material));
        public void UploadFrame(ModelFrame frame)
        {
            pose.Update(behavior, frame.Pose);
            foreach (var binding in bindings) binding.Update();
        }
        public void Dispose() { foreach (var binding in bindings) binding.Dispose(); bindings.Clear(); pose.Dispose(); }
    }
}
