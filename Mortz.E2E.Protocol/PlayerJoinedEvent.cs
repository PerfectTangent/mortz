namespace Mortz.E2E.Protocol;

public record PlayerJoinedEvent(int PeerId, string Name, E2EPhase Phase) : E2EEvent;
