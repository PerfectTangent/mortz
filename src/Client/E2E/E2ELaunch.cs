#if TOOLS
using System.Globalization;

namespace Mortz.Client.E2E;

/// <summary>Behind #if TOOLS, so exports never contain it and need no runtime
/// template_release check.</summary>
public static class E2ELaunch
{
    private const double MIN_TIMESCALE = 1;
    private const double MAX_TIMESCALE = 4;

    public static bool Enabled { get; } = CmdArgs.HasFlag("--e2e");

    /// <summary>1 unless an E2E process asked for compressed real time.</summary>
    public static double Timescale { get; } = ReadTimescale();

    /// <summary>Delays the lobby and match screens, to test the server waits for a slow client.</summary>
    public static int ScreenLoadDelayMs { get; } = Enabled
        ? Math.Clamp(CmdArgs.GetInt("--e2e-screen-load-delay-ms", 0), 0, 15_000)
        : 0;

    private static double ReadTimescale()
    {
        if (!Enabled)
            return 1;
        string? value = CmdArgs.GetValue("--timescale");
        if (value == null || !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double timescale) ||
            !double.IsFinite(timescale))
        {
            return 1;
        }

        return Math.Clamp(timescale, MIN_TIMESCALE, MAX_TIMESCALE);
    }
}
#endif
