using System;
using Anime25D.Sample.Core;
using Godot;

namespace Anime25D.Sample;

[GlobalClass]
public partial class AnimeRigModel : Resource
{
    /// <summary>Optional code-authored animation assets; no motion file loading.</summary>
    public Anime25D.Core.AnimationModel? Animations { get; set; }
    [Export(PropertyHint.MultilineText)] public string Manifest { get; set; } = "";
    [Export] public Godot.Collections.Array<Texture2D> Textures { get; set; } = [];
    [Export]
    public AnimeRigProfile? Profile { get; set; }

    public RigDefinition ReadDefinition()
    {
        var definition = RigDefinition.Parse(Manifest);
        foreach (var part in definition.Layers)
        {
            if (part.TextureIndex >= Textures.Count || Textures[part.TextureIndex] is not { } texture || texture.GetWidth() != part.Width || texture.GetHeight() != part.Height)
                throw new ArgumentException($"Missing or incorrectly sized texture for {part.Name}.");
        }
        return definition;
    }
}
