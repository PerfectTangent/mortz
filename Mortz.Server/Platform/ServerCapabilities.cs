namespace Mortz.Server.Platform;

public readonly record struct ServerCapabilities(
    bool Query,
    bool Authentication,
    bool Publication,
    string Status);
