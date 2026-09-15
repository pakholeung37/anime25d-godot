using System;
using Anime25D;
using Anime25D.Sample.Core;
using Anime25D.Sample.Rendering;
using Godot;

namespace Anime25D.Sample;

public enum DeformationBackend { Gpu, CpuReference }

/// <summary>Sample authoring facade; model evaluation and rendering are owned by AnimeModelNode.</summary>
[GlobalClass]
public partial class AnimeRigNode : AnimeModelNode
{
    [Export] public AnimeRigModel? Model { get; set; }
    [Export] public AnimeRigProfile? ProfileOverride { get; set; }
    [Export] public DeformationBackend Backend { get; set; } = DeformationBackend.Gpu;
    public SampleReferenceAdapter? Simulation { get; private set; }
    public Anime25D.Core.AnimationRuntime? Animation => Instance?.Animation;
    public DeformationBackend? ActiveBackend => ActualBackend is null ? null : ActualBackend == GeometryBackend.Cpu ? DeformationBackend.CpuReference : DeformationBackend.Gpu;
    public event Action<Parameters>? PreparingPose;
    public override void _Ready() { if (Model is not null && Instance is null) LoadModel(Model); }
    public void LoadModel(AnimeRigModel model, Func<double>? randomSource = null)
    {
        if (DeferLifecycle(() => LoadModel(model, randomSource))) return;
        var profile = (ProfileOverride ?? model.Profile)?.ReadConfiguration() ?? new RigProfile();
        var view = new ModelView(SampleModelBuilder.Build(model.ReadDefinition(), profile, model.Animations, randomSource), model.Textures, new SampleGpuFactory());
        var old = Simulation;
        Load(view, Backend == DeformationBackend.CpuReference ? GeometryBackend.Cpu : GeometryBackend.Gpu);
        if (old is not null) old.PreparingPose -= PreparePose;
        Model = model; Simulation = new(Instance!); Simulation.PreparingPose += PreparePose;
        Simulation.SyncReferenceVertices();
    }
    protected override void FrameSubmitted() => Simulation?.SyncReferenceVertices();
    public void SetParameter(Parameter key, double value, bool immediate = false) => Simulation?.SetParameter(key, value, immediate || !Playing);
    public void SetParameter(string name, double value, bool immediate = false) => SetParameter(ParameterNames.Parse(name), value, immediate);
    public void SetPreset(string? name) => Simulation?.SetPreset(name, !Playing);
    private void PreparePose(Parameters parameters) => PreparingPose?.Invoke(parameters);
    protected override void ModelCleared()
    {
        if (Simulation is not null) Simulation.PreparingPose -= PreparePose;
        Simulation = null;
    }
}
