using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Anime25D;
using Anime25D.Runtime;
using Godot;

// Tests real pixels using only addon-owned meshes, renderer, masks and model nodes.
public partial class Consumer : Node
{
    private int assertions;
    private void Require(bool pass, string label) { assertions++; if (!pass) throw new Exception(label); }
    private async Task DrawFrames()
    {
        for (int i = 0; i < 4; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
    }
    private (SubViewport View, AnimeModelNode Actor) Create()
    {
        var viewport = new SubViewport { Size = new Vector2I(128, 128), TransparentBg = true, Disable3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        AddChild(viewport); var actor = new AnimeModelNode { AutomaticProcessing = false }; viewport.AddChild(actor); return (viewport, actor);
    }
    private static ImageTexture Texture(Color color)
    {
        using var image = Image.CreateEmpty(4, 4, false, Image.Format.Rgba8); image.Fill(color); return ImageTexture.CreateFromImage(image);
    }
    public override async void _Ready()
    {
        try
        {
            using var white = Texture(Colors.White); using var red = Texture(Colors.Red);
            var animation = new AnimationModel(new[] { new ParameterDefinition("warp", 0, -20, 20) }, new Dictionary<string, MotionDefinition> {
                ["move"] = new(2, new[] { new MotionTrack("warp", MotionCurve.Linear((0, 0), (2, 8))) }, loop: true, fadeIn: 0)
            });
            var layerDefinitions = new List<LayerDefinition>(); var masks = new List<MaskDefinition>();
            for (int i = 0; i < 3; i++)
            {
                layerDefinitions.Add(new("source" + i, GridMeshBuilder.Create(16, 8 + i * 36, 20, 20), DrawOrder: i));
                layerDefinitions.Add(new("target" + i, GridMeshBuilder.Create(4, 4 + i * 36, 70, 28), TextureSlot: 1, DrawOrder: i + 10, MaskId: "group" + i));
                masks.Add(new("group" + i, new[] { "source" + i }));
            }
            layerDefinitions.Add(new("extra", GridMeshBuilder.Create(50, 8, 10, 20), DrawOrder: 0));
            masks[0] = new("group0", new[] { "source0", "extra" });
            var customDefinition = new ModelDefinition(animation, 128, 128, layerDefinitions, masks, plan: new ModelPlan(deformers: [new WarpBehavior(layerDefinitions.Select(l => l.Id))]));
            var basicDefinition = new ModelDefinition(animation, 128, 128, layerDefinitions, masks,
                plan: new ModelPlan([new("bindings", ModelStage.Layers, d => new LayerBindingComponent(d, d.Layers.Select(l => new LayerParameterBinding("warp", l.Id, LayerProperty.TranslationX))))]));
            var cpu = Create(); var gpu = Create(); var basic = Create();
            using var factory = new WarpGpu();
            var customView = new ModelView(customDefinition, new[] { white, red }, factory);
            cpu.Actor.Load(customView, GeometryBackend.Cpu); gpu.Actor.Load(customView, GeometryBackend.Gpu);
            basic.Actor.Load(new(basicDefinition, new[] { white, red }));
            Require(basic.Actor.ActualBackend == GeometryBackend.Gpu, "Default model requires a custom GPU program.");
            foreach (var actor in new[] { cpu.Actor, gpu.Actor, basic.Actor }) { actor.Instance!.Animation.PlayMotion("move"); actor.Advance(0.5); }
            await DrawFrames();
            using var ci = cpu.View.GetTexture().GetImage(); using var gi = gpu.View.GetTexture().GetImage(); using var bi = basic.View.GetTexture().GetImage();
            Require(ci.GetData().SequenceEqual(gi.GetData()), "Independent custom CPU/GPU deformation pixels differ.");
            Require(ci.GetData().SequenceEqual(bi.GetData()), "Basic layer transform differs from equivalent custom deformation.");
            Require(gi.GetPixel(20, 16).R > 0.9 && gi.GetPixel(20, 16).G < 0.1, "Masked target not rendered.");
            Require(gi.GetPixel(8, 16).A == 0 && gi.GetPixel(40, 16).A == 0, "Target leaked outside mask.");
            Require(gi.GetPixel(55, 16).R > 0.9 && gi.GetPixel(55, 16).G < 0.1, "Mask source union failed.");
            Require(gi.GetPixel(20, 52).R > 0.9 && gi.GetPixel(20, 88).R > 0.9, "Three unrelated masks failed.");
            Require(gpu.Actor.LastVertexUploadBytes == 0 && cpu.Actor.LastVertexUploadBytes > 0, "Wrong geometry submission backend.");
            double time = gpu.Actor.Instance!.Animation.Time; var before = gpu.Actor.Instance.EvaluateCpuSnapshot(); gpu.Actor.RefreshPose();
            Require(gpu.Actor.Instance.Animation.Time == time && before.Positions[0].SequenceEqual(gpu.Actor.Instance.EvaluateCpuSnapshot().Positions[0]), "Refresh or diagnostics advanced state.");
            gpu.Actor.Instance.Layers[0].Visible = false; gpu.Actor.Instance.Layers[6].Visible = false; gpu.Actor.RefreshPose(); await DrawFrames();
            using var hidden = gpu.View.GetTexture().GetImage();
            Require(hidden.GetPixel(20, 16).A == 0 && hidden.GetPixel(55, 16).A == 0, "Empty mask leaked target.");
            using var untouched = cpu.View.GetTexture().GetImage(); Require(untouched.GetData().SequenceEqual(ci.GetData()), "Shared model leaked instance state.");
            var existing = gpu.Actor.Instance;
            bool rejected = false;
            try { gpu.Actor.Load(new(customDefinition, new[] { white, red }), GeometryBackend.Gpu); } catch (ArgumentException) { rejected = true; }
            Require(rejected && ReferenceEquals(existing, gpu.Actor.Instance) && !existing.IsDisposed, "Failed load destroyed old model.");
            // Failure after extension resources are allocated must also clean up the candidate.
            var failing = new FailingGpu(factory);
            rejected = false;
            try { gpu.Actor.Load(new(customDefinition, new[] { white, red }, failing), GeometryBackend.Gpu); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected && failing.Disposals == 1 && ReferenceEquals(existing, gpu.Actor.Instance), "Partial binding leaked or replaced old instance.");
            gpu.Actor.Load(new(customDefinition, new[] { white, red }), GeometryBackend.Auto);
            Require(gpu.Actor.ActualBackend == GeometryBackend.Cpu && existing.IsDisposed, "Auto fallback or old-instance disposal failed.");
            gpu.Actor.Instance!.Animation.PlaybackChanged += e => { if (e.Kind == PlaybackEventKind.Completed) gpu.Actor.ClearModel(); };
            gpu.Actor.Instance.Animation.PlayMotion("move", new(Loop: false)); gpu.Actor.Advance(2);
            Require(gpu.Actor.Instance is null, "Clear from playback callback failed.");
            var oldCpu = cpu.Actor.Instance!; int inputs = 0;
            cpu.Actor.FinalizingPose += _ => inputs++;
            oldCpu.Animation.PlaybackChanged += e => { if (e.Kind == PlaybackEventKind.Completed) cpu.Actor.Load(customView, GeometryBackend.Cpu); };
            oldCpu.Animation.PlayMotion("move", new(Loop: false)); cpu.Actor.Advance(2);
            Require(oldCpu.IsDisposed && !ReferenceEquals(oldCpu, cpu.Actor.Instance), "Reload callback retained old instance.");
            cpu.Actor.Advance(0); Require(inputs == 2, "Node input subscription did not survive callback reload.");
            basic.Actor.ClearModel(); await DrawFrames(); using var empty = basic.View.GetTexture().GetImage();
            Require(empty.GetData().Where((_, i) => i % 4 == 3).All(v => v == 0), "Clear left visible content.");
            for (int i = 0; i < 4; i++) gpu.Actor.Load(customView, GeometryBackend.Gpu);
            // Independent composition: built-in drivers/physics/layer bindings and ordered GPU operations.
            var panel = new LayerDefinition("panel", GridMeshBuilder.Create(8, 8, 20, 20));
            var composed = new ModelDefinition(animation, 128, 128, new[] { panel }, plan: new ModelPlan(
                components: new ComponentDefinition[] {
                    new("wave", ModelStage.BasePose, d => new SineDriver(d, new SineBinding("warp", 1, 2))),
                    new("target", ModelStage.Derived, _ => new ChannelWriter("target", c => new[] { c.Pose["warp"] }), writes: new[] { "target" }),
                    new("spring", ModelStage.Derived, _ => new SpringBank("target", "lag", new[] { new SpringBinding(70, 9) }), reads: new[] { "target" }, writes: new[] { "lag" }),
                    new("alpha", ModelStage.Layers, d => new LayerOpacityBinding(d, "panel", c => Math.Clamp(.5 + c.Channels.Scalar("lag"), 0, 1)), reads: new[] {"lag"})
                }, channels: new[] { new ChannelDefinition("target"), new ChannelDefinition("lag") },
                deformers: new DeformerDefinition[] {
                    new AffineDeformer("translate", new[] { "panel" }, System.Numerics.Matrix3x2.CreateTranslation(3, 0)),
                    new AffineDeformer("scale", new[] { "panel" }, System.Numerics.Matrix3x2.CreateScale(2)),
                    new VertexOffsetDeformer("offset", new[] { "panel" }, "warp", Enumerable.Range(0, panel.Mesh.VertexCount * 2).Select(i => i % 2 == 0 ? 2f : 0f).ToArray())
                }));
            cpu.Actor.Load(new(composed, new[] { white }), GeometryBackend.Cpu);
            gpu.Actor.Load(new(composed, new[] { white }), GeometryBackend.Gpu);
            cpu.Actor.Advance(.2); gpu.Actor.Advance(.2); await DrawFrames();
            using var composedCpu = cpu.View.GetTexture().GetImage(); using var composedGpu = gpu.View.GetTexture().GetImage();
            Require(composedCpu.GetData().SequenceEqual(composedGpu.GetData()), "Built-in ordered CPU/GPU sequence differs.");
            Require(gpu.Actor.LastVertexUploadBytes == 0, "Composed GPU uploaded vertices.");
            Require(cpu.Actor.Instance!.Frame.Channels.Scalar("lag") == gpu.Actor.Instance!.Frame.Channels.Scalar("lag"), "GPU ran different physics.");
            Require(composedGpu.GetPixel(28, 20).A > 0, "Composed model did not render.");
            cpu.View.Free(); gpu.View.Free(); basic.View.Free(); await DrawFrames();
            GD.Print($"PASS: {assertions} independent model/render/mask/lifecycle assertions, with no sample implementation or custom renderer.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
    private sealed class WarpBehavior(IEnumerable<string> layers) : DeformerDefinition("warp", layers)
    {
        public override void Deform(int layer, ModelFrame frame, ReadOnlySpan<float> rest, ReadOnlySpan<float> input, Span<float> output)
        { input.CopyTo(output); for (int i = 0; i < output.Length; i += 2) output[i] += (float)frame.Pose["warp"]; }
    }
    private sealed class WarpGpu : IGodotDeformationFactory, IDisposable
    {
        public Shader ColorShader { get; } = new() { Code = """
            shader_type canvas_item;
            render_mode unshaded, blend_premul_alpha;
            #include "res://addons/anime25d/Godot/Shaders/runtime.gdshaderinc"
            uniform float warp = 0.0;
            varying vec2 position;
            void vertex() { VERTEX.x += warp; VERTEX = runtime_position(VERTEX); position = VERTEX; }
            void fragment() {
                vec2 p = position / runtime_canvas_size;
                if (runtime_clip) {
                    if (any(lessThan(p, vec2(0.0))) || any(greaterThan(p, vec2(1.0)))) discard;
                    if (texture(runtime_mask_texture, p).a < runtime_receiver_threshold) discard;
                }
                COLOR = texture(TEXTURE, UV) * runtime_layer_alpha;
            }
            """ };
        public Shader MaskShader { get; } = new() { Code = """
            shader_type canvas_item;
            render_mode unshaded, blend_disabled;
            #include "res://addons/anime25d/Godot/Shaders/runtime.gdshaderinc"
            uniform float warp = 0.0;
            void vertex() { VERTEX.x += warp; VERTEX = runtime_position(VERTEX); }
            void fragment() { if (texture(TEXTURE, UV).a < runtime_source_threshold) discard; COLOR = vec4(1.0); }
            """ };
        public bool Supports(ModelDefinition model) => model.Plan.Deformers.Count == 1 && model.Plan.Deformers[0] is WarpBehavior;
        public IGodotDeformationBinding Create(ModelInstance instance) => new WarpBinding();
        public void Dispose() { ColorShader.Dispose(); MaskShader.Dispose(); }
    }
    private sealed class WarpBinding : IGodotDeformationBinding
    {
        private readonly List<ShaderMaterial> materials = new();
        public void ConfigureLayer(int layerIndex, ShaderMaterial material, ModelRenderPass pass) => materials.Add(material);
        public void UploadFrame(ModelFrame frame) { foreach (var material in materials) material.SetShaderParameter("warp", frame.Pose["warp"]); }
        public void Dispose() => materials.Clear();
    }
    private sealed class FailingGpu(WarpGpu shaders) : IGodotDeformationFactory
    {
        public int Disposals;
        public Shader ColorShader => shaders.ColorShader;
        public Shader MaskShader => shaders.MaskShader;
        public bool Supports(ModelDefinition model) => true;
        public IGodotDeformationBinding Create(ModelInstance instance) => new Failure(this);
        private sealed class Failure(FailingGpu owner) : IGodotDeformationBinding
        {
            public void ConfigureLayer(int layerIndex, ShaderMaterial material, ModelRenderPass pass) => throw new InvalidOperationException("Injected configuration failure");
            public void UploadFrame(ModelFrame frame) { }
            public void Dispose() => owner.Disposals++;
        }
    }
}
