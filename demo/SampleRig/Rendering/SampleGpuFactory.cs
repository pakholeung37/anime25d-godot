using System;
using System.Collections.Generic;
using Anime25D;
using Anime25D.Core;
using Anime25D.Sample.Core;
using Godot;

namespace Anime25D.Sample.Rendering;

/// <summary>Only sample shader ABI and buffers. Generic renderer owns all draws and masks.</summary>
public sealed class SampleGpuFactory : IGodotDeformationFactory
{
    public Shader ColorShader => GD.Load<Shader>("res://demo/SampleRig/Shaders/part.gdshader");
    public Shader MaskShader => GD.Load<Shader>("res://demo/SampleRig/Shaders/eye_mask.gdshader");
    public bool Supports(IModelBehavior behavior) => behavior is SampleBehavior;
    public IGodotDeformationBinding Create(ModelInstance instance) => new Binding((SampleBehavior)instance.Behavior);
    private sealed class Binding(SampleBehavior behavior) : IGodotDeformationBinding
    {
        private readonly GpuPoseBuffer pose = new();
        private readonly List<GpuDeformationBinding> bindings = new();
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
