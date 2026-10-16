using System.Globalization;
using Mortz.Client.Logging;
using Mortz.Net.Gns;
using Serilog;
using Serilog.Events;

namespace Mortz.Client.Net;

/// <summary>Loads GNS, routes its log into ours and applies <c>--fake-lag &lt;ms&gt;</c> (round
/// trip) and <c>--fake-loss &lt;percent&gt;</c> (each way).</summary>
public static class GnsRuntime
{
    private static readonly ILogger _log = MortzLog.For("net");

    /// <summary>Null, with the reason logged, when the native library cannot be loaded.</summary>
    public static GnsLibrary? Load()
    {
        try
        {
            bool first = GnsLibrary.Log == null;
            GnsLibrary.Log ??= (level, message) =>
                _log.Write(level <= GnsLogLevel.ERROR ? LogEventLevel.Error
                    : LogEventLevel.Warning, "gns: {Message}", message.TrimEnd());
            GnsLibrary lib = GnsLibrary.Load();
            if (first)
                ApplyFakeNetwork(lib);
            return lib;
        }
        catch (Exception e) when (e is GnsException or DllNotFoundException or EntryPointNotFoundException)
        {
            _log.Error(e, "could not load the networking library");
            return null;
        }
    }

    private static void ApplyFakeNetwork(GnsLibrary lib)
    {
        int lagMs = CmdArgs.GetInt("--fake-lag", 0);
        float lossPercent = float.TryParse(CmdArgs.GetValue("--fake-loss"), NumberStyles.Float,
            CultureInfo.InvariantCulture, out float loss) ? loss : 0;
        if (lagMs <= 0 && lossPercent <= 0)
            return;
        _log.Information("simulating {LagMs} ms round-trip latency, {LossPercent}% loss each way",
            lagMs, lossPercent);
        lib.SetGlobalFakeNetwork(lagMs / 2, lagMs / 2, lossPercent, lossPercent);
    }
}
