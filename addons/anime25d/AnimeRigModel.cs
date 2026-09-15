using System;
using Anime25D.Core;
using Godot;

namespace Anime25D;

[GlobalClass]
public partial class AnimeRigModel : Resource
{
    [Export(PropertyHint.MultilineText)] public string Manifest { get; set; } = "";
    [Export] public Godot.Collections.Array<Texture2D> Textures { get; set; } = [];

    public RigDefinition ReadDefinition()
    {
        var definition = RigDefinition.Parse(Manifest);
        foreach (var part in definition.Layers)
        {
            if (part.Texture >= Textures.Count || Textures[part.Texture] is not { } texture || texture.GetWidth() != part.W || texture.GetHeight() != part.H)
                throw new ArgumentException($"Missing or incorrectly sized texture for {part.Name}.");
        }
        return definition;
    }
}
