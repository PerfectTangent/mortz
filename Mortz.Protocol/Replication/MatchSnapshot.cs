using Mortz.Core.Match.Objectives;
using Mortz.Core.Replication;
using Mortz.Core.Sim;
using Mortz.Protocol.Net;

namespace Mortz.Protocol.Replication;

public readonly record struct ReplicatedPlayer(
    PlayerState Simulation,
    PlayerPresentationState Presentation, byte Slot = 0, byte Skin = 0);

/// <summary>Simulation and presentation state sampled atomically at one server tick.</summary>
public record MatchSnapshot(
    int Tick,
    ReplicatedPlayer[] Players,
    int Generation = 0,
    int RosterRevision = 0)
{
    public ObjectiveState Objective { get; init; } = NoObjectiveState.Instance;

    public Snapshot SimulationSnapshot =>
        new(Tick, [.. Players.Select(player => player.Simulation)], []);

    public byte[] Serialize(ObjectiveWire? objectiveWire = null) => MatchSnapshotWire.Serialize(this, localPeerId: null, objectiveWire);

    public byte[] SerializeFor(int localPeerId, ObjectiveWire? objectiveWire = null) =>
        MatchSnapshotWire.Serialize(this, localPeerId, objectiveWire);

    public static MatchSnapshot Deserialize(byte[] data, ObjectiveWire? objectiveWire = null) =>
        MatchSnapshotWire.Deserialize(data, slots: null, objectiveWire);

    /// <summary>Remote player slots are resolved through the reliable roster.</summary>
    public static MatchSnapshot Deserialize(byte[] data, IPeerSlots slots, ObjectiveWire? objectiveWire = null) =>
        MatchSnapshotWire.Deserialize(data, slots, objectiveWire);
}
