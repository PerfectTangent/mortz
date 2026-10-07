using Mortz.Core.Match.Participation;

namespace Mortz.Server.Match;

/// <summary>Where the player sits and, while dead, when the camera flips to spectating.</summary>
public sealed class ParticipationState
{
    public MatchParticipation Current { get; set; } = MatchParticipation.Active;

    /// <summary>Tick that flips DEATH_PRESENTATION into SPECTATING; null when
    /// no flip is scheduled.</summary>
    public int? SpectateAtTick { get; set; }
}
