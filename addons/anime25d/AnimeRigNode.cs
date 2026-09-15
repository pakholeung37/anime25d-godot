using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Anime25D.Core;
using Godot;

namespace Anime25D;

[GlobalClass]
public partial class AnimeRigNode : Node2D
{
    [Export] public AnimeRigModel? Model { get; set; }
    [Export] public bool Playing { get; set; } = true;
    [Export] public bool AutomaticProcessing { get; set; } = true;
    public RigSimulation? Simulation { get; private set; }

    private sealed record PartView(PartState State, ArrayMesh Mesh, MeshInstance2D Node, ShaderMaterial Material, MeshInstance2D? Mask);
    private readonly List<PartView> views = [];
    private Node2D? content;
    private SubViewport? leftMask, rightMask;
    private static readonly string ShaderPath = "res://addons/anime25d/Shaders/";

    public override void _Ready()
    {
        if (Model is not null && Simulation is null) LoadModel(Model);
    }
    public void LoadModel(AnimeRigModel model, Func<double>? randomSource = null)
    {
        // Validate before touching the current instance, including its GPU resources.
        var simulation = new RigSimulation(model.ReadDefinition(), randomSource);
        ClearModel(); Model = model; Simulation = simulation;
        content = new Node2D { Name = "Parts" }; AddChild(content);
        var size = new Vector2I(simulation.Definition.Canvas.W, simulation.Definition.Canvas.H);
        leftMask = CreateMask("LeftEyeMask", size); rightMask = CreateMask("RightEyeMask", size);
        var shader = GD.Load<Shader>(ShaderPath + "part.gdshader");
        var maskShader = GD.Load<Shader>(ShaderPath + "eye_mask.gdshader");
        foreach (var part in simulation.Parts)
        {
            var mesh = CreateMesh(part);
            // Generous bounds include displaced hair and face vertices when the base rectangle leaves the screen.
            mesh.CustomAabb = new Aabb(new Vector3(-size.X, -size.Y, -1), new Vector3(size.X * 3, size.Y * 3, 2));
            var material = new ShaderMaterial { Shader = shader };
            material.SetShaderParameter("canvas_size", (Vector2)size);
            bool clipped = part.BaseName == "irides" && part.Definition.Side is "L" or "R";
            material.SetShaderParameter("clip_eye", clipped);
            if (clipped) material.SetShaderParameter("eye_mask", (part.Definition.Side == "L" ? leftMask : rightMask).GetTexture());
            var texture = model.Textures[part.Definition.Texture];
            var node = new MeshInstance2D { Name = "Part" + part.Definition.Z, Mesh = mesh, Texture = texture, Material = material, TextureFilter = TextureFilterEnum.Linear };
            content.AddChild(node);
            MeshInstance2D? mask = null;
            if (part.BaseName == "eyewhite" && part.Definition.Side is "L" or "R")
            {
                mask = new MeshInstance2D { Mesh = mesh, Texture = texture, Material = new ShaderMaterial { Shader = maskShader }, TextureFilter = TextureFilterEnum.Linear };
                (part.Definition.Side == "L" ? leftMask : rightMask).AddChild(mask);
            }
            views.Add(new PartView(part, mesh, node, material, mask));
        }
        simulation.Step(0); UploadFrame();
    }
    private SubViewport CreateMask(string name, Vector2I size)
    {
        var viewport = new SubViewport
        {
            Name = name, Size = size, TransparentBg = true, Disable3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            RenderTargetClearMode = SubViewport.ClearMode.Always,
            GuiDisableInput = true
        };
        AddChild(viewport); return viewport;
    }
    private static ArrayMesh CreateMesh(PartState part)
    {
        var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        var vertices = new Vector2[part.VertexCount]; var uvs = new Vector2[part.VertexCount];
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] = new Vector2(part.Positions[i * 2], part.Positions[i * 2 + 1]);
            uvs[i] = new Vector2(part.Uv[i * 2], part.Uv[i * 2 + 1]);
        }
        arrays[(int)Mesh.ArrayType.Vertex] = vertices; arrays[(int)Mesh.ArrayType.TexUV] = uvs; arrays[(int)Mesh.ArrayType.Index] = part.Indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, flags: Mesh.ArrayFormat.FlagUse2DVertices | Mesh.ArrayFormat.FlagUseDynamicUpdate);
        return mesh;
    }
    public override void _Process(double delta)
    {
        if (!AutomaticProcessing || Simulation is null) return;
        if (Playing)
        {
            if (Simulation.Auto.Mouse)
            {
                var p = GetLocalMousePosition(); var size = Simulation.Definition.Canvas;
                Simulation.MouseInside = p.X >= 0 && p.Y >= 0 && p.X <= size.W && p.Y <= size.H;
                Simulation.MouseX = p.X / size.W * 2 - 1; Simulation.MouseY = p.Y / size.H * 2 - 1;
            }
            Simulation.Step(delta);
        }
        else Simulation.UpdateGeometry();
        UploadFrame();
    }
    public void Advance(double delta)
    {
        if (Simulation is null) return;
        Simulation.Step(delta); UploadFrame();
    }
    public void RefreshPose() { Simulation?.UpdateGeometry(); UploadFrame(); }
    public void SetParameter(string name, double value, bool immediate = false)
    {
        if (!Enum.TryParse<Parameter>(name, out var key) || !Enum.IsDefined(key)) throw new ArgumentException($"Unknown parameter: {name}");
        Simulation?.SetParameter(key, value, immediate || !Playing);
    }
    public void SetPreset(string? name) => Simulation?.SetPreset(name, !Playing);
    private void UploadFrame()
    {
        foreach (var view in views)
        {
            var part = view.State; bool active = part.Alpha >= 0.004;
            view.Node.Visible = active; if (view.Mask is not null) view.Mask.Visible = active;
            view.Node.ZIndex = part.DrawOrder;
            if (!active) continue;
            view.Material.SetShaderParameter("layer_alpha", part.Alpha);
            // Godot 4.7 exposes a span overload: no per-frame managed vertex byte-array allocation.
            view.Mesh.SurfaceUpdateVertexRegion(0, 0, MemoryMarshal.AsBytes(part.Positions.AsSpan()));
        }
    }
    public void ClearModel()
    {
        foreach (var view in views)
        {
            view.Node.Mesh = null;
            if (view.Mask is not null) { view.Mask.Mesh = null; var material = view.Mask.Material; view.Mask.Material = null; material?.Dispose(); }
            view.Node.Material = null; view.Material.Dispose(); view.Mesh.Dispose();
        }
        views.Clear();
        content?.Free(); leftMask?.Free(); rightMask?.Free();
        content = null; leftMask = null; rightMask = null; Simulation = null;
    }
    public override void _ExitTree() => ClearModel();
}
