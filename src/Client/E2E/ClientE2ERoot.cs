#if TOOLS
using Godot;
using Mortz.Client.Match;
using Mortz.Client.Net;

namespace Mortz.Client.E2E;

/// <summary>
/// Builds the E2E nodes in code. A .tscn can't be preprocessed, so a scene declaring
/// them would reference scripts #if TOOLS strips from exports, and the game wouldn't
/// boot. Keep them out of ClientMain.tscn and GameView.tscn.
/// </summary>
public static class ClientE2ERoot
{
    public static ClientE2EHandler Attach(Node parent, NetworkManager network)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(network);
        E2EDriver driver = new() { Name = "E2EDriver" };
        parent.AddChild(driver);
        ClientE2EHandler handler = new() { Name = "E2E" };
        handler.Initialize(driver, network);
        parent.AddChild(handler);
        return handler;
    }

    /// <summary>The match probe, added once the game view is composed.</summary>
    public static void AttachMatch(GameView view, LocalPlayerController player)
    {
        ArgumentNullException.ThrowIfNull(view);
        E2EMatchProbe probe = new() { Name = "E2EMatchProbe" };
        probe.Initialize(view, player);
        view.AddChild(probe);
    }
}
#endif
