namespace Mortz.Core.Match.Configuration;

public static class ModeComposition
{
    public static IReadOnlyList<string> Errors(ModeRules rules)
    {
        List<string> errors = [];
        CheckVariant(rules.EndCondition, EndConditionRulesMetadata.Variants, "rules.end_condition", errors);
        CheckVariant(rules.Objective, ObjectiveRulesMetadata.Variants, "rules.objective", errors);
        CheckVariant(rules.Respawn, RespawnRulesMetadata.Variants, "rules.respawn", errors);
        CheckEnum(rules.Score, "rules.score", errors);
        CheckEnum(rules.Winner, "rules.winner", errors);
        CheckEnum(rules.Evaluation, "rules.evaluation", errors);
        CheckEnum(rules.Replay, "rules.replay", errors);
        CheckEnum(rules.SuicidePenalty, "rules.suicide_penalty", errors);

        if (rules.EndCondition is TimeLimitRules && rules.Evaluation != EvaluationTiming.TICK)
        {
            errors.Add("rules.end_condition 'time_limit' requires rules.evaluation = 'tick'.");
        }
        if (rules.EndCondition is ObjectiveCompletionRules)
        {
            if (rules.Objective?.CanComplete != true)
            {
                errors.Add("rules.end_condition 'objective_completion' requires an objective capable of completing.");
            }
            if (rules.Evaluation != EvaluationTiming.TICK)
            {
                errors.Add("rules.end_condition 'objective_completion' requires rules.evaluation = 'tick'.");
            }
        }
        if (rules.Winner == WinnerRule.QUALIFYING && rules.EndCondition?.SuppliesQualifier != true)
        {
            errors.Add("rules.winner 'qualifying' requires an end condition that supplies a qualifier.");
        }
        return errors;
    }

    public static void Validate(ModeRules rules)
    {
        IReadOnlyList<string> errors = Errors(rules);
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors), nameof(rules));
        }
    }

    private static void CheckVariant<T>(T? rules, IReadOnlyList<ConfigVariantDescriptor<T>> variants,
        string path, List<string> errors) where T : class
    {
        if (rules == null || variants.All(variant => variant.RulesType != rules.GetType()))
        {
            errors.Add($"{path} has unknown variant '{rules?.GetType().Name ?? "null"}'. " +
                       $"Expected: {string.Join(", ", variants.Select(variant => variant.Id))}.");
        }
    }

    private static void CheckEnum<T>(T value, string path, List<string> errors) where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            errors.Add($"{path} has unknown value '{value}'.");
        }
    }
}
