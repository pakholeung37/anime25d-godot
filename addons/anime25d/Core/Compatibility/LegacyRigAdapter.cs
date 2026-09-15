using System;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Anime25D.Core;

/// <summary>The only runtime boundary that interprets v1 layer names. V2 names are display labels.</summary>
internal static class LegacyRigAdapter
{
    public static string Upgrade(string json)
    {
        var root = JsonNode.Parse(json)?.AsObject() ?? throw new ArgumentException("Empty rig document.");
        if (root["version"]?.GetValue<int>() != 1)
            return json;
        foreach (var item in root["layers"]?.AsArray() ?? throw new ArgumentException("Missing layers."))
        {
            var layer = item?.AsObject() ?? throw new ArgumentException("Null layer.");
            string name = Regex.Replace(layer["name"]?.GetValue<string>() ?? "", "_(l|r)$", "");
            if (name != "eye_close2")
                name = Regex.Replace(name, "_\\d+$", "");
            layer["role"] = name switch
            {
                "face" => "Face",
                "neck" => "Neck",
                "irides" => "Iris",
                "eyewhite" => "EyeWhite",
                "eye_close" => "ClosedEye",
                "eye_close2" => "AlternateClosedEye",
                "eyebrow" => "Eyebrow",
                "mouth_open" => "OpenMouth",
                "mouth_close" => "ClosedMouth",
                "front hair" => "Fringe",
                "topwear" => "UpperClothing",
                "handwear" => "ArmClothing",
                _ => "Generic"
            };
            layer["side"] = layer["side"]?.GetValue<string>() switch
            {
                "L" => "Left",
                "R" => "Right",
                null => "None",
                var side => throw new ArgumentException($"Invalid side: {side}")
            };
            layer["fade"] = layer["fade"]?.GetValue<string>() switch
            {
                "eyeOpen" => "OpenEye",
                "eyeClose" => "ClosedEye",
                "eyeClose2" => "AlternateClosedEye",
                "mouthOpen" => "OpenMouth",
                "mouthClose" => "ClosedMouth",
                null => "None",
                var fade => throw new ArgumentException($"Invalid fade: {fade}")
            };
            layer["physicsMesh"] = layer["phys"] is not null;
            layer.Remove("phys");
        }
        root["version"] = 2;
        return root.ToJsonString();
    }
}
