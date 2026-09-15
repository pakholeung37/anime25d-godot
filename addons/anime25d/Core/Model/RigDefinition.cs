using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

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
        var rig = JsonSerializer.Deserialize<RigDefinition>(LegacyRigAdapter.Upgrade(json), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        })
            ?? throw new ArgumentException("Empty rig document.");
        if (rig.Format != "anime25d-rig" || rig.Version != 2)
            throw new ArgumentException("Unsupported rig format/version.");
        if (rig.Canvas.Width < 2 || rig.Canvas.Height < 2 || (long)rig.Canvas.Width * rig.Canvas.Height > 24_000_000 || Math.Max(rig.Canvas.Width, rig.Canvas.Height) > 16384)
            throw new ArgumentException("Invalid rig canvas.");
        if (rig.Layers.Length is < 1 or > 1000)
            throw new ArgumentException("Invalid part count.");
        foreach (var part in rig.Layers)
        {
            if (part.Width < 1 || part.Height < 1 || (long)part.Width * part.Height > 24_000_000 || part.TextureIndex < 0 || part.Strands?.Length > 6)
                throw new ArgumentException($"Invalid part: {part.Name}.");
            if (!double.IsFinite(part.Depth) || !double.IsFinite(part.Opacity) || part.Depth is < 0 or > 2 || part.Opacity is < 0 or > 1)
                throw new ArgumentException("Invalid layer depth/opacity.");
            if (string.IsNullOrWhiteSpace(part.Name) || !Enum.IsDefined(part.Group) || !Enum.IsDefined(part.Side) || !Enum.IsDefined(part.Role) || !Enum.IsDefined(part.Fade))
                throw new ArgumentException("Invalid part metadata.");
            foreach (var strand in part.Strands ?? [])
                if (!double.IsFinite(strand.X) || !double.IsFinite(strand.RootY) || !double.IsFinite(strand.TipY))
                    throw new ArgumentException("Invalid strand coordinates.");
        }
        if (!(rig.Anchors.FaceScale > 0) || !double.IsFinite(rig.Anchors.FaceScale))
            throw new ArgumentException("Invalid face scale.");
        foreach (var anchor in new[] { rig.Anchors.Face, rig.Anchors.Mouth, rig.Anchors.NeckPivot, rig.Anchors.BodyPivot, rig.Anchors.LeftEye, rig.Anchors.RightEye })
            if (anchor is not null && new[] { anchor.MinimumX, anchor.MaximumX, anchor.MinimumY, anchor.MaximumY, anchor.CenterX, anchor.CenterY, anchor.IrisCenterX, anchor.IrisCenterY, anchor.ClosedEyeY }.Any(v => !double.IsFinite(v)))
                throw new ArgumentException("Invalid anchor coordinates.");
        if (!double.IsFinite(rig.Anchors.NeckTopY) || !double.IsFinite(rig.Anchors.NeckBottomY))
            throw new ArgumentException("Invalid neck anchors.");
        return rig;
    }
}
public sealed record CanvasSize
{
    [JsonPropertyName("w")]
    public int Width { get; init; }
    [JsonPropertyName("h")]
    public int Height { get; init; }
}
public sealed record Anchor
{
    [JsonPropertyName("x0")]
    public double MinimumX { get; init; }
    [JsonPropertyName("x1")]
    public double MaximumX { get; init; }
    [JsonPropertyName("y0")]
    public double MinimumY { get; init; }
    [JsonPropertyName("y1")]
    public double MaximumY { get; init; }
    [JsonPropertyName("cx")]
    public double CenterX { get; init; }
    [JsonPropertyName("cy")]
    public double CenterY { get; init; }
    [JsonPropertyName("icx")]
    public double IrisCenterX { get; init; }
    [JsonPropertyName("icy")]
    public double IrisCenterY { get; init; }
    [JsonPropertyName("closeY")]
    public double ClosedEyeY { get; init; }
}
public sealed record RigAnchors
{
    public Anchor Face { get; init; } = new();
    [JsonPropertyName("eyeL")]
    public Anchor? LeftEye { get; init; }
    [JsonPropertyName("eyeR")]
    public Anchor? RightEye { get; init; }
    public Anchor Mouth { get; init; } = new();
    public Anchor NeckPivot { get; init; } = new();
    public Anchor BodyPivot { get; init; } = new();
    [JsonPropertyName("neckTop")]
    public double NeckTopY { get; init; }
    [JsonPropertyName("neckBottom")]
    public double NeckBottomY { get; init; }
    public double FaceScale { get; init; }
}
public sealed record StrandDefinition
{
    public double X { get; init; }
    public double RootY { get; init; }
    public double TipY { get; init; }
}
public enum PartRole
{
    Generic, Face, Neck, Iris, EyeWhite, ClosedEye, AlternateClosedEye, Eyebrow, OpenMouth, ClosedMouth, Fringe, UpperClothing, ArmClothing
}
public enum PartGroup
{
    Head, Body
}
public enum PartSide
{
    None, Left, Right
}
public enum FadeMode
{
    None, OpenEye, ClosedEye, AlternateClosedEye, OpenMouth, ClosedMouth
}

public sealed record PartDefinition
{
    public string Name { get; init; } = "";
    public int X { get; init; }
    public int Y { get; init; }
    [JsonPropertyName("w")]
    public int Width { get; init; }
    [JsonPropertyName("h")]
    public int Height { get; init; }
    [JsonPropertyName("z")]
    public int InitialDrawOrder { get; init; }
    [JsonPropertyName("texture")]
    public int TextureIndex { get; init; }
    public double Depth { get; init; } = 1;
    public double Opacity { get; init; } = 1;
    public PartRole Role { get; init; }
    public PartGroup Group { get; init; } = PartGroup.Head;
    public FadeMode Fade { get; init; }
    public PartSide Side { get; init; }
    public bool PhysicsMesh { get; init; }
    public StrandDefinition[]? Strands { get; init; }
    public bool Synthetic { get; init; }
}
