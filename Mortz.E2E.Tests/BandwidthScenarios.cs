using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Mortz.Core.Sim;
using Mortz.E2E.Protocol;
using Mortz.E2E.Tests.Harness;
using Xunit;

namespace Mortz.E2E.Tests;

/// <summary>
/// Worst-case bandwidth per player: a full server on the biggest map, everyone firing nonstop,
/// then a late joiner who needs the carved terrain. Explicit because it takes minutes, run with
/// <c>dotnet test Mortz.E2E.Tests --filter "FullyQualifiedName~BandwidthScenarios"</c> plus
/// <c>-- xUnit.Explicit=on</c>. The report goes to the test output and bandwidth.txt in the
/// artifact directory.
/// </summary>
public sealed partial class BandwidthScenarios
{
    private const int MATCH_SECONDS = 60;
    private const int LATE_SECONDS = 20;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [GeneratedRegex(@"\[stats\] .*? sent=(\d+)B/(\d+)pk recv=(\d+)B/(\d+)pk snap_app=(\d+)B mortar_app=(\d+)B input_app=(\d+)B(.*)$")]
    private static partial Regex StatsLine();

    [GeneratedRegex(@"terrain sync to (\d+): ""?(\w+)""?, (\d+) B in (\d+) chunk\(s\), (\d+) carve\(s\)")]
    private static partial Regex TerrainLine();

    [Fact(Explicit = true)]
    public async Task FullServerUnderConstantFireOnTheBiggestMap()
    {
        string artifacts;
        int players = SimConfig.MAX_PLAYERS;
        await using (MortzScenario scenario = await MortzScenario.StartAsync(new ScenarioOptions
        {
            Name = nameof(FullServerUnderConstantFireOnTheBiggestMap),
            MapId = "castlestrike",
            Mode = null,
            RulesetPath = Path.Combine(RepoRoot.Path, "Mortz.E2E.Tests", "Rulesets", "endless_deathmatch.toml"),
            Players = [.. Enumerable.Range(1, players - 1).Select(i => $"p{i}")],
            AllowJoinInProgress = true,
            NetStats = true,
        }, Cancellation))
        {
            artifacts = scenario.ArtifactDirectory;
            await scenario.ReadyAllAsync(Cancellation);
            int seed = 0;
            foreach (ClientDriver client in scenario.Clients)
            {
                await client.RunPlanAsync(Mayhem(seed++, (MATCH_SECONDS + LATE_SECONDS) * SimConfig.TICK_RATE), Cancellation);
            }
            await Task.Delay(TimeSpan.FromSeconds(MATCH_SECONDS), Cancellation);

            // The last seat fills mid-match, after a minute of carving.
            await scenario.AddClientAsync("late", Cancellation);
            ClientDriver late = scenario.Client("late");
            await late.Events.WaitAsync<SnapshotObservedEvent>(_ => true, scenario.Budget.MatchEvent,
                cancellationToken: Cancellation);
            await late.RunPlanAsync(Mayhem(seed, LATE_SECONDS * SimConfig.TICK_RATE), Cancellation);
            await Task.Delay(TimeSpan.FromSeconds(LATE_SECONDS), Cancellation);
        }

        string report = Report(File.ReadAllLines(Path.Combine(artifacts, "server.log")));
        File.WriteAllText(Path.Combine(artifacts, "bandwidth.txt"), report);
        TestContext.Current.TestOutputHelper?.WriteLine(report);
    }

    /// <summary>Run, jump, dash, rope and fire in both directions while sweeping the aim, so
    /// every player keeps shells in the air and terrain carving.</summary>
    private static BotInputPlan Mayhem(int seed, int ticks)
    {
        List<BotInputFrame> frames = [];
        int aim = seed * 31;
        for (int tick = 0; tick < ticks; tick += 20)
        {
            aim = (aim + 37) % 256;
            frames.Add(new BotInputFrame(InputButtons.RIGHT | InputButtons.JUMP | InputButtons.FIRE, (byte)aim, 5));
            frames.Add(new BotInputFrame(InputButtons.RIGHT, (byte)aim, 5));
            frames.Add(new BotInputFrame(InputButtons.LEFT | InputButtons.DASH | InputButtons.FIRE, (byte)(255 - aim), 5));
            frames.Add(new BotInputFrame(InputButtons.LEFT | InputButtons.ROPE, (byte)(255 - aim), 5));
        }
        return BotInputPlan.Sequence([.. frames]);
    }

    private readonly record struct Second(int Players, long Sent, long Recv, long SentPackets,
        long RecvPackets, long Snapshot, long Mortar, long Input);

    private static string Report(string[] serverLog)
    {
        List<Second> seconds = [];
        List<(int Peer, string Encoding, long Bytes, int Carves)> terrain = [];
        foreach (string line in serverLog)
        {
            if (StatsLine().Match(line) is { Success: true } s)
            {
                int players = Regex.Count(s.Groups[8].Value, @"peer=\d+");
                seconds.Add(new Second(players, Long(s, 1), Long(s, 3), Long(s, 2), Long(s, 4),
                    Long(s, 5), Long(s, 6), Long(s, 7)));
            }
            else if (TerrainLine().Match(line) is { Success: true } t)
            {
                terrain.Add((int.Parse(t.Groups[1].Value, CultureInfo.InvariantCulture), t.Groups[2].Value,
                    Long(t, 3), int.Parse(t.Groups[5].Value, CultureInfo.InvariantCulture)));
            }
        }
        // GNS rates take a few seconds to settle after connecting.
        Second[] steady = [.. seconds.Skip(3).Where(second => second.Players > 0)];
        Assert.NotEmpty(steady);

        StringBuilder text = new();
        text.AppendLine(CultureInfo.InvariantCulture, $"{steady.Length} seconds measured, up to {steady.Max(s => s.Players)} players");
        Row(text, "server sent (all players)", steady.Select(s => (double)s.Sent));
        Row(text, "server received (all players)", steady.Select(s => (double)s.Recv));
        Row(text, "per player download", steady.Select(s => (double)s.Sent / s.Players));
        Row(text, "per player upload", steady.Select(s => (double)s.Recv / s.Players));
        Row(text, "per player download packets/s", steady.Select(s => (double)s.SentPackets / s.Players), unit: "");
        Row(text, "snapshot payload (all players)", steady.Select(s => (double)s.Snapshot));
        Row(text, "mortar payload (all players)", steady.Select(s => (double)s.Mortar));
        double perPlayerPerHour = steady.Average(s => (double)(s.Sent + s.Recv) / s.Players) * 3600 / 1_000_000;
        text.AppendLine(CultureInfo.InvariantCulture, $"per player, both directions: {perPlayerPerHour:F1} MB/hour on average");
        foreach ((int peer, string encoding, long bytes, int carves) in terrain)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"terrain sync to peer {peer}: {bytes / 1024.0:F1} KB ({encoding}, {carves} carves)");
        }

        return text.ToString();
    }

    private static void Row(StringBuilder text, string label, IEnumerable<double> values, string unit = "B")
    {
        double[] all = [.. values];
        double[] sorted = [.. all.Order()];
        double p95 = sorted[(int)Math.Ceiling(sorted.Length * 0.95) - 1];
        text.AppendLine(CultureInfo.InvariantCulture,
            $"{label,-34} avg {all.Average(),9:F0} {unit}/s   p95 {p95,9:F0} {unit}/s   max {sorted[^1],9:F0} {unit}/s");
    }

    private static long Long(Match match, int group) =>
        long.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);
}
