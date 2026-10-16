using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Audio;
using Mortz.Client.Chat;
using Mortz.Client.Net;
using Mortz.Client.Players;
using Mortz.Core.Features;
using Mortz.Extensions;

namespace Mortz.Client.Views;

/// <summary>Pool of PlayerView instances, one per visible player: GameView pushes
/// placements between BeginFrame and Prune, anyone not placed despawns.</summary>
[Meta(typeof(IAutoNode))]
public partial class PlayerViewManager : Node2D
{
    [Export] private PackedScene _playerScene = null!;

    [Dependency] private INetwork Network => this.DependOn<INetwork>();

    [Dependency] private ISfx Sfx => this.DependOn<ISfx>();

    [Dependency] private FeatureScope Scope => this.DependOn<FeatureScope>();
    private ClientPlayers Players => Scope.Feature<ClientPlayers>();

    /// <summary>A remote player's rendered feet position this frame (lag probe tap).</summary>
    public event Action<Vector2>? RemotePlaced;

    private readonly Dictionary<int, PlayerView> _views = [];
    private readonly HashSet<int> _placed = new();

    public override void _Notification(int what) => this.Notify(what);

    public void OnResolved()
    {
        this.Subscribe(() => Players.Changed += OnPlayersChanged,
            () => Players.Changed -= OnPlayersChanged);
        this.Subscribe(() => Players.PlayerLeft += OnPlayerLeft,
            () => Players.PlayerLeft -= OnPlayerLeft);
        this.Subscribe(() => Players.MatchStatsChanged += OnMatchStatsChanged,
            () => Players.MatchStatsChanged -= OnMatchStatsChanged);
    }

    private void OnPlayersChanged()
    {
        foreach ((int peerId, PlayerView view) in _views)
        {
            ClientPlayer? player = Players.Find(peerId);
            if (player == null)
                continue;
            view.SetPlayerName(player.Name);
            view.SetTeam(player.Team);
        }
    }

    private void OnPlayerLeft(ClientPlayer player)
    {
        if (!_views.Remove(player.PeerId, out PlayerView? view))
            return;
        view.QueueFree();
    }

    private void OnMatchStatsChanged(ClientPlayer player)
    {
        if (player.Match != null && _views.TryGetValue(player.PeerId, out PlayerView? view))
            view.Configure(player.Match.Stats);
    }

    public PlayerView ViewForTest(int peerId) => _views[peerId];

    public void BeginFrame() => _placed.Clear();

    /// <summary>Called every render tick for every player.</summary>
    public void Place(
        int peerId,
        PlayerViewState state,
        MatchRenderMode mode = MatchRenderMode.LIVE)
    {
        // Buffered frames can still hold a player the roster just retired.
        if (Players.Find(peerId) is not ClientPlayer player)
            return;
        _placed.Add(peerId);
        bool isLocal = peerId == Network.LocalPeerId;
        ClientMatchPlayer match = player.Match ??
                                  throw new InvalidOperationException("Cannot place a player outside a match.");
        if (!isLocal)
            RemotePlaced?.Invoke(state.Feet);
        if (!_views.TryGetValue(peerId, out PlayerView? view))
        {
            view = _playerScene.Instantiate<PlayerView>();
            view.SetSfx(Sfx);
            view.Configure(match.Stats);
            view.SetIsLocal(isLocal);
            view.SetPlayerName(player.Name);
            view.SetTeam(player.Team);
            AddChild(view);
            _views[peerId] = view;
        }

        view.Apply(state, playTransitions: mode == MatchRenderMode.LIVE);
        bool typing = isLocal ? ChatInputGuard.IsTyping : player.IsTyping;
        view.SetTyping(mode == MatchRenderMode.LIVE && typing);
    }

    /// <summary>Despawn every view not placed since BeginFrame.</summary>
    public void Prune()
    {
        foreach (int peerId in _views.Keys.ToArray())
        {
            if (_placed.Contains(peerId))
                continue;
            _views[peerId].QueueFree();
            _views.Remove(peerId);
        }
    }
}
