using System;
using Anime25D.Core;
using Anime25D.Rendering;
using Godot;

namespace Anime25D;

public enum DeformationBackend
{
    Gpu, CpuReference
}

/// <summary>Godot-facing character lifecycle and playback; simulation and rendering are separate services.</summary>
[GlobalClass]
public partial class AnimeRigNode : Node2D
{
    [Export]
    public AnimeRigModel? Model { get; set; }
    [Export]
    public AnimeRigProfile? ProfileOverride { get; set; }
    [Export] public DeformationBackend Backend { get; set; } = DeformationBackend.Gpu;
    [Export] public bool Playing { get; set; } = true;
    [Export] public bool AutomaticProcessing { get; set; } = true;
    public RigSimulation? Simulation { get; private set; }
    public long LastVertexUploadBytes => renderer?.LastVertexUploadBytes ?? 0;
    public DeformationBackend? ActiveBackend => renderer?.Backend;
    private RigRenderer? renderer;

    public override void _Ready()
    {
        if (Model is not null && Simulation is null)
            LoadModel(Model);
    }

    public void LoadModel(AnimeRigModel model, Func<double>? randomSource = null)
    {
        var profile = (ProfileOverride ?? model.Profile)?.ReadConfiguration() ?? new RigProfile();
        var simulation = new RigSimulation(model.ReadDefinition(), randomSource, profile)
        {
            EvaluateCpuGeometry = Backend == DeformationBackend.CpuReference
        };
        // Validate before releasing the previous model.
        ClearModel();
        Model = model;
        Simulation = simulation;
        renderer = new RigRenderer();
        AddChild(renderer);
        renderer.Initialize(model, simulation, Backend);
        Advance(0);
    }

    public override void _Process(double delta)
    {
        if (!AutomaticProcessing || Simulation is null)
            return;
        if (Playing)
        {
            if (Simulation.AutomaticMotion.Mouse)
                DesktopMouseInput.Update(Simulation, GetWindow());
            Simulation.Step(delta);
        }
        else
            Simulation.UpdateGeometry();
        renderer?.UploadFrame();
    }

    public void Advance(double delta)
    {
        if (Simulation is null)
            return;
        Simulation.Step(delta);
        renderer?.UploadFrame();
    }

    public void RefreshPose()
    {
        Simulation?.UpdateGeometry();
        renderer?.UploadFrame();
    }

    public void SetParameter(Parameter key, double value, bool immediate = false) =>
        Simulation?.SetParameter(key, value, immediate || !Playing);

    public void SetParameter(string name, double value, bool immediate = false) =>
        SetParameter(ParameterNames.Parse(name), value, immediate);

    public void SetPreset(string? name) => Simulation?.SetPreset(name, !Playing);

    public void ClearModel()
    {
        renderer?.Free();
        renderer = null;
        Simulation = null;
    }

    public override void _ExitTree() => ClearModel();
}
