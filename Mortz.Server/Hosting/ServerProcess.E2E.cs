#if MORTZ_E2E
using System.Globalization;
using Mortz.E2E.Protocol;
using Mortz.Server.Diagnostics;

namespace Mortz.Server.Hosting;

/// <summary>The E2E control channel. MORTZ_E2E is defined in Debug only, so in Release the
/// partial hooks compile to nothing.</summary>
public sealed partial class ServerProcess
{
    private const double MIN_TIMESCALE = 1;
    private const double MAX_TIMESCALE = 4;

    private E2EDispatcher? _testDispatcher;
    private ServerE2EHandler? _testHandler;

    partial void ConfigureE2E(ref double timescale, ref bool loopbackOnly)
    {
        if (!_args.HasFlag("--e2e"))
            return;
        loopbackOnly = true;
        if (double.TryParse(_args.GetValue("--timescale"), NumberStyles.Float, CultureInfo.InvariantCulture,
                out double requested) && double.IsFinite(requested))
            timescale = Math.Clamp(requested, MIN_TIMESCALE, MAX_TIMESCALE);
        _testDispatcher = new E2EDispatcher((exception, message) =>
            ServerLog.For("e2e").Error(exception, "{Message}", message));
    }

    partial void AttachE2E(List<object> diagnostics, ref IMatchControl control)
    {
        if (_testDispatcher == null)
            return;
        E2EMatchControl matchControl = new();
        _testHandler = new ServerE2EHandler(matchControl) { Events = { Responder = _testDispatcher.Responder } };
        _testDispatcher.Attach(_testHandler);
        diagnostics.Add(_testHandler.Events);
        control = matchControl;
    }

    partial void NotifyE2EListening(int gamePort, int queryPort) =>
        _testHandler?.OnListening(gamePort, queryPort);

    partial void DrainE2E(ref int? exitCode) => exitCode ??= _testDispatcher?.Drain();
}
#endif
