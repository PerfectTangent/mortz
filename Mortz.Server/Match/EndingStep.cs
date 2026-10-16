using Mortz.Core.Match.Scoring;
using Mortz.Server.Match.Modes;

namespace Mortz.Server.Match;

/// <summary>Owns the winning transition and victory-lap countdown.</summary>
public class EndingStep(int victoryLapTicks)
{
    private readonly int _victoryLapTicks = Math.Max(0, victoryLapTicks);
    private int _ticksUntilLobby;

    public bool ReturnToLobby => Winner != null && _ticksUntilLobby == 0;

    public Victor? Winner { get; private set; }

    public FinalKillEvent? FinalKill { get; private set; }

    public void Apply(MatchContext match, MatchOutcome? outcome)
    {
        if (outcome == null)
            return;
        match.Stage = MatchStage.VICTORY_LAP;
        Winner = outcome.Winner;
        FinalKill = outcome.FinalKill;
        _ticksUntilLobby = _victoryLapTicks;
    }

    public bool AdvanceVictoryLap(MatchContext match)
    {
        if (match.Stage != MatchStage.VICTORY_LAP)
            throw new InvalidOperationException("The match is not in its victory lap.");

        return --_ticksUntilLobby <= 0;
    }
}
