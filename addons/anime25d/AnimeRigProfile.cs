using Anime25D.Core;
using Godot;

namespace Anime25D;

/// <summary>Optional model/instance profile resource. Omitted JSON fields use reference defaults.</summary>
[GlobalClass]
public partial class AnimeRigProfile : Resource
{
    [Export(PropertyHint.MultilineText)] public string SettingsJson { get; set; } = "{}";
    public RigProfile ReadConfiguration() => RigProfile.Parse(SettingsJson);
}
