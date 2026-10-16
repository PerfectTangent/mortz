using Mortz.Core.Identity;

namespace Mortz.Server.Players;

/// <summary>One admitted connection's identity.</summary>
public sealed class Player(
    int peerId,
    string name,
    byte skin = 0,
    VerifiedAccount? account = null)
{
    public int PeerId { get; } = peerId;

    public string Name { get; } = name;

    public byte Skin { get; } = skin;

    public VerifiedAccount? Account { get; } = account;
}
