using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Players;
using Mortz.Core.Features;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Match.Teams;
using Mortz.Extensions;

namespace Mortz.Client.Match;

/// <summary>Mode-independent winner banner shown during the victory lap.</summary>
[Meta(typeof(IAutoNode))]
public partial class WinnerBanner : Control
{
    [Export] private Label _winnerLabel = null!;

    [Dependency] private FeatureScope Scope => this.DependOn<FeatureScope>();
    private ClientPlayers Players => Scope.Feature<ClientPlayers>();
    private ClientMatchState MatchState => Scope.Feature<ClientMatchState>();

    public override void _Notification(int what) => this.Notify(what);

    public void OnResolved()
    {
        this.Subscribe(() => MatchState.WinnerChanged += OnWinnerChanged,
            () => MatchState.WinnerChanged -= OnWinnerChanged);
        if (MatchState.Winner is Victor winner)
            OnWinnerChanged(winner);
    }

    private void OnWinnerChanged(Victor? winner)
    {
        if (winner == null)
            return;
        _winnerLabel.Text = $"{Describe(winner)} wins!";
        _winnerLabel.Visible = true;
    }

    private string Describe(Victor winner) => winner switch
    {
        Victor.Team team => Teams.Name(team.Value),
        Victor.Player player => Players.NameOf(player.PeerId),
        _ => throw new ArgumentOutOfRangeException(nameof(winner)),
    };
}
