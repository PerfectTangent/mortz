using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Match.Teams;
using Mortz.Core.Sim;
using Mortz.Core.Sim.Modifiers;

namespace Mortz.Core.Match.Objectives;

public class DeadlockObjective : MatchObjective
{
    public const int CAPACITY = 100 * SimConfig.TICK_RATE;

    private readonly int _capture;
    private readonly int _decay;
    private readonly int _grace;
    private readonly MapZone[] _zones = [];
    private int _blueAbsence;
    private int _redAbsence;
    private DeadlockObjectiveState _state;

    public DeadlockObjective(DeadlockObjectiveRulesSnapshot rules, MapZones zones)
        : this(CAPACITY, rules.CaptureSpeed, rules.DecaySpeed, rules.AbsenceDelayTicks)
    {
        _zones = zones.All.Where(zone => zone.HasTag(DeadlockObjectiveRules.CONTROL_TAG)).ToArray();
        if (_zones.Length == 0)
        {
            throw new ArgumentException(DeadlockObjectiveRules.MAP_ERROR, nameof(zones));
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
        _state = new DeadlockObjectiveState(capacity, 0, 0, false, false, 0, 0, false, false, null);
    }

    public override DeadlockObjectiveState State => _state;

    public override Victor? Winner => State.Capture is { } team ? new Victor.Team(team) : null;

    public override void Advance(SimWorld world)
    {
        bool blue = false;
        bool red = false;
        foreach (PlayerState player in world.Players.Values)
        {
            if (!player.IsAlive || player.Team == null)
            {
                continue;
            }
            foreach (MapZone zone in _zones)
            {
                if (zone.ContainsPlayer(player))
                {
                    blue |= player.Team == Team.BLUE;
                    red |= player.Team == Team.RED;
                    break;
                }
            }
            if (blue && red)
            {
                break;
            }
        }
        Advance(blue, red);
    }

    public void Advance(bool blueOccupied, bool redOccupied)
    {
        if (State.Capture.HasValue)
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
        _state = new DeadlockObjectiveState(State.Capacity, blue, red, blueOccupied, redOccupied,
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
    public decimal NeutralProgress => Capacity - BlueProgress - RedProgress;
    public bool IsDeadlocked => BlueOccupied && RedOccupied && NeutralProgress == 0 && Capture == null;
}
