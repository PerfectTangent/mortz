using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Teams;
using Mortz.Core.Sim;

namespace Mortz.Core.Match.Objectives;

public class DeadlockObjective
{
    private readonly int _capture;
    private readonly int _decay;
    private readonly int _grace;
    private int _blueAbsence;
    private int _redAbsence;

    public DeadlockObjective(DeadlockObjectiveRules rules)
        : this(100 * SimConfig.TICK_RATE, rules.CaptureSpeed, rules.DecaySpeed,
            rules.AbsenceDelayTicks)
    {
        if (!float.IsFinite(rules.AbsenceDelay) || rules.AbsenceDelay < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rules));
        }
    }

    public DeadlockObjective(int capacity, int capturePerTick, int decayPerTick, int absenceDelayTicks)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capturePerTick);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(decayPerTick);
        ArgumentOutOfRangeException.ThrowIfNegative(absenceDelayTicks);
        _capture = capturePerTick;
        _decay = decayPerTick;
        _grace = absenceDelayTicks;
        State = new DeadlockObjectiveState(capacity, 0, 0, false, false, 0, 0, false, false, null);
    }

    public DeadlockObjectiveState State { get; private set; }

    public void Advance(bool blueOccupied, bool redOccupied)
    {
        if (State.CapturedBy.HasValue)
        {
            return;
        }

        bool blueDecays = SamplesAbsent(blueOccupied, ref _blueAbsence);
        bool redDecays = SamplesAbsent(redOccupied, ref _redAbsence);
        decimal blue = blueDecays ? Math.Max(0, State.BlueProgress - _decay) : State.BlueProgress;
        decimal red = redDecays ? Math.Max(0, State.RedProgress - _decay) : State.RedProgress;
        decimal gap = State.Capacity - blue - red;
        if (blueOccupied && redOccupied)
        {
            if (gap <= 2m * _capture)
            {
                // Resolve contact symmetrically, including fractional meeting points.
                decimal difference = blue - red;
                blue = (State.Capacity + difference) / 2;
                red = (State.Capacity - difference) / 2;
            }
            else
            {
                blue += _capture;
                red += _capture;
            }
        }
        else if (blueOccupied)
        {
            blue = Math.Min(blue + _capture, State.Capacity - red);
        }
        else if (redOccupied)
        {
            red = Math.Min(red + _capture, State.Capacity - blue);
        }

        Team? captured = blue == State.Capacity ? Team.BLUE : red == State.Capacity ? Team.RED : null;
        State = new DeadlockObjectiveState(State.Capacity, blue, red, blueOccupied, redOccupied,
            blueOccupied || blueDecays ? 0 : _grace - _blueAbsence + 1,
            redOccupied || redDecays ? 0 : _grace - _redAbsence + 1, blueDecays, redDecays, captured);
    }

    private bool SamplesAbsent(bool occupied, ref int absence)
    {
        if (occupied)
        {
            absence = 0;
            return false;
        }
        if (absence == _grace)
        {
            return true;
        }
        absence++;
        return false;
    }
}

/// <summary>Progress is measured against Capacity. grace is ticks until decay if the team stays absent.</summary>
public record DeadlockObjectiveState(int Capacity, decimal BlueProgress, decimal RedProgress,
    bool BlueOccupied, bool RedOccupied, int BlueGraceTicks, int RedGraceTicks,
    bool BlueDecaying, bool RedDecaying, Team? Capture) : ObjectiveState
{
    public override Team? CapturedBy => Capture;
    public decimal NeutralProgress => Capacity - BlueProgress - RedProgress;
    public bool IsDeadlocked => BlueOccupied && RedOccupied && NeutralProgress == 0 && Capture == null;
}
