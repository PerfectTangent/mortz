namespace Mortz.Server.Hosting;

/// <summary>Flag lookup over a process's user arguments, e.g. <c>--port 7778 --net-stats</c>.</summary>
public sealed class CommandLine(IReadOnlyList<string> args)
{
    public IReadOnlyList<string> Args { get; } = args;

    public bool HasFlag(string flag) => Args.Contains(flag);

    /// <summary>Value following <paramref name="flag"/>, or null.</summary>
    public string? GetValue(string flag)
    {
        for (int i = 0; i < Args.Count - 1; i++)
        {
            if (Args[i] == flag)
                return Args[i + 1];
        }
        return null;
    }

    public int GetInt(string flag, int fallback) =>
        int.TryParse(GetValue(flag), out int v) ? v : fallback;
}
