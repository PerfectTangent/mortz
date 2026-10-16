using Serilog;
using Serilog.Events;

namespace Mortz.Server.Hosting;

/// <summary>Serilog for the dedicated server: console plus a daily file in <c>logs/</c> next to
/// the executable. Console-only when that directory isn't writable.</summary>
public static class ServerLog
{
    public const string LEVEL_FLAG = "--log-level";

    /// <summary>Bracketed tag every line carries, e.g. "net" or "server".</summary>
    private const string AREA = "Area";

    private const string CONSOLE_TEMPLATE =
        "{Timestamp:HH:mm:ss.fff} {Level:u3} [{Area}] {Message:lj}{NewLine}{Exception}";

    private const string FILE_TEMPLATE =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3} [{Area}] {Message:lj}{NewLine}{Exception}";

    private const int RETAINED_DAYS = 14;

    public static void Start(CommandLine args)
    {
        LoggerConfiguration config = new LoggerConfiguration()
            .MinimumLevel.Is(ResolveLevel(args))
            .Enrich.WithProperty(AREA, "server")
            .WriteTo.Console(outputTemplate: CONSOLE_TEMPLATE);
        string? fileError = null;
        try
        {
            string directory = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(directory);
            // shared: two servers started from one install write the same file.
            config.WriteTo.File(Path.Combine(directory, "server-.log"), outputTemplate: FILE_TEMPLATE,
                rollingInterval: RollingInterval.Day, retainedFileCountLimit: RETAINED_DAYS, shared: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            fileError = exception.Message;
        }
        Log.Logger = config.CreateLogger();
        if (fileError != null)
            For("server").Warning("no log file, console only: {Reason}", fileError);
    }

    /// <summary>A logger tagged with the area shown in brackets on every line.</summary>
    public static ILogger For(string area) => Log.Logger.ForContext(AREA, area);

    public static void Flush() => Log.CloseAndFlush();

    private static LogEventLevel ResolveLevel(CommandLine args) =>
        (args.GetValue(LEVEL_FLAG) ?? Environment.GetEnvironmentVariable("MORTZ_LOG_LEVEL"))
        ?.ToLowerInvariant() switch
        {
            "verbose" or "trace" => LogEventLevel.Verbose,
            "debug" => LogEventLevel.Debug,
            "info" or "information" => LogEventLevel.Information,
            "warn" or "warning" => LogEventLevel.Warning,
            "error" => LogEventLevel.Error,
            "fatal" => LogEventLevel.Fatal,
            _ => LogEventLevel.Information,
        };
}
