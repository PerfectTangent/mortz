using Mortz.Core.Sim.Modifiers;

namespace Mortz.Core.Match.Configuration;

public sealed partial class MatchConfig
{
    [ConfigSection]
    public ModeRules Rules { get; init; } = new();

    [ConfigSection]
    public Physics Physics { get; init; } = new();

    [ConfigSection]
    public Combat Combat { get; init; } = new();

    /// <summary>Rule composition errors, then whether the map can host the objective.</summary>
    public IReadOnlyList<string> Errors(MapZones zones)
    {
        List<string> errors = [.. Rules.Errors()];
        if (Rules.Objective.MapError(zones) is { } mapError)
        {
            errors.Add(mapError);
        }
        return errors;
    }
}
