using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Mortz.Server.Hosting;

/// <summary>Runs <c>step</c> at a fixed rate and <c>frame</c> after each batch of steps. Catch-up
/// stops at <paramref name="maxCatchUpSteps"/> and the rest of the backlog is dropped.</summary>
public sealed class FixedStepLoop(double stepsPerSecond, int maxCatchUpSteps)
{
    /// <summary>Returns <c>frame</c>'s exit code once it gives one.</summary>
    public int Run(Action step, Func<int?> frame)
    {
        bool raisedTimerResolution = OperatingSystem.IsWindows() && TimeBeginPeriod(1) == 0;
        try
        {
            long period = (long)Math.Round(Stopwatch.Frequency / stepsPerSecond);
            long due = Stopwatch.GetTimestamp();
            while (true)
            {
                long now = Stopwatch.GetTimestamp();
                for (int steps = 0; now >= due && steps < maxCatchUpSteps; steps++)
                {
                    step();
                    due += period;
                }
                if (now >= due)
                    due = now + period;
                if (frame() is int exitCode)
                    return exitCode;
                while (Stopwatch.GetTimestamp() < due)
                {
                    Thread.Sleep(1);
                }
            }
        }
        finally
        {
            if (raisedTimerResolution)
                _ = TimeEndPeriod(1);
        }
    }

    // Windows sleeps in ~15.6 ms slices unless a process asks for finer timers.
    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint milliseconds);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint milliseconds);
}
