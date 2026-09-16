using System;
using System.Collections.Generic;
using Anime25D.Runtime;
using Godot;

namespace Anime25D;

/// <summary>Complete model lifecycle. Rendering, masks and model clocks are owned here.</summary>
[GlobalClass]
public partial class AnimeModelNode : Node2D
{
    public ModelInstance? Instance { get; private set; }
    [Export] public bool Playing { get; set; } = true;
    [Export] public bool AutomaticProcessing { get; set; } = true;
    public GeometryBackend? ActualBackend => renderer?.Backend;
    public string? BackendFallbackReason => renderer?.FallbackReason;
    public long LastVertexUploadBytes => renderer?.LastVertexUploadBytes ?? 0;
    public event Action<ParameterSet>? FinalizingPose;
    private Action<ParameterSet>? input;
    private ModelRenderer? renderer;
    private bool busy;
    private readonly Queue<Action> pending = new();
    protected bool DeferLifecycle(Action action)
    {
        if (!busy) return false;
        pending.Enqueue(action); return true;
    }
    public void Load(ModelView view, GeometryBackend backend = GeometryBackend.Auto)
    {
        if (busy) { pending.Enqueue(() => Load(view, backend)); return; }
        ModelInstance? candidate = null; ModelRenderer? display = null;
        try
        {
            candidate = new(view.Definition);
            display = new ModelRenderer { Visible = false }; AddChild(display);
            display.Initialize(view, candidate, backend);
            candidate.Refresh(); display.Submit();
        }
        catch
        {
            display?.Release(); display?.Free(); candidate?.Dispose(); throw;
        }
        ClearNow();
        Instance = candidate; renderer = display;
        input = pose => FinalizingPose?.Invoke(pose); candidate.FinalizingPose += input;
        display.Visible = true;
    }
    public override void _Process(double delta)
    {
        if (!AutomaticProcessing || Instance is null) return;
        if (Playing) Advance(delta); else renderer?.Submit();
    }
    public void Advance(double delta)
    {
        if (busy) throw new InvalidOperationException("Recursive model advance.");
        if (Instance is null) return;
        busy = true;
        try { Instance.Advance(delta, false); renderer?.Submit(); FrameSubmitted(); if (!Instance.Animation.Paused) Instance.Animation.DispatchEvents(); }
        finally { busy = false; Drain(); }
    }
    public void RefreshPose()
    {
        if (busy) throw new InvalidOperationException("Recursive model refresh.");
        if (Instance is null) return;
        busy = true;
        try { Instance.Refresh(); renderer?.Submit(); FrameSubmitted(); }
        finally { busy = false; Drain(); }
    }
    public void ClearModel()
    {
        if (busy) { pending.Enqueue(ClearNow); return; }
        ClearNow();
    }
    private void ClearNow()
    {
        if (Instance is not null && input is not null) Instance.FinalizingPose -= input;
        input = null;
        renderer?.Release(); renderer?.Free(); renderer = null;
        Instance?.Dispose(); Instance = null; ModelCleared();
    }
    protected virtual void FrameSubmitted() { }
    protected virtual void ModelCleared() { }
    private void Drain() { while (pending.TryDequeue(out var action)) action(); }
    public override void _ExitTree() => ClearModel();
}
