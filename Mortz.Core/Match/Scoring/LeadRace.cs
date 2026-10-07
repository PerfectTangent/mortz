namespace Mortz.Core.Match.Scoring;

/// <summary>Tracks the best and second-best score offered, so the standing is
/// the lead the front-runner holds over whoever chases them.</summary>
public sealed class LeadRace(int target)
{
    private int _best = int.MinValue;
    private int _second = int.MinValue;
    private int _count;
    private Victor? _leader;
    private bool _tied;

    public void Offer(Victor contender, int score)
    {
        _count++;
        if (score > _best)
        {
            _second = _best;
            _best = score;
            _leader = contender;
            _tied = false;
        }
        else if (score == _best)
        {
            _second = _best;
            _tied = true;
        }
        else if (score > _second)
        {
            _second = score;
        }
    }

    private bool Open => _count < 2 || _tied;

    /// <summary>Null while nobody leads alone.</summary>
    public Victor? Leader => Open ? null : _leader;

    public int Remaining => Open ? target : (int)Math.Max(0, target - ((long)_best - _second));
}
