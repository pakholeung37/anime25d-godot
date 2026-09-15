using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Anime25D.Core;
using Godot;

namespace Anime25D.Rendering;

/// <summary>Owns Godot meshes, materials and eye masks. No motion or rigging rules live here.</summary>
internal sealed partial class RigRenderer : Node2D
{
    private sealed record PartView(PartState State, ArrayMesh Mesh, MeshInstance2D Node,
        ShaderMaterial Material, MeshInstance2D? Mask, GpuDeformationBinding? Binding);
    private static readonly StringName LayerAlphaUniform = new("layer_alpha");
    private readonly List<PartView> views = [];
    private RigSimulation simulation = null!;
    private Node2D content = null!;
    private SubViewport leftMask = null!, rightMask = null!;
    private GpuPoseBuffer? poseBuffer;
    public DeformationBackend Backend { get; private set; }
    public long LastVertexUploadBytes { get; private set; }

    public void Initialize(AnimeRigModel model, RigSimulation simulation, DeformationBackend backend)
    {
        this.simulation = simulation;
        Backend = backend;
        content = new Node2D { Name = "Parts" };
        AddChild(content);
        var size = new Vector2I(simulation.Definition.Canvas.Width, simulation.Definition.Canvas.Height);
        leftMask = CreateMask("LeftEyeMask", size);
        rightMask = CreateMask("RightEyeMask", size);
        if (backend == DeformationBackend.Gpu)
            poseBuffer = new();
        var shader = GD.Load<Shader>("res://addons/anime25d/Shaders/part.gdshader");
        var maskShader = GD.Load<Shader>("res://addons/anime25d/Shaders/eye_mask.gdshader");
        foreach (var part in simulation.Parts)
        {
            var mesh = CreateMesh(part, backend);
            mesh.CustomAabb = new Aabb(new Vector3(-size.X, -size.Y, -1), new Vector3(size.X * 3, size.Y * 3, 2));
            var material = new ShaderMaterial { Shader = shader };
            material.SetShaderParameter("canvas_size", (Vector2)size);
            bool clipped = part.Definition.Role == PartRole.Iris && part.Definition.Side != PartSide.None;
            material.SetShaderParameter("clip_eye", clipped);
            if (clipped)
                material.SetShaderParameter("eye_mask", EyeMask(part.Definition.Side).GetTexture());
            var texture = model.Textures[part.Definition.TextureIndex];
            var node = new MeshInstance2D
            {
                Name = "Part" + part.Definition.InitialDrawOrder,
                Mesh = mesh,
                Texture = texture,
                Material = material,
                TextureFilter = TextureFilterEnum.Linear
            };
            content.AddChild(node);
            MeshInstance2D? mask = null;
            ShaderMaterial? maskMaterial = null;
            if (part.Definition.Role == PartRole.EyeWhite && part.Definition.Side != PartSide.None)
            {
                maskMaterial = new ShaderMaterial { Shader = maskShader };
                mask = new MeshInstance2D { Mesh = mesh, Texture = texture, Material = maskMaterial, TextureFilter = TextureFilterEnum.Linear };
                EyeMask(part.Definition.Side).AddChild(mask);
            }
            GpuDeformationBinding? binding = poseBuffer is null ? null
                : new GpuDeformationBinding(part, simulation, poseBuffer, maskMaterial is null ? [material] : [material, maskMaterial]);
            views.Add(new(part, mesh, node, material, mask, binding));
        }
    }

    private SubViewport EyeMask(PartSide side) => side == PartSide.Left ? leftMask : rightMask;

    private SubViewport CreateMask(string name, Vector2I size)
    {
        var viewport = new SubViewport
        {
            Name = name,
            Size = size,
            TransparentBg = true,
            Disable3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            RenderTargetClearMode = SubViewport.ClearMode.Always,
            GuiDisableInput = true
        };
        AddChild(viewport);
        return viewport;
    }

    private static ArrayMesh CreateMesh(PartState part, DeformationBackend backend)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        var vertices = new Vector2[part.Geometry.VertexCount];
        var coordinates = new Vector2[part.Geometry.VertexCount];
        for (int index = 0; index < vertices.Length; index++)
        {
            vertices[index] = new Vector2(part.Geometry.RestPositions[index * 2], part.Geometry.RestPositions[index * 2 + 1]);
            coordinates[index] = new Vector2(part.Geometry.TextureCoordinates[index * 2], part.Geometry.TextureCoordinates[index * 2 + 1]);
        }
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.TexUV] = coordinates;
        arrays[(int)Mesh.ArrayType.Index] = part.Geometry.TriangleIndices;
        var mesh = new ArrayMesh();
        var flags = Mesh.ArrayFormat.FlagUse2DVertices;
        if (backend == DeformationBackend.CpuReference)
            flags |= Mesh.ArrayFormat.FlagUseDynamicUpdate;
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, flags: flags);
        return mesh;
    }

    public void UploadFrame()
    {
        LastVertexUploadBytes = 0;
        poseBuffer?.Update(simulation);
        foreach (var view in views)
        {
            var part = view.State;
            bool active = part.Alpha >= LayerVisibility.RenderThreshold;
            view.Node.Visible = active;
            if (view.Mask is not null)
                view.Mask.Visible = active;
            view.Node.ZIndex = part.DrawOrder;
            if (!active)
                continue;
            view.Material.SetShaderParameter(LayerAlphaUniform, part.Alpha);
            if (view.Binding is not null)
                view.Binding.Update();
            else
            {
                view.Mesh.SurfaceUpdateVertexRegion(0, 0, MemoryMarshal.AsBytes(part.Positions.AsSpan()));
                LastVertexUploadBytes += part.Positions.Length * sizeof(float);
            }
        }
    }

    public override void _ExitTree()
    {
        foreach (var view in views)
        {
            view.Node.Mesh = null;
            view.Node.Material = null;
            if (view.Mask is not null)
            {
                view.Mask.Mesh = null;
                var material = view.Mask.Material;
                view.Mask.Material = null;
                material?.Dispose();
            }
            view.Material.Dispose();
            view.Mesh.Dispose();
            view.Binding?.Dispose();
        }
        views.Clear();
        poseBuffer?.Dispose();
        poseBuffer = null;
    }
}
