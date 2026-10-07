using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Objectives;
using Mortz.Core.Match.Teams;
using Mortz.Protocol.Net;

namespace Mortz.Protocol.Replication;

public abstract class ObjectiveWire
{
    public static ObjectiveWire None { get; } = new NoObjectiveWire();

    public static ObjectiveWire Create(ObjectiveRulesSnapshot rules) => rules switch
    {
        NoObjectiveRulesSnapshot => None,
        DeadlockObjectiveRulesSnapshot => new DeadlockWire(),
        _ => throw new ArgumentOutOfRangeException(nameof(rules)),
    };

    public abstract void Write(ref PacketWriter writer, ObjectiveState state);
    public abstract ObjectiveState Read(BinaryReader reader);

    public byte[] Serialize(ObjectiveState state) => PacketEncoder.Encode(state, this,
        static (ref PacketWriter writer, ObjectiveState value, ObjectiveWire wire) => wire.Write(ref writer, value));

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

    private class NoObjectiveWire : ObjectiveWire
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

    private class DeadlockWire : ObjectiveWire
    {
        public override void Write(ref PacketWriter writer, ObjectiveState state)
        {
            if (state is not DeadlockObjectiveState deadlock)
            {
                throw new InvalidDataException("Expected Deadlock objective state.");
            }
            WriteDecimal(ref writer, deadlock.BlueProgress);
            WriteDecimal(ref writer, deadlock.RedProgress);
            writer.Write((byte)((deadlock.BlueOccupied ? 1 : 0) | (deadlock.RedOccupied ? 2 : 0) |
                (deadlock.BlueDecaying ? 4 : 0) | (deadlock.RedDecaying ? 8 : 0)));
            writer.Write((ushort)deadlock.BlueGraceTicks);
            writer.Write((ushort)deadlock.RedGraceTicks);
            writer.Write((byte)(deadlock.Capture ?? 0));
        }

        public override ObjectiveState Read(BinaryReader reader)
        {
            decimal blue = reader.ReadDecimal();
            decimal red = reader.ReadDecimal();
            byte flags = reader.ReadByte();
            int blueGrace = reader.ReadUInt16();
            int redGrace = reader.ReadUInt16();
            byte capture = reader.ReadByte();
            return new DeadlockObjectiveState(DeadlockObjective.CAPACITY, blue, red,
                (flags & 1) != 0, (flags & 2) != 0, blueGrace, redGrace,
                (flags & 4) != 0, (flags & 8) != 0, capture == 0 ? null : (Team)capture);
        }

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
