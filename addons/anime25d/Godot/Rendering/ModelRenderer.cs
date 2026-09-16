using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Anime25D.Runtime;
using Godot;

namespace Anime25D;

/// <summary>Owns geometry submission, masks and material lifetime. No model-specific rules.</summary>
internal sealed partial class ModelRenderer : Node2D
{
    private sealed record LayerDraw(MeshInstance2D Node, ShaderMaterial Material);
    private sealed record LayerView(ArrayMesh Mesh, LayerDraw Color, List<LayerDraw> Masks);
    private readonly List<LayerView> layers = new();
    private readonly Dictionary<string, SubViewport> masks = new(StringComparer.Ordinal);
    private IGodotDeformationBinding? binding;
    private ModelInstance instance = null!;
    private bool released;
    public GeometryBackend Backend { get; private set; }
    public string? FallbackReason { get; private set; }
    public long LastVertexUploadBytes { get; private set; }
    public void Initialize(ModelView view, ModelInstance instance, GeometryBackend requested)
    {
        this.instance = instance;
        bool builtIn = instance.Definition.Plan.Deformers.Count == 0;
        IGodotDeformationFactory? factory = view.Deformation;
        if (factory is null && instance.Definition.Plan.Deformers.Count > 0) factory = new BuiltinGpuFactory();
        bool supports = factory?.Supports(instance.Definition) == true;
        if (!Enum.IsDefined(requested) || (requested == GeometryBackend.Gpu && !builtIn && !supports))
            throw new ArgumentException("Requested GPU deformation is unavailable for sequence: " + string.Join(", ", instance.Definition.Plan.Deformers.Select(d => d.Id)));
        Backend = requested == GeometryBackend.Cpu || (requested == GeometryBackend.Auto && !builtIn && !supports) ? GeometryBackend.Cpu : GeometryBackend.Gpu;
        FallbackReason = requested == GeometryBackend.Auto && Backend == GeometryBackend.Cpu ? "No GPU program supports sequence: " + string.Join(", ", instance.Definition.Plan.Deformers.Select(d => d.Id)) : null;
        instance.EvaluateCpuGeometry = Backend == GeometryBackend.Cpu;
        bool custom = Backend == GeometryBackend.Gpu && supports;
        Shader colorShader = custom ? factory!.ColorShader : GD.Load<Shader>("res://addons/anime25d/Godot/Shaders/layer.gdshader");
        Shader maskShader = custom ? factory!.MaskShader : GD.Load<Shader>("res://addons/anime25d/Godot/Shaders/mask.gdshader");
        if (colorShader is null || maskShader is null) throw new ArgumentException("Missing render shaders.");
        if (custom) binding = factory!.Create(instance);
        foreach (var mask in instance.Definition.Masks)
        {
            var viewport = new SubViewport {
                Name = "Mask" + masks.Count, Size = new Vector2I(instance.Definition.CanvasWidth, instance.Definition.CanvasHeight),
                TransparentBg = true, Disable3D = true, GuiDisableInput = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always, RenderTargetClearMode = SubViewport.ClearMode.Always
            };
            masks.Add(mask.Id, viewport); AddChild(viewport);
        }
        for (int i = 0; i < instance.Definition.Layers.Count; i++)
        {
            var layer = instance.Definition.Layers[i];
            var mesh = CreateMesh(layer.Mesh, Backend);
            var bounds = instance.Definition.Bounds;
            mesh.CustomAabb = new(new Vector3(bounds.X, bounds.Y, -1), new Vector3(bounds.Width, bounds.Height, 2));
            var material = new ShaderMaterial { Shader = colorShader };
            var node = new MeshInstance2D { Name = "Layer" + i, Mesh = mesh, Material = material, Texture = view.Textures[layer.TextureSlot], TextureFilter = TextureFilterEnum.Linear };
            var item = new LayerView(mesh, new(node, material), new());
            layers.Add(item); AddChild(node);
            material.SetShaderParameter("runtime_canvas_size", new Vector2(instance.Definition.CanvasWidth, instance.Definition.CanvasHeight));
            material.SetShaderParameter("runtime_clip", layer.MaskId is not null);
            if (layer.MaskId is { } id)
            {
                material.SetShaderParameter("runtime_mask_texture", masks[id].GetTexture());
                material.SetShaderParameter("runtime_receiver_threshold", instance.Definition.Masks.First(m => m.Id == id).ReceiverThreshold);
            }
            binding?.ConfigureLayer(i, material, ModelRenderPass.Color);
            foreach (var mask in instance.Definition.Masks.Where(m => m.Sources.Contains(layer.Id)))
            {
                var maskMaterial = new ShaderMaterial { Shader = maskShader };
                var maskNode = new MeshInstance2D { Mesh = mesh, Material = maskMaterial, Texture = view.Textures[layer.TextureSlot], TextureFilter = TextureFilterEnum.Linear };
                item.Masks.Add(new(maskNode, maskMaterial)); masks[mask.Id].AddChild(maskNode);
                maskMaterial.SetShaderParameter("runtime_source_threshold", mask.SourceThreshold);
                binding?.ConfigureLayer(i, maskMaterial, ModelRenderPass.Mask);
            }
        }
    }
    private static ArrayMesh CreateMesh(MeshDefinition definition, GeometryBackend backend)
    {
        var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        var vertices = new Vector2[definition.VertexCount]; var uv = new Vector2[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] = new(definition.RestPositions[i * 2], definition.RestPositions[i * 2 + 1]);
            uv[i] = new(definition.UV[i * 2], definition.UV[i * 2 + 1]);
        }
        arrays[(int)Mesh.ArrayType.Vertex] = vertices; arrays[(int)Mesh.ArrayType.TexUV] = uv; arrays[(int)Mesh.ArrayType.Index] = definition.Triangles.ToArray();
        var mesh = new ArrayMesh(); var flags = Mesh.ArrayFormat.FlagUse2DVertices;
        if (backend == GeometryBackend.Cpu) flags |= Mesh.ArrayFormat.FlagUseDynamicUpdate;
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, flags: flags); return mesh;
    }
    public void Submit()
    {
        if (instance.Faulted) return;
        LastVertexUploadBytes = 0;
        var frame = instance.Frame;
        binding?.UploadFrame(frame);
        // Dense ranks preserve arbitrary authored order without exceeding Godot ZIndex limits.
        var order = Enumerable.Range(0, layers.Count).OrderBy(i => frame.Layers[i].DrawOrder).ThenBy(i => i).ToArray();
        for (int rank = 0; rank < order.Length; rank++) layers[order[rank]].Color.Node.ZIndex = rank;
        for (int i = 0; i < layers.Count; i++)
        {
            var view = layers[i]; var layer = frame.Layers[i];
            Apply(view.Color, layer);
            foreach (var mask in view.Masks) Apply(mask, layer);
            if (!layer.Visible) continue;
            if (Backend == GeometryBackend.Cpu)
            {
                if (!frame.HasCpuGeometry) throw new InvalidOperationException("CPU renderer requires evaluated vertices.");
                view.Mesh.SurfaceUpdateVertexRegion(0, 0, MemoryMarshal.AsBytes(layer.Positions));
                LastVertexUploadBytes += layer.Positions.Length * sizeof(float);
            }
        }
    }
    private static void Apply(LayerDraw draw, LayerFrameView layer)
    {
        draw.Node.Visible = layer.Visible;
        draw.Material.SetShaderParameter("runtime_layer_alpha", layer.Opacity);
        var t = layer.Transform;
        draw.Material.SetShaderParameter("runtime_transform_x", new Vector3(t.M11, t.M21, t.M31));
        draw.Material.SetShaderParameter("runtime_transform_y", new Vector3(t.M12, t.M22, t.M32));
    }
    public void Release()
    {
        if (released) return; released = true;
        foreach (var layer in layers)
        {
            foreach (var draw in layer.Masks.Append(layer.Color))
            {
                draw.Node.Mesh = null; draw.Node.Material = null; draw.Material.Dispose();
            }
            layer.Mesh.Dispose();
        }
        binding?.Dispose(); binding = null; layers.Clear();
    }
    public override void _ExitTree() => Release();
}
