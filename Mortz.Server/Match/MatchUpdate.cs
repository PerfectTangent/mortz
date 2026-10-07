using System.Collections.Immutable;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Sim;
using Mortz.Server.Match.Events;

namespace Mortz.Server.Match;

/// <summary>Frozen outputs from one completed match tick.</summary>
public sealed record MatchUpdate
{
    public int Tick { get; init; }

    public ServerTime Time { get; init; }

    public ImmutableArray<SimWorld.MortarEvent> MortarEvents { get; init; } = [];

    public ImmutableArray<Explosion> Explosions { get; init; } = [];

    public ImmutableArray<ShellRetirement> ShellRetirements { get; init; } = [];

    public ImmutableArray<Death> Deaths { get; init; } = [];

    public ImmutableArray<ScoredKill> Eliminations { get; init; } = [];

    /// <summary>Null when nobody is one kill from winning, and once the match is decided.</summary>
    public MatchPoint? MatchPoint { get; init; }

    public ImmutableArray<Judgment> GameEvents { get; init; } = [];

    public ImmutableArray<MatchParticipationChange> ParticipationChanges { get; init; } = [];

    public Victor? MatchEnded { get; init; }

    public FinalKillEvent? FinalKill { get; init; }

    public bool ReturnToLobby { get; init; }

    public ImmutableArray<ModifierChange> ModifierChanges { get; init; } = [];
}
