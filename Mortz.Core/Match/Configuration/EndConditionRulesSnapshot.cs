using Mortz.Core.Match.Scoring;

namespace Mortz.Core.Match.Configuration;

public abstract record EndConditionRulesSnapshot
{
    public abstract EndConditionRules ToMutable();

    /// <summary>Null keeps the match running, including on a tie.</summary>
    public abstract Victor? Winner(int tick, Scores scores);

    /// <summary>Someone is one kill from winning.</summary>
    public virtual MatchPoint? MatchPoint(Scores scores) => null;
}
