using System.Net;
using Godot;
using Mortz.Core.Sim;
using Mortz.Net;
using Mortz.Net.Gns;
using Mortz.Protocol.Net;
using Mortz.Shared.Logging;
using Serilog;
#if TOOLS
using Mortz.Shared.E2E;
#endif

namespace Mortz.Server.Hosting;

[GlobalClass]
public partial class ServerHost : Node
{
    private static readonly ILogger _log = MortzLog.For("server");

    [Export] private string _defaultMap = "castlewars";

    public ServerBootLoad? Load { get; private set; }

    public GnsServerTransport? Transport { get; private set; }

    public override void _Ready() => Load = ServerBootLoader.TryLoad(_defaultMap);

    public bool Listen()
    {
        if (Load is not ServerBootLoad load)
            return false;
        ServerBoot boot = load.Boot;
        if (StartTransport(boot.GamePort) is not { } transport)
            return false;
        Transport = transport;

        int gamePort = transport.BoundPort;
        int queryPort = boot.QueryPort == 0 ? gamePort + 1 : boot.QueryPort;
        if (queryPort is < 1 or > 65535 || queryPort == gamePort)
        {
            _log.Error("bound game port {Port} needs an explicit distinct --query-port", gamePort);
            Stop();
            return false;
        }
        Load = load with { Boot = boot with { GamePort = gamePort, QueryPort = queryPort } };

        _log.Information(
            "'{Name}' listening on port {Port} (protocol v{Protocol}, " +
            "map '{Map}' {Width}x{Height}, tick {TickRate} Hz)",
            boot.Name, gamePort, NetConfig.PROTOCOL_VERSION, boot.Map.DisplayName,
            boot.Map.Width, boot.Map.Height, SimConfig.TICK_RATE);
        return true;
    }

    public void Stop()
    {
        Transport?.Dispose();
        Transport = null;
    }

    private static GnsServerTransport? StartTransport(int port)
    {
        if (GnsRuntime.Load() is not { } lib)
            return null;
        IPAddress bind = IPAddress.IPv6Any;
        double rateScale = 1;
#if TOOLS
        // E2E servers stay on loopback, and their game time outruns the wall clock the rate
        // limits refill against.
        if (E2ELaunch.Enabled)
        {
            bind = IPAddress.Loopback;
            rateScale = E2ELaunch.Timescale;
        }
#endif
        try
        {
            return GnsServerTransport.Listen(lib, bind, port, Time.GetTicksMsec, MortzLog.For("net"), rateScale);
        }
        catch (GnsException e)
        {
            _log.Error("failed to listen on port {Port}: {Reason}", port, e.Message);
            return null;
        }
    }
}
