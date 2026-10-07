using Mortz.Core.Match.Participation;
using Mortz.Core.Match.Scoring;
using Mortz.Protocol.Net;
using Mortz.Protocol.Net.Match;
using Mortz.Protocol.Net.Score;
using Mortz.Protocol.Replication;

namespace Mortz.Client.Match;

/// <summary>Decodes the reliable match-state stream for one match lifetime.</summary>
public class ClientMatchStateAdapter(ClientMatchState state, ObjectiveWire? objectiveWire = null) :
    IHandle<MatchParticipationMsg>,
    IHandle<MatchPointMsg>,
    IHandle<MatchEndMsg>,
    IHandle<ScoreSyncMsg>,
    IHandle<EliminationMsg>
{
    private readonly ObjectiveWire _objectiveWire = objectiveWire ?? ObjectiveWire.None;

    public void Handle(in MatchParticipationMsg message) =>
        state.TryApplyParticipation(message.MatchGeneration, new MatchParticipation(
            message.Seat, message.Activity, message.Reason, message.ReturnTick));

    public void Handle(in MatchPointMsg message) =>
        state.TryApplyMatchPoint(message.MatchGeneration, MatchProtocol.Decode(message));

    public void Handle(in MatchEndMsg message)
    {
        if (MatchProtocol.TryDecode(message, out Victor? winner))
            state.TryComplete(message.MatchGeneration, message.Tick,
                _objectiveWire.Deserialize(message.Objective), winner);
    }

    public void Handle(in ScoreSyncMsg message) =>
        state.TryReplaceScores(
            message.MatchGeneration,
            message.Rows.Select(row => new MatchScoreRow(row.PeerId, row.Kills, row.Deaths))
                .ToArray(),
            new TeamKills(message.BlueKills, message.RedKills));

    public void Handle(in EliminationMsg message) =>
        state.TryPatchScores(message.MatchGeneration, new MatchScorePatch(
            message.KillerId,
            message.VictimId,
            message.Flags.HasFlag(EliminationFlags.SUICIDE),
            message.KillerKills,
            message.VictimDeaths,
            message.RewardedId,
            message.RewardedKills,
            new TeamKills(message.BlueKills, message.RedKills)));
}
