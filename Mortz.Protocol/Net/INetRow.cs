namespace Mortz.Protocol.Net;

/// <summary>Mortz.Generators writes this for every [NetRow] type, never implement it by hand.</summary>
public interface INetRow<T>
    where T : struct, INetRow<T>
{
    static abstract void WriteTo(BinaryWriter w, in T row);

    static abstract T ReadFrom(BinaryReader r);
}
