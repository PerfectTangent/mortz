using Mortz.Content;
using Serilog;

namespace Mortz.Server.Content;

public static class ContentLog
{
    public static void Print(ILogger log, IReadOnlyList<ContentDiagnostic> diagnostics)
    {
        foreach (ContentDiagnostic diagnostic in diagnostics)
        {
            if (diagnostic.Severity == ContentDiagnosticSeverity.ERROR)
                log.Error("{Diagnostic}", diagnostic);
            else
                log.Warning("{Diagnostic}", diagnostic);
        }
    }
}
