namespace Mortz.Core.Match.Scoring;

public readonly record struct ContenderScore(Victor Contender, int Value);

/// <summary>Every contender's score under one metric. Lower values win when
/// HigherIsBetter is false.</summary>
public sealed record Scores(IReadOnlyList<ContenderScore> Values, bool HigherIsBetter)
{
    /// <summary>Null on an empty board or a tie for first.</summary>
    public Victor? Best()
    {
        ContenderScore? best = null;
        bool tied = false;
        foreach (ContenderScore score in Values)
        {
            if (best == null || (HigherIsBetter ? score.Value > best.Value.Value : score.Value < best.Value.Value))
            {
                best = score;
                tied = false;
            }
            else if (score.Value == best.Value.Value)
            {
                tied = true;
            }
        }
        return tied ? null : best?.Contender;
    }
}
