namespace Mortz.Tools.Mapping;

public static class MapCommand
{
    private const string USAGE = "usage: dotnet run --project tools -- map schema | map build [<pack-dir>[/<map-id>]]";

    public static int Run(string[] args)
    {
        switch (args)
        {
            case ["schema"]:
                MapSchema.Write(Program.RepoRoot());
                return 0;
            case ["build"]:
                return MapBuild.Run(Program.RepoRoot(), null);
            case ["build", string target]:
                return MapBuild.Run(Program.RepoRoot(), target);
            default:
                throw new Exception(USAGE);
        }
    }
}
