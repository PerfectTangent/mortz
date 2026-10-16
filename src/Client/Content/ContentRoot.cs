using Godot;

namespace Mortz.Client.Content;

/// <summary>Builds read content from content/ next to the executable, where the export
/// tool puts it.</summary>
public static class ContentRoot
{
    public static string Resolve()
    {
        string? configured = CmdArgs.GetValue("--content-root");
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(configured);

        return OS.HasFeature("editor")
            ? Path.GetFullPath(ProjectSettings.GlobalizePath("res://content"))
            : Path.GetFullPath(Path.Combine(OS.GetExecutablePath().GetBaseDir(), "content"));
    }
}
