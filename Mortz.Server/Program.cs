using System.Runtime.InteropServices;
using Mortz.Server.Hosting;

CommandLine commandLine = new(args);
ServerLog.Start(commandLine);

int stopRequested = 0;
using PosixSignalRegistration interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, Stop);
using PosixSignalRegistration terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, Stop);

int exitCode;
using (ServerProcess server = new(commandLine))
    exitCode = server.Run(() => Volatile.Read(ref stopRequested) != 0);
ServerLog.Flush();
return exitCode;

void Stop(PosixSignalContext context)
{
    // Let the loop finish its pass and shut down cleanly instead of dying mid-tick.
    context.Cancel = true;
    Volatile.Write(ref stopRequested, 1);
}
