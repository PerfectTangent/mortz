#if TOOLS
namespace Mortz.Server;

public partial class ServerMain
{
    partial void NotifyE2EListening() =>
        _pump.E2E?.OnListening(_host.Transport?.BoundPort ?? -1, BoundQueryPort);
}
#endif
