using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client;
using Mortz.Client.Logging;
using Mortz.Client.Net;

namespace Mortz;

/// <summary>Boot: provides the network to the client scene and enters it.</summary>
[Meta(typeof(IAutoNode))]
public partial class Main : Node, IProvide<NetworkManager>
{
    [Export] private NetworkManager _network = null!;
    [Export] private PackedScene _clientScene = null!;

    NetworkManager IProvide<NetworkManager>.Value() => _network;
    public override void _Notification(int what) => this.Notify(what);

    public void OnReady()
    {
        this.Provide();
        if (!CmdArgs.HasFlag("--windowed") && !OS.HasFeature("editor"))
            GoFullScreen();
        AddChild(_clientScene.Instantiate());
    }

    public override void _ExitTree() => MortzLog.Flush();

    private static void GoFullScreen()
    {
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
    }
}
