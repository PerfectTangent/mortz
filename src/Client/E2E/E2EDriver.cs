#if TOOLS
using Godot;
using Mortz.Client.Logging;
using Mortz.E2E.Protocol;
using Serilog;

namespace Mortz.Client.E2E;

/// <summary>The only stdin reader in the game, inert unless E2ELaunch.Enabled. Drains the shared
/// <see cref="E2EDispatcher"/> in _Process, so no Godot object is touched off-thread.</summary>
public partial class E2EDriver : Node
{
    private static readonly ILogger _log = MortzLog.For("e2e");

    private const int MIN_PHYSICS_STEPS = 8;

    private E2EDispatcher? _dispatcher;
    private bool _quitting;

    /// <summary>For events outside a request, like a phase change.</summary>
    public IE2EResponder? Responder => _dispatcher?.Responder;

    public override void _Ready()
    {
        if (!E2ELaunch.Enabled)
        {
            SetProcess(false);
            return;
        }

        Engine.TimeScale = E2ELaunch.Timescale;
        Engine.MaxPhysicsStepsPerFrame =
            Math.Max(MIN_PHYSICS_STEPS, (int)(MIN_PHYSICS_STEPS * E2ELaunch.Timescale));
        _dispatcher = new E2EDispatcher((exception, message) => _log.Error(exception, "{Message}", message));
    }

    /// <summary>Called by the role handler once it can serve requests.</summary>
    public void Attach(IE2EHandler handler) => _dispatcher?.Attach(handler);

    public override void _Process(double delta)
    {
        if (_quitting || _dispatcher?.Drain() is not int exitCode)
            return;
        _quitting = true;
        // Finalize wrappers of freed scenes while the runtime is still healthy. Racing engine
        // teardown sometimes hits Godot's "!rc_owner" FATAL and exits 134.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(exitCode);
    }
}
#endif
