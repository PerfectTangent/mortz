using System.Diagnostics;
using System.Net;
using System.Reflection;
using Mortz.Core.Sim;
using Mortz.Net.Gns;
using Mortz.Protocol.Hosting;
using Mortz.Protocol.Net;
using Mortz.Server.Admission;
using Mortz.Server.Diagnostics;
using Mortz.Server.Discovery;
using Serilog;

namespace Mortz.Server.Hosting;

/// <summary>One dedicated server, driven by a fixed-step loop on the calling thread.</summary>
public sealed partial class ServerProcess : IDisposable
{
    private const string DEFAULT_MAP = "castlewars";

    private readonly ILogger _log = ServerLog.For("server");
    private readonly CommandLine _args;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly OwnedServerConnection? _owner;
    private readonly QueryResponder _query = new(ServerLog.For("server"));

    private GnsServerTransport? _transport;
    private ServerAdmission? _admission;
    private GameServer? _server;
    private Task? _startupReport;
    private bool _startupFailed;
    private readonly double _timescale = 1;
    private readonly bool _loopbackOnly;

    public ServerProcess(CommandLine args)
    {
        _args = args;
        string? pipeName = args.GetValue("--host-pipe");
        string? token = args.GetValue("--host-token");
        if (args.HasFlag("--host-pipe") || args.HasFlag("--host-token"))
            _owner = new OwnedServerConnection(pipeName ?? "", token ?? "");
        ConfigureE2E(ref _timescale, ref _loopbackOnly);
    }

    private ulong NowMs => (ulong)_clock.ElapsedMilliseconds;

    partial void ConfigureE2E(ref double timescale, ref bool loopbackOnly);
    partial void AttachE2E(List<object> diagnostics, ref IMatchControl control);
    partial void NotifyE2EListening(int gamePort, int queryPort);
    partial void DrainE2E(ref int? exitCode);

    /// <summary>Starts the server and runs it until it stops by itself or
    /// <paramref name="stopRequested"/> turns true. Returns the process exit code.</summary>
    public int Run(Func<bool> stopRequested)
    {
        Start();
        FixedStepLoop loop = new(SimConfig.TICK_RATE * _timescale, Math.Max(8, (int)(8 * _timescale)));
        return loop.Run(Step, () => stopRequested() ? 0 : Frame());
    }

    private void Start()
    {
        try
        {
            StartRuntime();
        }
        catch (Exception exception)
        {
            _log.Error(exception, "server startup failed");
            FailStartup("Local server startup failed. Check the server log.");
        }
    }

    private void StartRuntime()
    {
        string contentRoot = Path.GetFullPath(_args.GetValue("--content-root")
                                              ?? Path.Combine(AppContext.BaseDirectory, "content"));
        if (ServerBootLoader.TryLoad(_args, contentRoot, DEFAULT_MAP, _log, ServerLog.For("content"))
            is not ServerBootLoad load)
        {
            FailStartup("Local server could not load its content or settings. Check the server log.");
            return;
        }
        ServerBoot boot = load.Boot;
        if (Listen(boot.GamePort) is not GnsServerTransport transport)
        {
            FailStartup("Local server could not bind its game port. It may already be in use.");
            return;
        }
        _transport = transport;
        int gamePort = transport.BoundPort;
        int queryPort = boot.QueryPort == 0 ? gamePort + 1 : boot.QueryPort;
        if (queryPort is < 1 or > 65535 || queryPort == gamePort)
        {
            _log.Error("game port {Port} needs a distinct --query-port", gamePort);
            FailStartup("Local server needs a query port distinct from its game port.");
            return;
        }
        boot = boot with { GamePort = gamePort, QueryPort = queryPort };
        _log.Information(
            "'{Name}' listening on port {Port} (protocol v{Protocol}, " +
            "map '{Map}' {Width}x{Height}, tick {TickRate} Hz)",
            boot.Name, gamePort, NetConfig.PROTOCOL_VERSION, boot.Map.DisplayName,
            boot.Map.Width, boot.Map.Height, SimConfig.TICK_RATE);

        List<object> diagnostics = [];
        IMatchControl control = new NullMatchControl();
        AttachE2E(diagnostics, ref control);
        string version = typeof(ServerProcess).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        GameServer server = new(boot, transport, load.Maps, _log, control, version, diagnostics);
        _server = server;
        _admission = new ServerAdmission(transport);
        _admission.Admitted += server.Connect;
        _admission.Departed += server.Disconnect;
        transport.Attach(_admission, server);

        int boundQueryPort = _query.Start(queryPort, server.Describe, _loopbackOnly) ? queryPort : -1;
        _startupReport = _owner?.ReportAsync(new HostControlMessage(HostControlKind.READY, "", "127.0.0.1",
            gamePort, boundQueryPort));
        NotifyE2EListening(gamePort, boundQueryPort);
    }

    private GnsServerTransport? Listen(int port)
    {
        if (ServerNetwork.Load(_args, ServerLog.For("net")) is not GnsLibrary lib)
            return null;
        IPAddress bind = _loopbackOnly ? IPAddress.Loopback : IPAddress.IPv6Any;
        try
        {
            // E2E game time outruns the wall clock the rate limits refill against.
            return GnsServerTransport.Listen(lib, bind, port, () => NowMs, ServerLog.For("net"), _timescale);
        }
        catch (GnsException e)
        {
            _log.Error("failed to listen on port {Port}: {Reason}", port, e.Message);
            return null;
        }
    }

    private void FailStartup(string reason)
    {
        _startupFailed = true;
        _startupReport = _owner?.ReportAsync(new HostControlMessage(HostControlKind.FAILED, "", Reason: reason));
    }

    /// <summary>One simulation tick. Inbound traffic lands right before the step that consumes it.</summary>
    private void Step()
    {
        if (_transport == null || _server == null)
            return;
        _transport.Pump();
        _server.Advance(new ServerTime(NowMs, _timescale / SimConfig.TICK_RATE));
    }

    /// <summary>Everything that isn't the simulation, once per loop pass. Returns an exit code
    /// once the process should stop.</summary>
    private int? Frame()
    {
        if (_startupFailed)
            return _startupReport?.IsCompleted != false ? 1 : null;
        if (_owner?.StopRequested == true)
            return 0;
        if (_server != null)
        {
            ulong now = NowMs;
            _admission?.Advance(now);
            _query.Poll(now);
        }
        int? exitCode = null;
        DrainE2E(ref exitCode);
        return exitCode;
    }

    public void Dispose()
    {
        _transport?.Detach();
        _admission?.Dispose();
        _server?.Dispose();
        _query.Dispose();
        _transport?.Dispose();
        _owner?.Dispose();
    }
}
