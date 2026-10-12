using Mortz.Content;

namespace Mortz.Tools.Mapping;

public static class PackCommand
{
    private const string USAGE = "usage: dotnet run --project tools -- pack new <dir> <id> <name>";

    public static void Run(string[] args)
    {
        switch (args)
        {
            case ["new", string directory, string id, string name]:
                New(directory, id, name);
                Console.WriteLine($"wrote {Path.Combine(directory, "content_pack.toml")}");
                return;
            default:
                throw new Exception(USAGE);
        }
    }

    public static void New(string directory, string id, string name)
    {
        name = name.Trim();
        id = id.Trim();
        if (name.Length == 0)
            throw new Exception("the pack needs a name");
        if (!IsValidPackId(id))
            throw new Exception($"bad pack id '{id}': use a dotted id like com.example.my-maps");
        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
            throw new Exception($"'{directory}' already exists and is not empty");

        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "content_pack.toml"),
            TomlModel.Write(new ContentPackManifest(id, name, "1.0.0")));
    }

    private static bool IsValidPackId(string id)
    {
        string[] parts = id.Split('.');
        return parts.Length >= 2 && parts.All(ContentId.IsValid);
    }
}
