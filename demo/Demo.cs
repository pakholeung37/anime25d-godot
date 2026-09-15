using Anime25D;
using Anime25D.Core;
using Godot;

public partial class Demo : Control
{
    private AnimeRigNode actor = null!;
    private AnimeRigNode? second;
    private Panel stage = null!;
    private Label info = null!;
    private VBoxContainer parameters = null!, layerControls = null!;
    private readonly Dictionary<Parameter, HSlider> sliders = [];
    private readonly Dictionary<string, CheckButton> toggles = [];
    private bool syncing;
    private string sample = "sample-a";

    public override void _Ready()
    {
        if (OS.GetCmdlineUserArgs().Contains("--render-tests")) { AddChild(new RenderChecks()); return; }
        BuildUi(); LoadSample("sample-a");
        if (OS.GetCmdlineUserArgs().Contains("--capture-demo")) CaptureDemo();
    }
    private async void CaptureDemo()
    {
        ulong end = Time.GetTicksMsec() + 2200;
        while (Time.GetTicksMsec() < end) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://artifacts"));
        GetViewport().GetTexture().GetImage().SavePng("res://artifacts/demo.png");
        GetTree().Quit();
    }
    private static Label Text(string text, int size = 14)
    {
        var label = new Label { Text = text }; label.AddThemeFontSizeOverride("font_size", size); return label;
    }
    private static Button Button(string text, Action onPress)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 32) }; button.Pressed += onPress; return button;
    }
    private static StyleBoxFlat Box(Color color, int radius = 12)
    {
        return new StyleBoxFlat { BgColor = color, CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius, CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius, ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 12, ContentMarginBottom = 12 };
    }
    private void BuildUi()
    {
        var margin = new MarginContainer(); margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 20);
        AddChild(margin);
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation", 14); margin.AddChild(root);
        var header = new HBoxContainer(); root.AddChild(header);
        var title = Text("Anime25D", 28); header.AddChild(title);
        var subtitle = Text("   GODOT / C#   ·   RUNTIME LAB", 13); subtitle.Modulate = new Color(0.56f, 0.7f, 0.83f); subtitle.SizeFlagsHorizontal = SizeFlags.ExpandFill; header.AddChild(subtitle);
        header.AddChild(Button("Sample A", () => LoadSample("sample-a")));
        header.AddChild(Button("Sample B", () => LoadSample("sample-b")));
        var columns = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; columns.AddThemeConstantOverride("separation", 18); root.AddChild(columns);
        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; columns.AddChild(left);
        stage = new Panel { SizeFlagsVertical = SizeFlags.ExpandFill, ClipContents = true };
        stage.AddThemeStyleboxOverride("panel", Box(new Color(0.10f, 0.125f, 0.16f)));
        left.AddChild(stage); actor = new AnimeRigNode { Name = "Character" }; stage.AddChild(actor); stage.Resized += Fit;
        info = Text("Loading…", 13); left.AddChild(info);
        var controls = new HFlowContainer(); controls.AddThemeConstantOverride("h_separation", 6); left.AddChild(controls);
        controls.AddChild(Button("Pause / Resume", () => actor.Playing = !actor.Playing));
        controls.AddChild(Button("Reset", () => { actor.Simulation?.ResetParameters(); SyncSliders(); }));
        controls.AddChild(Button("Two instances", ToggleSecond));
        controls.AddChild(Button("Background", () => stage.AddThemeStyleboxOverride("panel", Box(stage.GetThemeStylebox("panel") is StyleBoxFlat b && b.BgColor.R < 0.2 ? new Color(0.78f, 0.80f, 0.82f) : new Color(0.10f, 0.125f, 0.16f)))));
        var automatic = new HFlowContainer(); left.AddChild(automatic);
        foreach (var key in new[] { "Idle", "Blink", "Random", "Talk", "Physics", "Mouse" })
        {
            var toggle = new CheckButton { Text = key, ButtonPressed = key != "Mouse" }; toggles[key] = toggle; automatic.AddChild(toggle);
            toggle.Toggled += value =>
            {
                if (actor.Simulation is not { } sim) return;
                switch (key) { case "Idle": sim.Auto.Idle = value; break; case "Blink": sim.SetBlinkEnabled(value); break; case "Random": sim.Auto.Random = value; break; case "Talk": sim.Auto.Talk = value; break; case "Physics": sim.Auto.Physics = value; break; case "Mouse": sim.Auto.Mouse = value; break; }
            };
        }
        var sidebar = new VBoxContainer { CustomMinimumSize = new Vector2(340, 0) }; columns.AddChild(sidebar);
        sidebar.AddChild(Text("EXPRESSION", 12));
        var expressions = new HFlowContainer(); sidebar.AddChild(expressions);
        foreach (var name in RigSimulation.Presets.Keys)
            expressions.AddChild(Button(name, () =>
            {
                if (actor.Simulation?.ActivePreset == name) { actor.SetPreset("neutral"); actor.SetPreset(null); }
                else actor.SetPreset(name);
                SyncSliders();
            }));
        var tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; sidebar.AddChild(tabs);
        parameters = CreateScrollTab(tabs, "Parameters"); layerControls = CreateScrollTab(tabs, "Layers");
        BuildParameters();
        var footer = Text("Original motion defaults  ·  Independent character instances  ·  No PSD dependency in addon", 12);
        footer.Modulate = new Color(0.52f, 0.59f, 0.68f); root.AddChild(footer);
    }
    private static VBoxContainer CreateScrollTab(TabContainer tabs, string name)
    {
        var scroll = new ScrollContainer { Name = name, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        tabs.AddChild(scroll);
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; box.AddThemeConstantOverride("separation", 8); scroll.AddChild(box); return box;
    }
    private void BuildParameters()
    {
        foreach (var spec in Parameters.Specs)
        {
            var row = new VBoxContainer(); parameters.AddChild(row);
            var heading = new HBoxContainer(); row.AddChild(heading);
            var label = Text(spec.Key.ToString(), 13); label.SizeFlagsHorizontal = SizeFlags.ExpandFill; heading.AddChild(label);
            var number = new SpinBox { MinValue = spec.Min, MaxValue = spec.Max, Step = 0.01, Value = spec.Default, CustomMinimumSize = new Vector2(90, 0) }; heading.AddChild(number);
            var slider = new HSlider { MinValue = spec.Min, MaxValue = spec.Max, Step = 0.01, Value = spec.Default, CustomMinimumSize = new Vector2(0, 18) }; row.AddChild(slider); sliders[spec.Key] = slider;
            slider.ValueChanged += value => { number.SetValueNoSignal(value); if (!syncing) actor.SetParameter(spec.Key.ToString(), value); };
            number.ValueChanged += value => { slider.Value = value; };
            slider.GuiInput += ev => { if (ev is InputEventMouseButton { DoubleClick: true, Pressed: true }) slider.Value = spec.Default; };
        }
    }
    private void SyncSliders()
    {
        if (actor.Simulation is not { } sim) return;
        syncing = true; foreach (var spec in Parameters.Specs) sliders[spec.Key].Value = sim.Target[spec.Key]; syncing = false;
    }
    private void BuildLayerControls()
    {
        foreach (var child in layerControls.GetChildren()) { layerControls.RemoveChild(child); child.QueueFree(); }
        foreach (var part in actor.Simulation!.Parts)
        {
            var group = new VBoxContainer(); layerControls.AddChild(group);
            var visible = new CheckButton { Text = part.Definition.Name, ButtonPressed = part.Visible }; group.AddChild(visible); visible.Toggled += v => part.Visible = v;
            var row = new HBoxContainer(); group.AddChild(row);
            row.AddChild(Text("Depth")); var depth = new SpinBox { MinValue = 0, MaxValue = 2, Step = 0.01, Value = part.Depth }; row.AddChild(depth); depth.ValueChanged += v => part.Depth = v;
            row.AddChild(Text("Order")); var order = new SpinBox { MinValue = 0, MaxValue = 1000, Step = 1, Value = part.DrawOrder }; row.AddChild(order); order.ValueChanged += v => part.DrawOrder = (int)v;
            var opacity = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.01, Value = part.Opacity, TooltipText = "Opacity" }; group.AddChild(opacity); opacity.ValueChanged += v => part.Opacity = v;
        }
    }
    private void LoadSample(string name)
    {
        sample = name; actor.LoadModel(GD.Load<AnimeRigModel>($"res://demo/models/{name}/model.tres"));
        foreach (var (key, toggle) in toggles) toggle.SetPressedNoSignal(key != "Mouse");
        SyncSliders(); BuildLayerControls(); Fit();
    }
    private void ToggleSecond()
    {
        if (second is not null) { second.Free(); second = null; }
        else
        {
            second = new AnimeRigNode { Name = "IndependentCharacter", Model = actor.Model }; stage.AddChild(second);
            second.SetPreset("winkR");
        }
        Fit();
    }
    private void Fit()
    {
        if (actor.Simulation is not { } sim) return;
        var size = sim.Definition.Canvas; float width = second is null ? stage.Size.X : stage.Size.X / 2;
        float scale = Math.Min(width / size.W, stage.Size.Y / size.H) * 0.97f;
        actor.Scale = Vector2.One * scale; actor.Position = new Vector2((width - size.W * scale) / 2, (stage.Size.Y - size.H * scale) / 2);
        if (second is not null) { second.Scale = actor.Scale; second.Position = actor.Position + new Vector2(width, 0); }
    }
    public override void _Process(double delta)
    {
        if (info is null || actor.Simulation is not { } sim) return;
        info.Text = $"{sample.ToUpperInvariant()}   /   {sim.Parts.Length} parts   /   {sim.Parts.Sum(p => p.Springs.Length)} strands   /   {Engine.GetFramesPerSecond()} FPS   /   {(actor.Playing ? "PLAYING" : "PAUSED")}";
    }
}
