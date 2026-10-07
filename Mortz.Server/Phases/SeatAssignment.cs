using Mortz.Core.Match.Teams;
using Mortz.Server.Players;

namespace Mortz.Server.Phases;

/// <summary>A player and the team they froze on in the lobby.</summary>
public readonly record struct SeatAssignment(Player Player, Team? Team);
