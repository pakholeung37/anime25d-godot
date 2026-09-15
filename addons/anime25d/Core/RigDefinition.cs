using System;
using System.Linq;
using System.Text.Json;

namespace Anime25D.Core;

// Plain C# data and simulation contain no Godot references. JSON is a versioned interchange format.
public sealed record RigDefinition
{
    public string Format { get; init; } = "";
    public int Version { get; init; }
    public string Name { get; init; } = "";
    public CanvasSize Canvas { get; init; } = new();
    public RigAnchors Anchors { get; init; } = new();
    public PartDefinition[] Layers { get; init; } = [];
    public string[] Warnings { get; init; } = [];

    public static RigDefinition Parse(string json)
    {
        var rig = JsonSerializer.Deserialize<RigDefinition>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new ArgumentException("Empty rig document.");
        if (rig.Format != "anime25d-rig" || rig.Version != 1) throw new ArgumentException("Unsupported rig format/version.");
        if (rig.Canvas.W < 2 || rig.Canvas.H < 2 || (long)rig.Canvas.W * rig.Canvas.H > 24_000_000 || Math.Max(rig.Canvas.W, rig.Canvas.H) > 16384)
            throw new ArgumentException("Invalid rig canvas.");
        if (rig.Layers.Length is < 1 or > 1000) throw new ArgumentException("Invalid part count.");
        foreach (var part in rig.Layers)
        {
            if (part.W < 1 || part.H < 1 || (long)part.W * part.H > 24_000_000 || part.Texture < 0 || part.Strands?.Length > 6)
                throw new ArgumentException($"Invalid part: {part.Name}.");
            if (!double.IsFinite(part.Depth) || !double.IsFinite(part.Opacity) || part.Depth is < 0 or > 2 || part.Opacity is < 0 or > 1)
                throw new ArgumentException("Invalid layer depth/opacity.");
            if (string.IsNullOrWhiteSpace(part.Name) || part.Group is not ("head" or "body") || part.Side is not (null or "L" or "R"))
                throw new ArgumentException("Invalid part metadata.");
            foreach (var strand in part.Strands ?? [])
                if (!double.IsFinite(strand.X) || !double.IsFinite(strand.RootY) || !double.IsFinite(strand.TipY)) throw new ArgumentException("Invalid strand coordinates.");
        }
        if (!(rig.Anchors.FaceScale > 0) || !double.IsFinite(rig.Anchors.FaceScale)) throw new ArgumentException("Invalid face scale.");
        foreach (var anchor in new[] { rig.Anchors.Face, rig.Anchors.Mouth, rig.Anchors.NeckPivot, rig.Anchors.BodyPivot, rig.Anchors.EyeL, rig.Anchors.EyeR })
            if (anchor is not null && new[] { anchor.X0, anchor.X1, anchor.Y0, anchor.Y1, anchor.Cx, anchor.Cy, anchor.Icx, anchor.Icy, anchor.CloseY }.Any(v => !double.IsFinite(v)))
                throw new ArgumentException("Invalid anchor coordinates.");
        if (!double.IsFinite(rig.Anchors.NeckTop) || !double.IsFinite(rig.Anchors.NeckBottom)) throw new ArgumentException("Invalid neck anchors.");
        return rig;
    }
}
public sealed record CanvasSize { public int W { get; init; } public int H { get; init; } }
public sealed record Anchor
{
    public double X0 { get; init; } public double X1 { get; init; }
    public double Y0 { get; init; } public double Y1 { get; init; }
    public double Cx { get; init; } public double Cy { get; init; }
    public double Icx { get; init; } public double Icy { get; init; }
    public double CloseY { get; init; }
}
public sealed record RigAnchors
{
    public Anchor Face { get; init; } = new();
    public Anchor? EyeL { get; init; } public Anchor? EyeR { get; init; }
    public Anchor Mouth { get; init; } = new();
    public Anchor NeckPivot { get; init; } = new();
    public Anchor BodyPivot { get; init; } = new();
    public double NeckTop { get; init; } public double NeckBottom { get; init; }
    public double FaceScale { get; init; }
}
public sealed record StrandDefinition
{
    public double X { get; init; } public double RootY { get; init; } public double TipY { get; init; }
}
public sealed record PartDefinition
{
    public string Name { get; init; } = "";
    public int X { get; init; } public int Y { get; init; }
    public int W { get; init; } public int H { get; init; }
    public int Z { get; init; } public int Texture { get; init; }
    public double Depth { get; init; } = 1;
    public double Opacity { get; init; } = 1;
    public string Group { get; init; } = "head";
    public string? Phys { get; init; } public string? Fade { get; init; } public string? Side { get; init; }
    public StrandDefinition[]? Strands { get; init; }
    public bool Synthetic { get; init; }
}
