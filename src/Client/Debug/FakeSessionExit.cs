using Mortz.Client.Logging;
using Mortz.Client.Session;
using Serilog;

namespace Mortz.Client.Debug;

public sealed class FakeSessionExit : ISessionExit
{
    private static readonly ILogger _log = MortzLog.For("client");

    public void LeaveSession(string reason) =>
        _log.Information("LeaveSession: {Reason}", reason);
}
