using Mortz.Content;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Sim;
using Mortz.Core.Terrain;
using Mortz.Server.Match;
using Mortz.Server.Players;
using Mortz.Server.Settings;
using Xunit;

namespace Mortz.Runtime.Tests.Core.Match;

public class ConfigurationVariantTests
{
    [Fact]
    public void EscalatingRespawnsRoundTripAndExposeOnlyTheirSettings()
    {
        MatchConfig config = new()
        {
            Rules = new ModeRules
            {
                Respawn = new EscalatingRespawnRules { BaseDelay = 1.25f, Increase = 0.5f, MaximumDelay = 7.5f },
            },
        };
        MatchConfigSnapshot snapshot = config.ToSnapshot();
        Assert.Equal(snapshot, snapshot.ToMutable().ToSnapshot());
        Assert.Equal(snapshot, MatchConfigCodec.FromBytes(config.ToBytes()).ToSnapshot());
        Assert.Equal(snapshot, MatchConfigCodec.FromBytes(snapshot.ToBytes()).ToSnapshot());
        ContentReadResult<RulesetManifest> read = TomlModel.Read<RulesetManifest>(
            TomlModel.Write(new RulesetManifest { Rules = config.Rules }));
        Assert.Empty(read.Diagnostics);
        Assert.Equal(snapshot, read.Value!.ToMatchConfigSnapshot());
        Assert.Equal(["BaseDelay", "Increase", "MaximumDelay"],
            RespawnRulesMetadata.For(config.Rules.Respawn).Categories
                .SelectMany(category => category.Properties).Select(property => property.Name));
        Assert.Equal(12, EscalatingRespawnRulesCodec.ToBytes((EscalatingRespawnRules)config.Rules.Respawn).Length);
    }

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
    public void InvalidEscalationFailsBeforeSimulation(float baseDelay, float increase, float maximum)
    {
        MatchConfig config = new()
        {
            Rules = new ModeRules
            {
                Respawn = new EscalatingRespawnRules { BaseDelay = baseDelay, Increase = increase, MaximumDelay = maximum },
            },
        };
        Assert.Contains(ModeComposition.Errors(config.Rules), error => error.Contains("escalating"));
        Assert.Throws<ArgumentException>(() => new SimWorld(new TerrainMask(8, 8, (_, _) => false, (_, _) => false), config));
        ContentReadResult<RulesetManifest> read = TomlModel.Read<RulesetManifest>(
            TomlModel.Write(new RulesetManifest { Rules = config.Rules }));
        Assert.Null(read.Value);
        Assert.NotEmpty(read.Diagnostics);
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(0, 0)]
    [InlineData(0, 0.001f)]
    public void EscalationWireRejectsInvalidCeilings(float baseDelay, float maximum)
    {
        MatchConfig config = new()
        {
            Rules = new ModeRules
            {
                Respawn = new EscalatingRespawnRules { BaseDelay = baseDelay, MaximumDelay = maximum },
            },
        };
        Assert.Throws<InvalidDataException>(() => MatchConfigCodec.FromBytes(config.ToBytes()));
    }

    [Fact]
    public void EscalationWirePreservesExistingScalarClamping()
    {
        MatchConfig config = new()
        {
            Rules = new ModeRules
            {
                Respawn = new EscalatingRespawnRules { BaseDelay = -1, Increase = float.NaN, MaximumDelay = float.PositiveInfinity },
            },
        };
        EscalatingRespawnRules decoded = Assert.IsType<EscalatingRespawnRules>(
            MatchConfigCodec.FromBytes(config.ToBytes()).Rules.Respawn);
        Assert.Equal(0, decoded.BaseDelay);
        Assert.Equal(0, decoded.Increase);
        Assert.Equal(EscalatingRespawnRules.MAXIMUM_DELAY_SECONDS, decoded.MaximumDelay);
        Assert.Null(decoded.ValidationError);
    }

    [Fact]
    public void DeadlockSettingsRoundTripThroughEveryConfigurationProjection()
    {
        DeadlockObjectiveRules rules = new() { CaptureSpeed = 7, DecaySpeed = 3, AbsenceDelay = 1.5f };
        Assert.True(rules.CanComplete);
        Assert.Equal(rules.ToSnapshot(), rules.ToSnapshot().ToMutable().ToSnapshot());
        Assert.Equal(90, rules.AbsenceDelayTicks);
        Assert.Contains(ObjectiveRulesMetadata.Variants, variant => variant.RulesType == typeof(DeadlockObjectiveRules));
        Assert.Equal(rules.ToSnapshot(), ObjectiveRulesCodec.FromBytes(ObjectiveRulesCodec.ToBytes(rules)).ToSnapshot());
        MatchConfig config = new()
        {
            Rules = new ModeRules
            {
                Teams = true,
                Objective = rules,
                EndCondition = new ObjectiveCompletionRules(),
                Winner = WinnerRule.QUALIFYING,
                Evaluation = EvaluationTiming.TICK,
                Replay = ReplayRule.NONE,
            },
        };
        Assert.Empty(ModeComposition.Errors(config.Rules));
        Assert.Equal(config.ToSnapshot(), MatchConfigCodec.FromBytes(config.ToBytes()).ToSnapshot());
        ContentReadResult<RulesetManifest> read = TomlModel.Read<RulesetManifest>(
            TomlModel.Write(new RulesetManifest { Rules = config.Rules }));
        Assert.Empty(read.Diagnostics);
        Assert.Equal(config.ToSnapshot(), read.Value!.ToMatchConfigSnapshot());
    }

    [Theory]
    [InlineData(false, WinnerRule.QUALIFYING, EvaluationTiming.TICK, "qualifier")]
    [InlineData(true, WinnerRule.QUALIFYING, EvaluationTiming.TICK, "objective capable")]
    [InlineData(true, WinnerRule.QUALIFYING, EvaluationTiming.ELIMINATION, "evaluation")]
    public void UnsupportedObjectiveCompositionsFailAtEveryEntryPoint(bool completion, WinnerRule winner,
        EvaluationTiming timing, string error)
    {
        MatchConfig config = new()
        {
            Rules = new ModeRules
            {
                EndCondition = completion ? new ObjectiveCompletionRules() : new ScoreTargetRules(),
                Winner = winner,
                Evaluation = timing,
            }
        };
        Assert.Contains(ModeComposition.Errors(config.Rules), message => message.Contains(error));
        Assert.Throws<InvalidDataException>(() => MatchConfigCodec.FromBytes(config.ToBytes()));
        ContentReadResult<RulesetManifest> read = TomlModel.Read<RulesetManifest>(
            TomlModel.Write(new RulesetManifest { Rules = config.Rules }));
        Assert.Null(read.Value);
        Assert.Contains(read.Diagnostics, diagnostic => diagnostic.Message.Contains(error));
        Assert.Throws<ArgumentException>(() => new MatchRuntime(
            new TerrainMask(8, 8, (_, _) => false, (_, _) => false), config, 0, new MatchStateKeys(1)));
    }

    [Fact]
    public void ObjectiveCompletionVariantHasNoTuningPayloadAndProjectsItsCapability()
    {
        ObjectiveCompletionRules rules = new();
        Assert.True(rules.SuppliesQualifier);
        Assert.IsType<ObjectiveCompletionRulesSnapshot>(rules.ToSnapshot());
        Assert.IsType<ObjectiveCompletionRules>(rules.ToSnapshot().ToMutable());
        Assert.IsType<ObjectiveCompletionRules>(EndConditionRulesCodec.FromBytes(EndConditionRulesCodec.ToBytes(rules)));
        Assert.Empty(ObjectiveCompletionRulesCodec.ToBytes(rules));
        Assert.Empty(EndConditionRulesMetadata.For(rules).Categories);
        Assert.Contains(EndConditionRulesMetadata.Variants, variant => variant.Id == "objective_completion");
        Assert.False(new NoObjectiveRules().CanComplete);
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
    public void ConfigWireContainsOnlyDiscriminatorsAndSelectedSettings()
    {
        Assert.Equal(Convert.FromHexString("046E6F6E6500000000"),
            ObjectiveRulesCodec.ToBytes(new NoObjectiveRules()));
        Assert.Equal(Convert.FromHexString("0566697865640400000000005040"),
            RespawnRulesCodec.ToBytes(new FixedRespawnRules { Delay = 3.25f }));
        Assert.Equal(Convert.FromHexString("0A73636F72655F6C6561640400000007000000"),
            EndConditionRulesCodec.ToBytes(new ScoreLeadRules { Target = 7 }));
        Assert.Empty(NoObjectiveRulesCodec.ToBytes(new NoObjectiveRules()));
        Assert.Empty(ObjectiveRulesMetadata.For(new NoObjectiveRules()).Categories);
        Assert.Equal(["Delay"], RespawnRulesMetadata.For(new FixedRespawnRules()).Categories
            .SelectMany(category => category.Properties).Select(property => property.Name));
        Assert.DoesNotContain(ModeRulesUiMetadata.Categories.SelectMany(category => category.Properties),
            property => property.Name is "Delay" or "RespawnDelay");
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
    public void InvalidCompositionIsRejectedByContentWireAndDirectMatchConstruction()
    {
        MatchConfig invalid = new()
        {
            Rules = new ModeRules { EndCondition = new TimeLimitRules() },
        };
        Assert.Contains(ModeComposition.Errors(invalid.Rules), error =>
            error.Contains("requires rules.evaluation = 'tick'", StringComparison.Ordinal));
        Assert.Throws<InvalidDataException>(() => MatchConfigCodec.FromBytes(invalid.ToBytes()));
        ContentReadResult<RulesetManifest> read = TomlModel.Read<RulesetManifest>("[rules.end_condition]\ntype = \"time_limit\"\nseconds = 60\n");
        Assert.Null(read.Value);
        Assert.Contains(read.Diagnostics, error => error.Message.Contains("evaluation", StringComparison.Ordinal));
        Assert.Throws<ArgumentException>(() => new MatchRuntime(
            new TerrainMask(8, 8, (_, _) => false, (_, _) => false), invalid, 0, new MatchStateKeys(1)));
        invalid.Rules.Evaluation = EvaluationTiming.TICK;
        Assert.Empty(ModeComposition.Errors(invalid.Rules));
    }

    [Fact]
    public void InvalidVariantsCannotHideBehindTheirSnapshotProjection()
    {
        MatchConfig config = new() { Rules = new ModeRules { Respawn = new UnknownRespawnRules() } };
        Assert.Contains(ModeComposition.Errors(config.Rules), error => error.Contains("rules.respawn"));
        Assert.Throws<ArgumentException>(() => new SimWorld(new TerrainMask(8, 8, (_, _) => false, (_, _) => false), config));
        config.Rules.Respawn = null!;
        Assert.Contains(ModeComposition.Errors(config.Rules), error => error.Contains("null"));
    }

    [Fact]
    public void DelayTuningPreservesModeIdentityAndProducesASettingsDelta()
    {
        GameModeManifest mode = new() { Name = "Default", FormatVersion = 1 };
        MatchConfig before = mode.ToMatchConfigSnapshot().ToMutable();
        MatchConfig after = before.ToSnapshot().ToMutable();
        ((FixedRespawnRules)after.Rules.Respawn).Delay = 9.5f;
        Assert.True(mode.Matches(after));
        Assert.Contains("rules.objective.type", mode.Identity);
        Assert.Contains("rules.respawn.type", mode.Identity);
        LobbySettingDelta delta = Assert.Single(LobbySettingsDiff.Between(before, after));
        Assert.Equal(new LobbySettingDelta("Respawn Delay", "2", "9.5"), delta);
    }

    public class UnknownRespawnRules : RespawnRules
    {
        public override RespawnRulesSnapshot ToSnapshot() => new FixedRespawnRulesSnapshot(1);
    }
}
