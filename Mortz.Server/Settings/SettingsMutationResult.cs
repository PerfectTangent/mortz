using Mortz.Protocol.Net.Lobby;

namespace Mortz.Server.Settings;

public sealed record SettingsChange(
    LobbySettings Before,
    LobbySettings After,
    IReadOnlyList<LobbySettingDelta> Deltas,
    bool TeamsRuleChanged);

public enum SettingsRejectReason
{
    UNAUTHORIZED,
    INVALID_RULES,
    UNKNOWN_MODE,
    UNKNOWN_MAP,
    MAP_LOAD_FAILED,
    INCOMPATIBLE_MAP,
}

public abstract record SettingsMutationResult
{
    public sealed record Applied(SettingsChange Change) : SettingsMutationResult;

    public sealed record Rejected(SettingsRejectReason Reason, string? Message = null) : SettingsMutationResult;
}
