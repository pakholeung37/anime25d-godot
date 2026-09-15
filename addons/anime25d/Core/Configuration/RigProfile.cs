using System;
using System.Collections.Generic;
using System.Collections.Frozen;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Anime25D.Core;

/// <summary>Immutable, per-model authoring settings. Defaults preserve the reference rig.</summary>
public sealed record RigProfile
{
    public MotionSettings Motion { get; init; } = new();
    public PhysicsSettings Physics { get; init; } = new();
    public DeformationSettings Deformation { get; init; } = new();
    public MeshSettings Mesh { get; init; } = new();
    public IReadOnlyDictionary<Parameter, ParameterRange> Parameters { get; init; } = new Dictionary<Parameter, ParameterRange>();
    public IReadOnlyDictionary<string, ExpressionPose> Expressions { get; init; } = ExpressionPose.Defaults;

    public static JsonSerializerOptions JsonOptions => new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };

    public static RigProfile Parse(string json)
    {
        var profile = JsonSerializer.Deserialize<RigProfile>(json, JsonOptions)
            ?? throw new ArgumentException("Profile must be an object.");
        profile.Validate();
        return profile;
    }

    public IReadOnlyList<ParameterSpec> CreateCatalog() => Array.AsReadOnly(Core.Parameters.Specs.Select(spec =>
        Parameters.TryGetValue(spec.Key, out var range) ? new ParameterSpec(spec.Key, range.Default, range.Minimum, range.Maximum) : spec).ToArray());

    public RigProfile CreateSnapshot()
    {
        Validate();
        return this with
        {
            Parameters = Parameters.ToFrozenDictionary(),
            Expressions = Expressions.ToFrozenDictionary()
        };
    }

    public void Validate()
    {
        // Reject NaN, infinity, and null settings at the resource boundary, not in the frame loop.
        ValidateObject(this);
        foreach (var (key, range) in Parameters)
            if (!Enum.IsDefined(key) || range.Minimum > range.Maximum || range.Default < range.Minimum || range.Default > range.Maximum)
                throw new ArgumentException($"Invalid parameter range: {key}.");
        if (Motion.MaximumDeltaSeconds <= 0 || Motion.SmoothingRate < 0 || Motion.Breath.PeriodSeconds <= 0 ||
            Motion.Blink.CloseSeconds <= 0 || Motion.Blink.OpenSeconds <= 0 || Motion.Blink.IrisBounceSeconds <= 0 ||
            Motion.Blink.HoldSeconds < 0 || Motion.Blink.AlternateHoldSeconds < 0 || Motion.MaximumDeltaSeconds > 1 ||
            Physics.IntegrationStepSeconds < 0.00001 || Mesh.BaseCellPixels <= 0 || Mesh.PhysicsCellPixels <= 0 ||
            Mesh.ReferenceCanvasWidth <= 0 || Mesh.MinimumScale <= 0 || Mesh.FringeTransitionPixels <= 0 ||
            Deformation.EyeFadeWidth <= 0 || Deformation.MouthFadeWidth <= 0 || Deformation.IrisCloseThreshold <= 0 ||
            Deformation.ChestBreathFalloff <= 0 || Deformation.MouthCurvePaddingPixels <= 0 ||
            Deformation.MouthCurveExponent < 0 || Deformation.FringeOffsetExponent < 0 || Deformation.FringeLengthScale < 0 ||
            Deformation.FringeSwayExponent < 0 || Deformation.HairSwayExponent < 0 || Deformation.HairSoftnessExponent < 0)
            throw new ArgumentException("Periods, integration steps, mesh sizes, and fade widths must be positive.");
    }

    private static void ValidateObject(object value)
    {
        if (value is DelayRange delay && (delay.MinimumMilliseconds < 0 || delay.SpreadMilliseconds < 0))
            throw new ArgumentException("Motion delays cannot be negative.");
        if (value is SpringSettings spring && (spring.Stiffness < 0 || spring.Damping < 0))
            throw new ArgumentException("Spring stiffness and damping cannot be negative.");
        if (value is double number)
        {
            if (!double.IsFinite(number) || Math.Abs(number) > float.MaxValue)
                throw new ArgumentException("Profile values must be finite GPU-representable numbers.");
            return;
        }
        if (value is string || value.GetType().IsEnum)
            return;
        if (value is System.Collections.IEnumerable items)
        {
            foreach (var item in items)
                ValidateObject(item ?? throw new ArgumentException("Null profile entry."));
            return;
        }
        foreach (var property in value.GetType().GetProperties().Where(p => p.GetMethod is { IsStatic: false } && p.GetIndexParameters().Length == 0))
            ValidateObject(property.GetValue(value) ?? throw new ArgumentException($"Missing profile setting: {property.Name}."));
    }
}

public sealed record ParameterRange(double Default, double Minimum, double Maximum);
