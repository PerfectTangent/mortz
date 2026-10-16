using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Net;
using Mortz.Core.Features;
using Mortz.Protocol.Net;

namespace Mortz.Client.Session;

/// <summary>Owns the services that live for exactly one server connection:
/// created on connect, freed on disconnect, so a reconnect starts fresh.</summary>
[Meta(typeof(IAutoNode))]
public partial class ConnectedSession : Node, IProvide<FeatureScope>
{
    FeatureScope IProvide<FeatureScope>.Value() => _runtime.Features;

    /// <summary>For the session controller, which owns match lifecycle.</summary>
    public ClientConnectionScope Connection => _runtime;

    public override void _Notification(int what) => this.Notify(what);

    [Dependency] private NetRouter Router => this.DependOn<NetRouter>();
    [Dependency] private IClientSender Sender => this.DependOn<IClientSender>();
    [Dependency] private INetwork Network => this.DependOn<INetwork>();
    [Dependency] private ISessionExit SessionExit => this.DependOn<ISessionExit>();
    private ClientConnectionScope _runtime = null!;

    public void OnResolved()
    {
        _runtime = new ClientConnectionScope(Router, Sender, () => Network.LocalPeerId, SessionExit);
        this.Provide();
    }

    public void OnExitTree() => _runtime?.Dispose();
}
