using Anime25D;
using Anime25D.Runtime;
using Anime25D.Sample.Rendering;
using Anime25D.Sample;
using Anime25D.Sample.Core;
using Anime25D.Examples;
using Godot;

public partial class Demo : Control
{
    private AnimeModelNode actor = null!;
    private DesktopMouseTracking mouseTracking = null!;
    private AnimeModelNode? second;
    private Panel stage = null!;
    private Label info = null!;
    private VBoxContainer parameters = null!, layerControls = null!;
    private readonly Dictionary<Parameter, HSlider> sliders = [];
    private readonly Dictionary<string, CheckButton> toggles = [];
    private bool syncing;
    private AnimeRigModel loadedModel = null!;
    private string sample = "sample-a";

    public override void _Ready()
    {
#if ANIME25D_TESTS
        if (OS.GetCmdlineUserArgs().Contains("--record-idle"))
        {
            AddChild(new IdleRecording());
            return;
        }
        if (OS.GetCmdlineUserArgs().Contains("--render-tests"))
        {
            AddChild(new RenderChecks());
            return;
        }
        if (OS.GetCmdlineUserArgs().Contains("--backend-tests"))
        {
            AddChild(new BackendChecks());
            return;
        }
        if (OS.GetCmdlineUserArgs().Contains("--composition-tests")) { AddChild(new CompositionRenderChecks()); return; }
#endif
        BuildUi();
        LoadSample("sample-a");
        if (OS.GetCmdlineUserArgs().Contains("--capture-demo"))
            CaptureDemo();
    }
    private async void CaptureDemo()
    {
        ulong end = Time.GetTicksMsec() + 2200;
        while (Time.GetTicksMsec() < end)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://artifacts"));
        GetViewport().GetTexture().GetImage().SavePng("res://artifacts/demo.png");
        GetTree().Quit();
    }
    private static Label Text(string text, int size = 14)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        return label;
    }
    private static Button Button(string text, Action onPress)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 32) };
        button.Pressed += onPress;
        return button;
    }
    private static StyleBoxFlat Box(Color color, int radius = 12)
    {
        return new StyleBoxFlat { BgColor = color, CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius, CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius, ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 12, ContentMarginBottom = 12 };
    }
    private void BuildUi()
    {
        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride("margin_" + side, 20);
        AddChild(margin);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 14);
        margin.AddChild(root);
        var header = new HBoxContainer();
        root.AddChild(header);
        var title = Text("Anime25D", 28);
        header.AddChild(title);
        var subtitle = Text("   GODOT / C#   ·   RUNTIME LAB", 13);
        subtitle.Modulate = new Color(0.56f, 0.7f, 0.83f);
        subtitle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(subtitle);
        header.AddChild(Button("Sample A", () => LoadSample("sample-a")));
        header.AddChild(Button("Sample B", () => LoadSample("sample-b")));
        var columns = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", 18);
        root.AddChild(columns);
        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        columns.AddChild(left);
        stage = new Panel { SizeFlagsVertical = SizeFlags.ExpandFill, ClipContents = true };
        stage.AddThemeStyleboxOverride("panel", Box(new Color(0.10f, 0.125f, 0.16f)));
        left.AddChild(stage);
        actor = new AnimeModelNode { Name = "Character" };
        stage.AddChild(actor);
        mouseTracking = new DesktopMouseTracking { Enabled = false };
        actor.AddChild(mouseTracking);
        stage.Resized += Fit;
        info = Text("Loading…", 13);
        left.AddChild(info);
        var controls = new HFlowContainer();
        controls.AddThemeConstantOverride("h_separation", 6);
        left.AddChild(controls);
        controls.AddChild(Button("Pause / Resume", () => actor.Playing = !actor.Playing));
        controls.AddChild(Button("Reset", () => { actor.Instance?.ResetInputs(); actor.Instance?.Animation?.StopMotion(0); actor.Instance?.Animation?.ClearExpression(0); SyncSliders(); }));
        controls.AddChild(Button("Two instances", ToggleSecond));
        controls.AddChild(Button("Nod", () => actor.Instance?.Animation?.PlayMotion("nod")));
        controls.AddChild(Button("Loop sway", () => actor.Instance?.Animation?.PlayMotion("sway")));
        controls.AddChild(Button("Stop motion", () => actor.Instance?.Animation?.StopMotion()));
        controls.AddChild(Button("Background", () => stage.AddThemeStyleboxOverride("panel", Box(stage.GetThemeStylebox("panel") is StyleBoxFlat b && b.BgColor.R < 0.2 ? new Color(0.78f, 0.80f, 0.82f) : new Color(0.10f, 0.125f, 0.16f)))));
        var automatic = new HFlowContainer();
        left.AddChild(automatic);
        foreach (var key in new[] { "Idle", "Blink", "Random", "Talk", "Physics", "Mouse" })
        {
            var toggle = new CheckButton { Text = key, ButtonPressed = key != "Mouse" };
            toggles[key] = toggle;
            automatic.AddChild(toggle);
            if (key == "Mouse")
                toggle.TooltipText = "Follow the mouse across the entire screen, including outside this window.";
            toggle.Toggled += value =>
            {
                if (actor.Instance is not { } instance) return;
                if (key == "Mouse") mouseTracking.Enabled = value;
                else instance.SetComponentEnabled(key, value);
            };
        }
        var sidebar = new VBoxContainer { CustomMinimumSize = new Vector2(340, 0) };
        columns.AddChild(sidebar);
        sidebar.AddChild(Text("EXPRESSION", 12));
        var expressions = new HFlowContainer();
        sidebar.AddChild(expressions);
        foreach (var name in ExpressionPose.Defaults.Keys)
            expressions.AddChild(Button(name, () =>
            {
                if (actor.Instance?.Animation?.Expression?.Name == name)
                {
                    actor.Instance?.Animation?.ClearExpression();
                }
                else
                    actor.Instance?.Animation?.SetExpression(name);
                SyncSliders();
            }));
        var tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        sidebar.AddChild(tabs);
        parameters = CreateScrollTab(tabs, "Parameters");
        layerControls = CreateScrollTab(tabs, "Layers");
        var footer = Text("Original motion defaults  ·  Independent character instances  ·  No PSD dependency in addon", 12);
        footer.Modulate = new Color(0.52f, 0.59f, 0.68f);
        root.AddChild(footer);
    }
    private static VBoxContainer CreateScrollTab(TabContainer tabs, string name)
    {
        var scroll = new ScrollContainer { Name = name, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        tabs.AddChild(scroll);
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(box);
        return box;
    }
    private void BuildParameters()
    {
        foreach (var child in parameters.GetChildren())
        {
            parameters.RemoveChild(child);
            child.QueueFree();
        }
        sliders.Clear();
        foreach (var spec in SampleParameters.Specs)
        {
            var row = new VBoxContainer();
            parameters.AddChild(row);
            var heading = new HBoxContainer();
            row.AddChild(heading);
            var label = Text(spec.Key.ToString(), 13);
            label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            label.ClipText = true;
            label.TooltipText = spec.Key.ToString();
            heading.AddChild(label);
            var number = new SpinBox { MinValue = spec.Min, MaxValue = spec.Max, Step = 0.01, Value = spec.Default, CustomMinimumSize = new Vector2(90, 0) };
            heading.AddChild(number);
            var slider = new HSlider { MinValue = spec.Min, MaxValue = spec.Max, Step = 0.01, Value = spec.Default, CustomMinimumSize = new Vector2(0, 18) };
            row.AddChild(slider);
            sliders[spec.Key] = slider;
            slider.ValueChanged += value => { number.SetValueNoSignal(value); if (!syncing) actor.Instance!.SetParameter(spec.Key.ToString(), value, !actor.Playing); };
            number.ValueChanged += value => { slider.Value = value; };
            slider.GuiInput += ev => { if (ev is InputEventMouseButton { DoubleClick: true, Pressed: true }) slider.Value = spec.Default; };
        }
    }
    private void SyncSliders()
    {
        if (actor.Instance is not { } sim)
            return;
        syncing = true;
        foreach (var spec in SampleParameters.Specs)
            sliders[spec.Key].Value = sim.Animation.BasePose[spec.Key.ToString()];
        syncing = false;
    }
    private void BuildLayerControls()
    {
        foreach (var child in layerControls.GetChildren())
        {
            layerControls.RemoveChild(child);
            child.QueueFree();
        }
        foreach (var (part, index) in ((SampleWarp)actor.Instance!.Definition.Plan.Deformers[0]).Parts.Select((p, i) => (p, i)))
        {
            var group = new VBoxContainer();
            layerControls.AddChild(group);
            var visible = new CheckButton { Text = part.Definition.Name, ButtonPressed = actor.Instance!.Layers[index].Visible };
            group.AddChild(visible);
            visible.Toggled += v => actor.Instance!.Layers[index].Visible = v;
            var row = new HBoxContainer();
            group.AddChild(row);
            row.AddChild(Text("Depth"));
            var depth = new SpinBox { MinValue = 0, MaxValue = 2, Step = 0.01, Value = part.Definition.Depth };
            row.AddChild(depth);
            depth.ValueChanged += v => actor.Instance!.SetParameter(SampleWarp.DepthParameter(index), v, true);
            row.AddChild(Text("Order"));
            var order = new SpinBox { MinValue = 0, MaxValue = 1000, Step = 1, Value = part.Definition.InitialDrawOrder };
            row.AddChild(order);
            order.ValueChanged += v => actor.Instance!.Layers[index].DrawOrder = (int)v;
            var opacity = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.01, Value = 1, TooltipText = "Opacity" };
            group.AddChild(opacity);
            opacity.ValueChanged += v => actor.Instance!.Layers[index].Opacity = v;
        }
    }
    private static ModelView CreateView(AnimeRigModel model) => new(SampleModelBuilder.Build(model.ReadDefinition(), model.Profile?.ReadConfiguration(), model.Animations), model.Textures, new SampleGpuFactory());
    private void LoadSample(string name)
    {
        sample = name;
        mouseTracking.Enabled = false;
        loadedModel = GD.Load<AnimeRigModel>($"res://demo/models/{name}/model.tres");
        actor.Load(CreateView(loadedModel));
        BuildParameters();
        foreach (var (key, toggle) in toggles)
            toggle.SetPressedNoSignal(key != "Mouse");
        SyncSliders();
        BuildLayerControls();
        Fit();
    }
    private void ToggleSecond()
    {
        if (second is not null)
        {
            second.Free();
            second = null;
        }
        else
        {
            second = new AnimeModelNode { Name = "IndependentCharacter" };
            stage.AddChild(second);
            second.Load(CreateView(loadedModel));
            second.Instance?.Animation?.SetExpression("winkR");
        }
        Fit();
    }
    private void Fit()
    {
        if (actor.Instance is not { } sim)
            return;
        var size = (Width: sim.Definition.CanvasWidth, Height: sim.Definition.CanvasHeight);
        float width = second is null ? stage.Size.X : stage.Size.X / 2;
        float scale = Math.Min(width / size.Width, stage.Size.Y / size.Height) * 0.97f;
        actor.Scale = Vector2.One * scale;
        actor.Position = new Vector2((width - size.Width * scale) / 2, (stage.Size.Y - size.Height * scale) / 2);
        if (second is not null)
        {
            second.Scale = actor.Scale;
            second.Position = actor.Position + new Vector2(width, 0);
        }
    }
    public override void _Process(double delta)
    {
        if (info is null || actor.Instance is not { } sim)
            return;
        info.Text = $"{sample.ToUpperInvariant()}   /   {sim.Definition.Layers.Count} parts   /   {((SampleWarp)sim.Definition.Plan.Deformers[0]).Parts.Sum(p => p.Definition.Strands?.Length ?? 0)} strands   /   {Engine.GetFramesPerSecond()} FPS   /   {(actor.Playing ? "PLAYING" : "PAUSED")}";
    }
}
