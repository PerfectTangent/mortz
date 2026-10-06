using System.Globalization;
using Godot;
using Mortz.Core.Match.Objectives;
using Mortz.Core.Match.Teams;
using Mortz.Core.Sim;

namespace Mortz.Client.Ui;

[Tool, GlobalClass]
public partial class DeadlockHud : Control
{
    public enum PreviewState { CAPTURING, DEADLOCKED, GRACE, DECAYING, CAPTURED }

    [Export] private ColorRect _blue = null!;
    [Export] private ColorRect _red = null!;
    [Export] private Label _blueProgress = null!;
    [Export] private Label _redProgress = null!;
    [Export] private Label _blueStatus = null!;
    [Export] private Label _redStatus = null!;
    [Export] private Label _status = null!;
    private PreviewState _preview;

    [Export]
    public PreviewState Preview
    {
        get => _preview;
        set
        {
            _preview = value;
            if (Engine.IsEditorHint() && IsNodeReady())
            {
                ShowPreview();
            }
        }
    }

    public override void _Ready()
    {
        if (Engine.IsEditorHint())
        {
            ShowPreview();
        }
    }

    public void Present(ObjectiveState objective)
    {
        Visible = objective is DeadlockObjectiveState;
        if (objective is not DeadlockObjectiveState state)
        {
            return;
        }
        float blue = (float)(state.BlueProgress / state.Capacity);
        float red = (float)(state.RedProgress / state.Capacity);
        _blue.AnchorRight = blue;
        _red.AnchorLeft = 1 - red;
        _blueProgress.Text = $"BLUE  {(int)(100 * state.BlueProgress / state.Capacity)}%";
        _redProgress.Text = $"{(int)(100 * state.RedProgress / state.Capacity)}%  RED";
        _status.Text = state.CapturedBy is Team winner ? $"{winner} CAPTURED" :
            state.IsDeadlocked ? "DEADLOCK" : "CONTROL";
        _blueStatus.Text = TeamStatus(state, true);
        _redStatus.Text = TeamStatus(state, false);
    }

    private static string TeamStatus(DeadlockObjectiveState state, bool blue)
    {
        if (state.CapturedBy != null)
        {
            return state.CapturedBy == (blue ? Team.BLUE : Team.RED) ? "Captured" : "Lost";
        }
        bool occupied = blue ? state.BlueOccupied : state.RedOccupied;
        decimal progress = blue ? state.BlueProgress : state.RedProgress;
        bool decaying = blue ? state.BlueDecaying : state.RedDecaying;
        int grace = blue ? state.BlueGraceTicks : state.RedGraceTicks;
        if (state.IsDeadlocked) return "Contested";
        if (occupied) return state.NeutralProgress > 0 || (blue ? state.RedDecaying : state.BlueDecaying)
            ? "Capturing" : "Holding";
        if (progress == 0) return "Enter a control zone";
        if (decaying) return "Decaying";
        return $"Decay in {(grace / (float)SimConfig.TICK_RATE).ToString("0.0", CultureInfo.InvariantCulture)}s";
    }

    private void ShowPreview() => Present(_preview switch
    {
        PreviewState.DEADLOCKED => new DeadlockObjectiveState(6000, 3900, 2100, true, true, 0, 0, false, false, null),
        PreviewState.GRACE => new DeadlockObjectiveState(6000, 3900, 2100, true, false, 0, 90, false, false, null),
        PreviewState.DECAYING => new DeadlockObjectiveState(6000, 4200, 1800, true, false, 0, 0, false, true, null),
        PreviewState.CAPTURED => new DeadlockObjectiveState(6000, 6000, 0, true, false, 0, 0, false, true, Team.BLUE),
        _ => new DeadlockObjectiveState(6000, 1800, 1200, true, true, 0, 0, false, false, null),
    });
}
