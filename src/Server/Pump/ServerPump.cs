using System.Reflection;
using Godot;
using Mortz.Server.Admission;
using Mortz.Server.Diagnostics;
using Mortz.Server.Hosting;
using Mortz.Shared.Logging;

namespace Mortz.Server.Pump;

/// <summary>The only file that connects the engine to server logic.</summary>
[GlobalClass]
public partial class ServerPump : Node
{
    private GnsServerTransport? _transport;
    private ServerAdmission? _admission;

    public GameServer Server { get; private set; } = null!;

    partial void AttachE2E(List<object> diagnostics, ref IMatchControl control);

    public GameServer Start(ServerBootLoad load, GnsServerTransport transport, IAdmissionVerifier verifier)
    {
        _transport = transport;
        List<object> diagnostics = [];
        IMatchControl control = new NullMatchControl();
        AttachE2E(diagnostics, ref control);
        string version = typeof(ServerPump).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        Server = new GameServer(load.Boot, transport,
            new GodotMapSource(load.Content), MortzLog.For("server"),
            control, version, diagnostics);
        _admission = new ServerAdmission(verifier, transport, Time.GetTicksMsec);
        _admission.Admitted += Server.Connect;
        _admission.Departed += Server.Disconnect;
        transport.Attach(_admission, Server);
        return Server;
    }

    public void Stop()
    {
        if (_transport == null)
            return;
        _transport.Detach();
        _transport = null;
        _admission!.Dispose();
        _admission = null;
        Server.Dispose();
    }

    public void AdvanceAdmission() => _admission?.Advance(Time.GetTicksMsec());

    public override void _PhysicsProcess(double delta)
    {
        if (_transport == null)
            return;
        // Inbound traffic lands right before the step that consumes it.
        _transport.Pump();
        Server.Advance(new ServerTime(Time.GetTicksMsec(), delta));
    }
}
