using Mortz.Server.Hooks;
using Mortz.Server.Phases;

namespace Mortz.Server.Match.Services;

public class EmptyMatchTimeout(
    MatchRuntime runtime,
    ServerClock clock,
    IPhaseTransitionRequests transitions)
    : IObserveMatchRoster, IAdvance
{
    private const ulong TIMEOUT_MS = 10_000;
    private ulong? _emptySinceMs;

    public void Advance(ServerTime time)
    {
        if (_emptySinceMs is not ulong emptySince ||
            time.Ms < emptySince ||
            time.Ms - emptySince < TIMEOUT_MS)
        {
            return;
        }

        transitions.RequestReturnToLobby();
    }

    public void RosterChanged()
    {
        if (runtime.SeatedPlayerCount > 0)
        {
            _emptySinceMs = null;
            return;
        }

        // Join-in-progress spectators do not restart an existing timeout.
        _emptySinceMs ??= clock.Ms;
    }
}
