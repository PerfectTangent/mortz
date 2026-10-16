namespace Mortz.Client.Servers;

/// <summary>A favorite persisted in the local player profile. A Steam favorite has a
/// <paramref name="ServerId"/> and its address is only the FakeIP it last had.</summary>
public record FavoriteServer(string Address, int Port, string Label = "", int? QueryPort = null,
    string? ServerId = null);
