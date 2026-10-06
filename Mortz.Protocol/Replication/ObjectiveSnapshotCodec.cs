using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Objectives;
using Mortz.Core.Match.Teams;
using Mortz.Core.Sim;
using Mortz.Protocol.Net;

namespace Mortz.Protocol.Replication;

public abstract class ObjectiveSnapshotCodec
{
    public static ObjectiveSnapshotCodec None { get; } = new NoObjectiveCodec();

    public static ObjectiveSnapshotCodec Create(ObjectiveRulesSnapshot rules) => rules switch
    {
        NoObjectiveRulesSnapshot => None,
        DeadlockObjectiveRulesSnapshot deadlock => new DeadlockCodec(deadlock.ToMutable().AbsenceDelayTicks),
        _ => throw new ArgumentOutOfRangeException(nameof(rules)),
    };

    public abstract void Write(ref PacketWriter writer, ObjectiveState state);
    public abstract ObjectiveState Read(BinaryReader reader);

    public byte[] Serialize(ObjectiveState state) => PacketEncoder.Encode(state, this,
        static (ref PacketWriter writer, ObjectiveState value, ObjectiveSnapshotCodec codec) => codec.Write(ref writer, value));

    public ObjectiveState Deserialize(byte[] data)
    {
        using MemoryStream stream = new(data, writable: false);
        using BinaryReader reader = new(stream);
        ObjectiveState state = Read(reader);
        if (stream.Position != stream.Length)
        {
            throw new InvalidDataException("Trailing objective bytes.");
        }
        return state;
    }

    private class NoObjectiveCodec : ObjectiveSnapshotCodec
    {
        public override void Write(ref PacketWriter writer, ObjectiveState state)
        {
            if (state is not NoObjectiveState)
            {
                throw new InvalidDataException("Objective state does not match the configured mode.");
            }
        }

        public override ObjectiveState Read(BinaryReader reader) => NoObjectiveState.Instance;
    }

    private class DeadlockCodec(int graceTicks) : ObjectiveSnapshotCodec
    {
        public override void Write(ref PacketWriter writer, ObjectiveState state)
        {
            if (state is not DeadlockObjectiveState deadlock)
            {
                throw new InvalidDataException("Expected Deadlock objective state.");
            }
            Validate(deadlock);
            WriteDecimal(ref writer, deadlock.BlueProgress);
            WriteDecimal(ref writer, deadlock.RedProgress);
            writer.Write((byte)((deadlock.BlueOccupied ? 1 : 0) | (deadlock.RedOccupied ? 2 : 0) |
                (deadlock.BlueDecaying ? 4 : 0) | (deadlock.RedDecaying ? 8 : 0)));
            writer.Write((ushort)deadlock.BlueGraceTicks);
            writer.Write((ushort)deadlock.RedGraceTicks);
            writer.Write((byte)(deadlock.CapturedBy ?? 0));
        }

        public override ObjectiveState Read(BinaryReader reader)
        {
            decimal blue = reader.ReadDecimal();
            decimal red = reader.ReadDecimal();
            byte flags = reader.ReadByte();
            int blueGrace = reader.ReadUInt16();
            int redGrace = reader.ReadUInt16();
            byte capture = reader.ReadByte();
            if (flags > 15 || capture > 2)
            {
                throw new InvalidDataException("Invalid objective flags.");
            }
            DeadlockObjectiveState state = new(100 * SimConfig.TICK_RATE, blue, red,
                (flags & 1) != 0, (flags & 2) != 0, blueGrace, redGrace,
                (flags & 4) != 0, (flags & 8) != 0, capture == 0 ? null : (Team)capture);
            Validate(state);
            return state;
        }

        private void Validate(DeadlockObjectiveState state)
        {
            const int CAPACITY = 100 * SimConfig.TICK_RATE;
            if (state.Capacity != CAPACITY || state.BlueProgress < 0 || state.BlueProgress > CAPACITY ||
                state.RedProgress < 0 || state.RedProgress > CAPACITY || state.NeutralProgress < 0 ||
                !ValidTeam(state.BlueOccupied, state.BlueDecaying, state.BlueGraceTicks) ||
                !ValidTeam(state.RedOccupied, state.RedDecaying, state.RedGraceTicks) ||
                (state.CapturedBy == Team.BLUE) != (state.BlueProgress == CAPACITY) ||
                (state.CapturedBy == Team.RED) != (state.RedProgress == CAPACITY) ||
                (state.CapturedBy != null && state.CapturedBy is not Team.BLUE and not Team.RED))
            {
                throw new InvalidDataException("Invalid Deadlock objective state.");
            }
        }

        private bool ValidTeam(bool occupied, bool decaying, int grace) =>
            grace >= 0 && grace <= graceTicks && !(occupied && decaying) &&
            (!(occupied || decaying) || grace == 0);

        private static void WriteDecimal(ref PacketWriter writer, decimal value)
        {
            Span<int> bits = stackalloc int[4];
            decimal.GetBits(value, bits);
            foreach (int part in bits)
            {
                writer.Write(part);
            }
        }
    }
}
