using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mortz.Generators;
using Xunit;

namespace Mortz.Runtime.Tests.Core.Match;

public sealed class ConfigGeneratorTests
{
    [Fact]
    public void UnprojectedMatchConfigSectionFailsGeneration()
    {
        const string SOURCE = """
            namespace Mortz.Core.Match.Configuration;

            [System.AttributeUsage(System.AttributeTargets.Property)]
            public sealed class ConfigSectionAttribute : System.Attribute;

            public sealed class Known;
            public sealed class Missing;

            public sealed partial class MatchConfig
            {
                [ConfigSection]
                public Known Known { get; init; } = new();

                public Missing Missing { get; init; } = new();
            }
            """;

        GeneratorDriverRunResult result = Run(SOURCE);

        Diagnostic diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "MZ3007");
        Assert.Contains("MatchConfig.Missing", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void UnprojectedSectionFieldFailsGeneration()
    {
        const string SOURCE = """
            namespace Mortz.Core.Match.Configuration;

            [System.AttributeUsage(System.AttributeTargets.Property)]
            public sealed class MatchRuleAttribute(
                float min = float.NaN,
                float max = float.NaN) : System.Attribute;

            public sealed partial class Rules
            {
                [MatchRule]
                public int Known { get; set; }

                public int Missing { get; set; }
            }
            """;

        GeneratorDriverRunResult result = Run(SOURCE);

        Diagnostic diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "MZ3007");
        Assert.Contains("Rules.Missing", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void SectionDeclarationDrivesClampAndImmutableProjection()
    {
        const string SOURCE = """
            namespace Mortz.Core.Match.Configuration;

            [System.AttributeUsage(System.AttributeTargets.Property)]
            public sealed class ConfigSectionAttribute : System.Attribute;

            [System.AttributeUsage(System.AttributeTargets.Property)]
            public sealed class MatchRuleAttribute(
                float min = float.NaN,
                float max = float.NaN) : System.Attribute;

            public sealed partial class Extra
            {
                [MatchRule]
                public int Count { get; set; }
            }

            public sealed partial class MatchConfig
            {
                [ConfigSection]
                public Extra Extra { get; init; } = new();
            }
            """;

        GeneratorDriverRunResult result = Run(SOURCE);
        string generated = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, item => item.Severity == DiagnosticSeverity.Error);
        Assert.Contains("Extra.Clamp();", generated, StringComparison.Ordinal);
        Assert.Contains("Extra.ToSnapshot()", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("BinaryWriter", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyVariantGetsSnapshotClampAndNoSettingsMetadata()
    {
        GeneratorDriverRunResult result = Run("""
            namespace Mortz.Core.Match.Configuration;
            [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
            public class ConfigVariantAttribute(string id, string name, System.Type type) : System.Attribute;
            [ConfigVariant("none", "None", typeof(NoRules))]
            public abstract class Rules;
            public partial class NoRules : Rules;
            """);
        string generated = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, item => item.Severity == DiagnosticSeverity.Error);
        Assert.Contains("NoRulesSnapshot(", generated);
        Assert.Contains(") : global::Mortz.Core.Match.Configuration.RulesSnapshot", generated);
        Assert.Contains("public void Clamp()", generated);
        Assert.DoesNotContain("NoRulesUiMetadata", generated);
    }

    [Theory]
    [InlineData("[ConfigVariant(\"a\", \"Again\", typeof(Second))]", "")]
    [InlineData("[ConfigVariant(\"b\", \"Again\", typeof(First))]", "")]
    [InlineData("", "public int Untracked { get; set; }")]
    public void InvalidVariantDeclarationsProduceDiagnostics(string extra, string body)
    {
        GeneratorDriverRunResult result = Run($$$"""
            namespace Mortz.Core.Match.Configuration;
            [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
            public class ConfigVariantAttribute(string id, string name, System.Type type) : System.Attribute;
            [ConfigVariant("a", "First", typeof(First))]
            {{{extra}}}
            public abstract class Rules;
            public partial class First : Rules { {{{body}}} }
            public partial class Second : Rules;
            """);

        Assert.Contains(result.Diagnostics, item => item.Id is "MZ3010" or "MZ3007");
    }

    [Fact]
    public void VariantTypedPropertiesAreValuesByConvention()
    {
        GeneratorDriverRunResult result = Run("""
            namespace Mortz.Core.Match.Configuration;
            [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
            public class ConfigVariantAttribute(string id, string name, System.Type type) : System.Attribute;
            [ConfigVariant("plain", "Plain", typeof(PlainValue))]
            public abstract class Value;
            public partial class PlainValue : Value;
            public partial class Rules
            {
                public Value Objective { get; set; } = new PlainValue();
                public Value Respawn { get; set; } = new PlainValue();
            }
            """);
        string generated = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, item => item.Severity == DiagnosticSeverity.Error);
        Assert.Contains("global::Mortz.Core.Match.Configuration.ValueSnapshot Objective,", generated);
        Assert.Contains("Objective.ToSnapshot()", generated);
        Assert.Contains("Respawn.ToMutable()", generated);
        Assert.Contains("global::Mortz.Core.Match.Configuration.ValueMetadata.Clamp(Objective);", generated);
        Assert.Contains("global::Mortz.Core.Match.Configuration.ValueMetadata.Clamp(Respawn);", generated);
    }

    private static GeneratorDriverRunResult Run(string source)
    {
        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            typeof(object).Assembly.Location,
        };
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string platform)
        {
            foreach (string path in platform.Split(Path.PathSeparator))
            {
                if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    references.Add(path);
            }
        }

        CSharpCompilation compilation = CSharpCompilation.Create(
            $"ConfigGen_{Guid.NewGuid():N}",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            references.Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new ConfigGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }
}
