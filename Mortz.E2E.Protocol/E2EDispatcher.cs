using System.Collections.Concurrent;
using System.Text.Json;

namespace Mortz.E2E.Protocol;

/// <summary>Reads E2E requests on a background thread. <see cref="Drain"/> answers ping and
/// shutdown itself and passes the rest to the role's handler, on the main loop's thread.</summary>
public sealed class E2EDispatcher
{
    private readonly ConcurrentQueue<string> _lines = new();
    private readonly E2EStdoutWriter _writer = new();
    private readonly Action<Exception?, string> _logError;

    private IE2EHandler? _handler;
    private volatile bool _armed;
    private volatile bool _eof;

    public E2EDispatcher(Action<Exception?, string> logError)
    {
        _logError = logError;
        Thread reader = new(ReadStdin) { IsBackground = true, Name = "e2e-stdin" };
        reader.Start();
    }

    /// <summary>For events outside a request, like a phase change.</summary>
    public IE2EResponder Responder => _writer;

    /// <summary>Called once the role handler can serve requests.</summary>
    public void Attach(IE2EHandler handler)
    {
        _handler = handler;
        _writer.Emit(new ProcessReadyEvent(handler.Role, Environment.ProcessId, E2EProtocolSchema.Hash));
    }

    /// <summary>Handles queued lines. Returns an exit code once the process should quit: after a
    /// shutdown request, or when the harness closed stdin.</summary>
    public int? Drain()
    {
        while (_lines.TryDequeue(out string? line))
        {
            if (Dispatch(line) is int exitCode)
                return Quit(exitCode);
        }
        return _eof ? Quit(E2EExitCode.STDIN_EOF) : null;
    }

    private int Quit(int exitCode)
    {
        _writer.Flush();
        return exitCode;
    }

    private void ReadStdin()
    {
        while (Console.In.ReadLine() is string line)
        {
            if (line.Length == 0)
                continue;
            _armed = true;
            _lines.Enqueue(line);
        }

        if (_armed)
        {
            _eof = true;
            return;
        }
        // --e2e under systemd, nohup or docker without a TTY gets instant EOF.
        // Exiting 0 there would look like a clean shutdown, so stay up and log it.
        _logError(null, "stdin closed before any request, ignoring EOF. --e2e needs a harness driving stdin");
    }

    private int? Dispatch(string line)
    {
        E2ERequest request;
        try
        {
            request = E2EWire.DeserializeRequest(line);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            Reject(line, exception);
            return null;
        }

        switch (request)
        {
            case PingRequest ping:
                _writer.Respond(new PongResponse(ping.Id, E2EProtocolSchema.Hash));
                return null;
            case ShutdownRequest shutdown:
                _writer.Respond(new ShutdownStartedResponse(shutdown.Id));
                return E2EExitCode.CLEAN;
        }

        if (_handler is not IE2EHandler handler)
        {
            Fail(request.Id, E2EError.INVALID_STATE, "No E2E handler is attached yet.");
            return null;
        }
        if (!AcceptedBy(handler.Role, request))
        {
            Fail(request.Id, E2EError.UNKNOWN_COMMAND,
                $"{request.GetType().Name} is not served by the {handler.Role} process.");
            return null;
        }

        handler.Handle(request, _writer);
        return null;
    }

    private static bool AcceptedBy(E2EProcessRole role, E2ERequest request) => role switch
    {
        E2EProcessRole.SERVER => request is IServerRequest,
        E2EProcessRole.CLIENT => request is IClientRequest,
        _ => false,
    };

    private void Reject(string line, Exception exception)
    {
        if (RecoverId(line) is Guid id)
        {
            Fail(id, E2EError.INVALID_REQUEST, exception.Message);
            return;
        }
        _logError(exception, "unparseable request line dropped");
    }

    private static Guid? RecoverId(string line)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return null;
            if (!document.RootElement.TryGetProperty("id", out JsonElement id))
                return null;
            return id.TryGetGuid(out Guid value) ? value : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void Fail(Guid id, E2EError error, string message) =>
        _writer.Respond(new CommandFailedResponse(id, error, message));
}
