namespace Mortz.Core.Match.Configuration;

public enum ScoreMetric { KILLS, DEATHS }

public static class ScoreMetricExtensions
{
    public static bool HigherIsBetter(this ScoreMetric metric) => metric == ScoreMetric.KILLS;
}
