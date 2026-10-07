using Mortz.Client.Hooks;
using Mortz.Core.Features;
using Mortz.Protocol.Net;
using Mortz.Protocol.Net.Admin;

namespace Mortz.Client.Admin;

/// <summary>Connected-session admin authority: owns the handshake secrets and
/// signs privileged actions. Human-readable progress goes out as status lines;
/// chat displays them but owns none of this.</summary>
public class ClientAdmin(FeatureScope scope, IClientSender sender, Func<int> localPeerId) : IDisposable,
    IHandle<AdminChallengeMsg>,
    IHandle<AdminStateMsg>
{
    private readonly AdminAuthFlow _flow = new(sender);
    private bool _closed;

    public bool IsAdmin => _flow is { IsAdmin: true };
    public event Action<bool>? AdminChanged;

    public void Dispose()
    {
        _closed = true;
        _flow.Reset();
    }

    public void BeginAuthentication(string password)
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        if (localPeerId() == 0)
        {
            Status("Connect to a server before authenticating.");
            return;
        }
        bool wasAdmin = IsAdmin;
        _flow.Begin(password);
        if (wasAdmin)
            AdminChanged?.Invoke(false);
        Status("Requesting admin challenge...");
    }

    public bool TrySignAdminAction(byte action, ReadOnlySpan<byte> payload,
        out ulong sequence, out byte[] tag) =>
        _flow.TrySign(localPeerId(), action, payload, out sequence, out tag);

    public void Handle(in AdminChallengeMsg message)
    {
        if (_closed)
            return;
        if (!_flow.TryAnswerChallenge(localPeerId(), message))
            Status("Invalid admin challenge.");
    }

    public void Handle(in AdminStateMsg message)
    {
        if (_closed)
            return;
        bool wasAdmin = IsAdmin;
        _flow.ApplyState(message);
        Status(message.Status);
        if (wasAdmin != IsAdmin)
            AdminChanged?.Invoke(IsAdmin);
    }

    private void Status(string line) =>
        scope.Raise<IObserveAdminStatus>(feature => feature.AdminStatus(line));
}
