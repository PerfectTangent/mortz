#if TOOLS
using Godot;
using Mortz.Core.Match.Objectives;
using Mortz.Core.Sim;
using Mortz.E2E.Protocol;
using Mortz.Server.Diagnostics;
using Mortz.Server.Phases;
using Mortz.Shared.E2E;

namespace Mortz.Server.E2E;

/// <summary>The server end of the control protocol.</summary>
public partial class ServerE2EHandler : Node, IE2EHandler
{
    private E2EDriver _driver = null!;

    // Field-initialized so nothing depends on tree order. Real objects under
    // E2E, null objects otherwise. ServerPump reads them without a flag check.
    private E2EMatchControl _control = null!;

    private IE2EResponder? _responder;

    public E2EProcessRole Role => E2EProcessRole.SERVER;

    public ServerE2EEvents Events { get; } = new();

    /// <summary>Must be called right after instantiating, before entering the tree.</summary>
    public void Initialize(E2EDriver driver, E2EMatchControl control)
    {
        _driver = driver;
        _control = control;
    }

    public override void _Ready()
    {
        _responder = _driver.Responder;
        Events.Responder = _responder;
        _driver.Attach(this);
    }

    /// <summary>Called by ServerMain once the transport is bound, with the ports
    /// the OS actually handed out.</summary>
    public void OnListening(int gamePort, int queryPort) =>
        _responder?.Emit(new ServerListeningEvent(gamePort, queryPort));

    public void Handle(E2ERequest request, IE2EResponder responder)
    {
        switch (request)
        {
            case ServerStateRequest state:
                ReadState(state, responder);
                return;
            case MatchSetupRequest setup:
                InMatch(setup.Id, responder, () => _control.ReadSetup(
                    outcome => responder.Respond(new MatchSetupResponse(
                        setup.Id, outcome.Config, outcome.TerrainWidth, outcome.TerrainHeight))));
                return;
            case PlacePlayerRequest place:
                InMatch(place.Id, responder, () => _control.PlacePlayer(
                    place.PeerId, place.Position,
                    outcome => Complete(place.Id, outcome, responder)));
                return;
            case DamagePlayerRequest damage:
                InMatch(damage.Id, responder, () => _control.DamagePlayer(
                    damage.PeerId, damage.Amount,
                    outcome => Complete(damage.Id, outcome, responder)));
                return;
            default:
                responder.Respond(new CommandFailedResponse(request.Id,
                    E2EError.UNKNOWN_COMMAND,
                    $"The server has no handler for {request.GetType().Name}."));
                return;
        }
    }

    // ---- translation ----

    private void ReadState(ServerStateRequest request, IE2EResponder responder)
    {
        if (Events.Phase != ServerPhaseKind.MATCH)
        {
            responder.Respond(new ServerStateResponse(request.Id, E2EPhase.LOBBY, 0,
                Events.Names.Select(pair => new E2EServerPlayer(
                    pair.Key, pair.Value, Vec2.Zero, Vec2.Zero, 0, 0)).ToArray()));
            return;
        }
        _control.ReadState(outcome => responder.Respond(new ServerStateResponse(
            request.Id, E2EPhase.MATCH, outcome.Tick,
            outcome.Players.Select(player => new E2EServerPlayer(
                player.PeerId, Name(player.PeerId), player.Position, player.Velocity,
                player.Health, player.RespawnTicks)).ToArray())
        {
            Objective = outcome.Objective as DeadlockObjectiveState,
        }));
    }

    /// <summary>A mutation outside a match never reaches a tick boundary.
    /// Refused instead of hanging.</summary>
    private void InMatch(Guid id, IE2EResponder responder, Action enqueue)
    {
        if (Events.Phase != ServerPhaseKind.MATCH)
        {
            responder.Respond(new CommandFailedResponse(id, E2EError.INVALID_STATE,
                "The server is not in a match."));
            return;
        }
        enqueue();
    }

    private static void Complete(Guid id, MutationOutcome outcome, IE2EResponder responder)
    {
        if (outcome.Error is string error)
        {
            responder.Respond(new CommandFailedResponse(id, E2EError.INVALID_STATE, error));
            return;
        }
        responder.Respond(new PlayerPlacedResponse(id, outcome.AppliedTick));
    }

    private static void Complete(Guid id, DamageOutcome outcome, IE2EResponder responder)
    {
        if (outcome.Error is string error)
        {
            responder.Respond(new CommandFailedResponse(id, E2EError.INVALID_STATE, error));
            return;
        }
        responder.Respond(new PlayerDamagedResponse(
            id, outcome.RemainingHealth, outcome.Died, outcome.AppliedTick));
    }

    private new string Name(int peerId) =>
        Events.Names.TryGetValue(peerId, out string? name) ? name : $"Player {peerId}";
}
#endif
