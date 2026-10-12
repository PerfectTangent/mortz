using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Mortz.Content;
using Mortz.Core.Match.Teams;
using Mortz.Core.Sim;
using Mortz.Core.Sim.Modifiers;

namespace Mortz.Tools.Mapping;

public static class MapSchema
{
    private const int VERSION = 1;
    private static readonly string[] _schemaPath = ["tools", "Mapping", "Krita", "mortz", "schema.json"];

    public static void Write(string root)
    {
        (int viewportWidth, int viewportHeight) = Viewport(root);
        var schema = new
        {
            version = VERSION,
            stats = Names<Stat>(),
            ops = Names<StatOp>(),
            teams = Names<Team>(),
            idPattern = ContentId.PATTERN,
            // MapPackageLoader collides on alpha > 0.5, i.e. byte alpha >= 128.
            collisionAlpha = 128,
            limits = new
            {
                minWidth = MapDimensions.MIN_WIDTH,
                minHeight = MapDimensions.MIN_HEIGHT,
                maxSize = MapDimensions.MAX_SIZE,
                minPlayers = 1,
                maxPlayers = SimConfig.MAX_PLAYERS,
                maxEffectZones = MapZones.MAX_EFFECT_ZONES,
            },
            guides = new
            {
                viewport = new { width = viewportWidth, height = viewportHeight },
                body = new { width = 2 * SimConfig.PLAYER_HALF_WIDTH, height = 2 * SimConfig.PLAYER_HALF_HEIGHT },
                jump = Round(Rise(SimConfig.JUMP_SPEED)),
                // A ground jump, then an air jump pressed near its peak.
                doubleJump = Round(Chain(SimConfig.JUMP_SPEED, SimConfig.AIR_JUMP_SPEED)),
                // Grounded, jump always means a ground jump, so a wall jump comes after one.
                wallJump = Round(Chain(SimConfig.JUMP_SPEED, SimConfig.WALL_JUMP_SPEED_Y)),
                rope = SimConfig.ROPE_MAX_RANGE,
                blastRadius = SimConfig.MORTAR_CARVE_RADIUS,
                blastCoreRadius = SimConfig.MORTAR_CARVE_RADIUS * SimConfig.BLAST_CORE_FRACTION,
                parryRadius = SimConfig.PARRY_RADIUS,
                // Range on flat ground at a 45 degree aim.
                mortarRange = Round((double)SimConfig.MORTAR_SPEED * SimConfig.MORTAR_SPEED / SimConfig.MORTAR_GRAVITY),
            },
        };
        string path = Path.Combine([root, .. _schemaPath]);
        File.WriteAllText(path, JsonSerializer.Serialize(schema, new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n");
        Console.WriteLine($"wrote {Path.GetRelativePath(root, path)}");
    }

    private static string[] Names<T>() where T : struct, Enum =>
        [.. Enum.GetValues<T>().Select(value => (string)TomlModel.WriteScalar(value))];

    // How far off the peak an average player presses the next jump. The guides show what they
    // still reach, not the tick-perfect best.
    private const int TIMING_SLACK_TICKS = 6; // 100 ms at 60 ticks/s

    private static double Rise(float speed) => HeightAfter(speed, PeakTicks(speed));

    // A jump, then a second one pressed TIMING_SLACK_TICKS before or after the first one's peak,
    // whichever reaches less.
    private static double Chain(float first, float second)
    {
        int peak = PeakTicks(first);
        return Math.Min(HeightAfter(first, peak - TIMING_SLACK_TICKS),
                   HeightAfter(first, peak + TIMING_SLACK_TICKS)) + Rise(second);
    }

    // Ticks a jump launched at this speed keeps rising.
    private static int PeakTicks(float speed) =>
        (int)Math.Ceiling(speed / (SimConfig.GRAVITY * SimConfig.DT));

    // Height gained this many ticks after a jump at this speed, tick by tick like PlayerSim: the
    // launch tick moves at full speed, then gravity lands before each later move. Past the peak it
    // falls back down.
    private static double HeightAfter(float speed, int ticks)
    {
        double height = 0;
        for (int tick = 0; tick < ticks; tick++)
        {
            height += (speed - tick * SimConfig.GRAVITY * SimConfig.DT) * SimConfig.DT;
        }

        return height;
    }

    private static double Round(double value) => Math.Round(value, 1);

    private static (int Width, int Height) Viewport(string root)
    {
        string settings = File.ReadAllText(Path.Combine(root, "project.godot"));
        return (Setting(settings, "viewport_width"), Setting(settings, "viewport_height"));
    }

    private static int Setting(string settings, string name)
    {
        Match match = Regex.Match(settings, $@"^window/size/{name}=(\d+)\s*$", RegexOptions.Multiline);
        if (!match.Success)
            throw new Exception($"window/size/{name} not found in project.godot");
        return int.Parse(match.Groups[1].Value);
    }
}
