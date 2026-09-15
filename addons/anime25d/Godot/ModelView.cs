using System;
using System.Collections.Generic;
using System.Linq;
using Anime25D.Core;
using Godot;

namespace Anime25D;

public enum GeometryBackend { Auto, Cpu, Gpu }
public enum ModelRenderPass { Color, Mask }
public interface IGodotDeformationFactory
{
    Shader ColorShader { get; }
    Shader MaskShader { get; }
    bool Supports(IModelBehavior behavior);
    IGodotDeformationBinding Create(ModelInstance instance);
}
public interface IGodotDeformationBinding : IDisposable
{
    void ConfigureLayer(int layerIndex, ShaderMaterial material, ModelRenderPass pass);
    void UploadFrame(ModelFrame frame);
}
public sealed class ModelView
{
    public ModelDefinition Definition { get; }
    public IReadOnlyList<Texture2D> Textures { get; }
    public IGodotDeformationFactory? Deformation { get; }
    public ModelView(ModelDefinition definition, IEnumerable<Texture2D> textures, IGodotDeformationFactory? deformation = null)
    {
        Definition = definition; Textures = Array.AsReadOnly(textures.ToArray()); Deformation = deformation;
        foreach (var layer in definition.Layers)
            if (layer.TextureSlot >= Textures.Count || Textures[layer.TextureSlot] is not { } texture || !GodotObject.IsInstanceValid(texture) || texture.GetWidth() < 1 || texture.GetHeight() < 1)
                throw new ArgumentException($"Missing texture slot for {layer.Id}.");
    }
}
