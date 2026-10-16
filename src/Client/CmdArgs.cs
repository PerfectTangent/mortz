using Godot;

namespace Mortz.Client;

/// <summary>
/// User command-line args (everything after the `++` separator, e.g.
/// `Mortz.exe ++ --windowed --name Gil`).
/// </summary>
public static class CmdArgs
{
    private static string[] Args => OS.GetCmdlineUserArgs();

    public static bool HasFlag(string flag) => Args.Contains(flag);

    /// <summary>Value following <paramref name="flag"/>, or null.</summary>
    public static string? GetValue(string flag)
    {
        string[] args = Args;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == flag)
                return args[i + 1];
        }
        return null;
    }

    public static int GetInt(string flag, int fallback) =>
        int.TryParse(GetValue(flag), out int v) ? v : fallback;
}
