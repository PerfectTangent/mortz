using Mortz.Content;
using Mortz.Core.Match.Configuration;
using Mortz.Server.Settings;
using Xunit;

namespace Mortz.Runtime.Tests.Core.Match;

public class ConfigurationVariantTests
{
    [Theory]
    [InlineData(-1, 1, 10)]
    [InlineData(1, -1, 10)]
    [InlineData(2, 1, 1)]
    [InlineData(0, 0, 0)]
    [InlineData(0, 0, 0.001f)]
    [InlineData(0, 1, 1093)]
    [InlineData(float.NaN, 1, 10)]
    [InlineData(1, float.PositiveInfinity, 10)]
    [InlineData(1, 1, float.PositiveInfinity)]
    public void InvalidEscalationIsRejectedByContent(float baseDelay, float increase, float maximum)
    {
        MatchConfig config = new()
        {
            Rules = new ModeRules
            {
                Respawn = new EscalatingRespawnRules { BaseDelay = baseDelay, Increase = increase, MaximumDelay = maximum },
            },
        };
        Assert.Contains(config.Rules.Errors(), error => error.Contains("escalating"));
        ContentReadResult<RulesetManifest> read = TomlModel.Read<RulesetManifest>(
            TomlModel.Write(new RulesetManifest { Rules = config.Rules }));
        Assert.Null(read.Value);
        Assert.NotEmpty(read.Diagnostics);
    }

    [Theory]
    [InlineData("[rules.end_condition]\ntype = \"none\"\n", "objective")]
    [InlineData("[rules.objective]\ntype = \"deadlock\"\n", "teams")]
    public void CompositionsThatCannotPlayAreRejectedByContent(string toml, string error)
    {
        ContentReadResult<RulesetManifest> read = TomlModel.Read<RulesetManifest>(toml);
        Assert.Null(read.Value);
        Assert.Contains(read.Diagnostics, diagnostic => diagnostic.Message.Contains(error));
    }

    [Fact]
    public void NoEndConditionHasNoTuningPayload()
    {
        NoEndConditionRules rules = new();
        Assert.IsType<NoEndConditionRulesSnapshot>(rules.ToSnapshot());
        Assert.IsType<NoEndConditionRules>(EndConditionRulesCodec.FromBytes(EndConditionRulesCodec.ToBytes(rules)));
        Assert.Empty(NoEndConditionRulesCodec.ToBytes(rules));
        Assert.Empty(EndConditionRulesMetadata.For(rules).Categories);
        Assert.Contains(EndConditionRulesMetadata.Variants, variant => variant.Id == "none");
    }

    [Fact]
    public void ActiveVariantsSurviveAllConfigurationProjections()
    {
        MatchConfig config = new()
        {
            Rules = new ModeRules
            {
                EndCondition = new ScoreLeadRules { Target = 7 },
                Respawn = new FixedRespawnRules { Delay = 3.25f },
                SpawnImmunity = 0.75f,
                SpectateDuringRespawn = false,
            },
        };
        MatchConfigSnapshot snapshot = config.ToSnapshot();
        MatchConfig copy = snapshot.ToMutable();
        Assert.NotSame(config.Rules.Objective, copy.Rules.Objective);
        Assert.NotSame(config.Rules.Respawn, copy.Rules.Respawn);
        Assert.Equal(snapshot, copy.ToSnapshot());
        Assert.Equal(snapshot, MatchConfigCodec.FromBytes(config.ToBytes()).ToSnapshot());
        Assert.Equal(snapshot, MatchConfigCodec.FromBytes(snapshot.ToBytes()).ToSnapshot());

        string toml = TomlModel.Write(new RulesetManifest
        {
            Rules = config.Rules,
            Physics = config.Physics,
            Combat = config.Combat,
        });
        ContentReadResult<RulesetManifest> read = TomlModel.Read<RulesetManifest>(toml);
        Assert.Empty(read.Diagnostics);
        Assert.Equal(snapshot, read.Value!.ToMatchConfigSnapshot());
        Assert.Contains("[rules.respawn]", toml);
        Assert.DoesNotContain("respawn_delay", toml);
        ((FixedRespawnRules)copy.Rules.Respawn).Delay = 8;
        Assert.Equal(3.25f, Assert.IsType<FixedRespawnRulesSnapshot>(snapshot.Rules.Respawn).Delay);
    }

    [Theory]
    [InlineData("end_condition", "tickets")]
    [InlineData("objective", "unimplemented")]
    [InlineData("respawn", "tickets")]
    [InlineData("respawn", "disabled")]
    public void UnimplementedVariantsAreRejectedByToml(string family, string variant)
    {
        ContentReadResult<RulesetManifest> read = TomlModel.Read<RulesetManifest>($"[rules.{family}]\ntype = \"{variant}\"\n");
        Assert.Null(read.Value);
        Assert.Contains(read.Diagnostics, error => error.Severity == ContentDiagnosticSeverity.ERROR &&
            error.Message.Contains(variant, StringComparison.Ordinal));
    }

    [Fact]
    public void UnknownWireVariantsAndMalformedSegmentsFailClearly()
    {
        byte[] unknown = Convert.FromHexString("046F6F707300000000");
        Assert.Contains("Unknown", Assert.Throws<InvalidDataException>(() =>
            ObjectiveRulesCodec.FromBytes(unknown)).Message);
        Assert.Throws<InvalidDataException>(() => RespawnRulesCodec.FromBytes(unknown));
        Assert.Throws<InvalidDataException>(() => EndConditionRulesCodec.FromBytes(unknown));
        Assert.Throws<InvalidDataException>(() =>
            ObjectiveRulesCodec.FromBytes(Convert.FromHexString("046E6F6E6501000000")));
        Assert.Throws<InvalidDataException>(() =>
            NoObjectiveRulesCodec.FromBytes([0]));
        byte[] valid = new MatchConfig().ToBytes();
        Assert.Throws<InvalidDataException>(() => MatchConfigCodec.FromBytes(valid[..^1]));
    }

    [Fact]
    public void DelayTuningPreservesModeIdentityAndProducesASettingsDelta()
    {
        GameModeManifest mode = new() { Name = "Default", FormatVersion = 1 };
        MatchConfig before = mode.ToMatchConfigSnapshot().ToMutable();
        MatchConfig after = before.ToSnapshot().ToMutable();
        ((FixedRespawnRules)after.Rules.Respawn).Delay = 9.5f;
        Assert.True(mode.Matches(after));
        LobbySettingDelta delta = Assert.Single(LobbySettingsDiff.Between(before, after));
        Assert.Equal(new LobbySettingDelta("Respawn Delay", "2", "9.5"), delta);
    }
}
