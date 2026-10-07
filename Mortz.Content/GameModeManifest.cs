using Mortz.Core.Match.Configuration;
using Combat = Mortz.Core.Match.Configuration.Combat;
using ModeRules = Mortz.Core.Match.Configuration.ModeRules;
using Physics = Mortz.Core.Match.Configuration.Physics;

namespace Mortz.Content;

[TomlModel]
public sealed record GameModeManifest : ITomlValidatedModel
{
    private static readonly string[] _identity =
        ["rules.teams", "rules.end_condition.type", "rules.objective.type", "rules.respawn.type"];

    public required int FormatVersion { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public ModeRules Rules { get; init; } = new();
    public Physics Physics { get; init; } = new();
    public Combat Combat { get; init; } = new();

    public void ValidateToml(string source, List<ContentDiagnostic> diagnostics) =>
        MatchConfigContent.Validate(Rules, source, diagnostics);

    public MatchConfigSnapshot ToMatchConfigSnapshot() => new(
        Rules.ToSnapshot(),
        Physics.ToSnapshot(),
        Combat.ToSnapshot());

    public bool Matches(MatchConfig current) => Matches(current.ToSnapshot());

    public bool Matches(MatchConfigSnapshot current) => TomlModel.PropertiesMatch(
        this,
        new RulesetManifest
        {
            Rules = current.Rules.ToMutable(),
            Physics = current.Physics.ToMutable(),
            Combat = current.Combat.ToMutable(),
        },
        _identity);
}

[TomlModel]
public sealed record RulesetManifest : ITomlValidatedModel
{
    public ModeRules Rules { get; init; } = new();
    public Physics Physics { get; init; } = new();
    public Combat Combat { get; init; } = new();

    public void ValidateToml(string source, List<ContentDiagnostic> diagnostics) =>
        MatchConfigContent.Validate(Rules, source, diagnostics);

    public MatchConfigSnapshot ToMatchConfigSnapshot() => new(
        Rules.ToSnapshot(),
        Physics.ToSnapshot(),
        Combat.ToSnapshot());
}
