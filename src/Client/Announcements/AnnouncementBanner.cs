using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Godot.Collections;
using Mortz.Client.Ui;
using Mortz.Core.Features;
using Mortz.Core.Match.Events;
using Mortz.Core.Match.Teams;
using Mortz.Core.Text;
using Mortz.Extensions;

namespace Mortz.Client.Announcements;

/// <summary>On-screen announcement text. Lines stack; the match-point warning
/// stays up while it holds.</summary>
[Meta(typeof(IAutoNode))]
public partial class AnnouncementBanner : Control
{
    [Export] private float _lineSeconds = 2.6f;
    [Export] private float _lineFadeSeconds = 0.5f;
    [Export] private int _maxLines = 2;

    // Newest line sits on top; pushed-down lines dim.
    [Export] private float[] _tierBrightness = [1f, 0.82f];

    [Export] private RichTextLabel _matchPoint = null!;
    [Export] private VBoxContainer _lines = null!;
    [Export] private PackedScene _lineScene = null!;
    [Export] private float _lineWidth = 400f;

    private float LineWidth => Math.Min(_lineWidth, Math.Max(1f, Size.X - 32f));

    [Dependency] private FeatureScope Scope => this.DependOn<FeatureScope>();
    private IAnnouncementDirector Director => Scope.Feature<IAnnouncementDirector>();

    public override void _Notification(int what) => this.Notify(what);

    public void OnReady() => Resized += FitLines;

    public void OnResolved()
    {
        this.Subscribe(() => Director.BatchReady += OnBatch,
            () => Director.BatchReady -= OnBatch);
        this.Subscribe(() => Director.MatchPointChanged += OnMatchPoint,
            () => Director.MatchPointChanged -= OnMatchPoint);
        // A late joiner walks in mid match point.
        if (Director.MatchPoint is MatchPointState active)
            OnMatchPoint(active);
    }

    private void OnBatch(IReadOnlyList<Announcement> batch)
    {
        // Reversed so the highest-priority line lands on top.
        for (int i = batch.Count - 1; i >= 0; i--)
        {
            if (Compose(batch[i]) is Headline line)
                AddLine(line);
        }
    }

    private void OnMatchPoint(MatchPointState? state)
    {
        _matchPoint.Visible = state != null;
        if (state is not MatchPointState held)
            return;
        _matchPoint.Text = MatchPointLine(held).Wrap(new Style().Center());
    }

    private static RichText MatchPointLine(MatchPointState mp)
    {
        string kills = mp.Remaining <= 1 ? "one more kill" : $"{mp.Remaining} more kills";
        if (mp.Leader is not MatchPointLeader leader)
            return new RichText($"{kills} wins!");
        return new RichText()
            .Add(leader.Name, NameStyle(leader.Team))
            .Add($" needs {kills}!");
    }

    /// <summary>Null for the kinds the banner stays quiet on.</summary>
    private static Headline? Compose(Announcement a) => a.Kind switch
    {
        GameEventKind.FIRST_BLOOD => new Headline("FIRST BLOOD", Player(a.Actor), "AnnouncementFirstBlood"),
        GameEventKind.HOLY_SHIT => new Headline("HOLY SHIT!",
            BuildDetail(a, Vocab.MultiKillTier(a.Magnitude).Loud), "AnnouncementHolyShit"),
        GameEventKind.MULTI_KILL => BuildMultiKill(a),
        GameEventKind.KILL_STREAK => BuildKillStreak(a),
        GameEventKind.SHUTDOWN => new Headline("SHUT DOWN",
            Player(a.Actor).Add(" ended ").Add(Player(a.Victim)).Add("'s streak"), "AnnouncementShutdown"),
        GameEventKind.HUMILIATION => new Headline("OWNED",
            new RichText("by ").Add(Player(a.Actor)), "AnnouncementOwned", Player(a.Victim)),
        GameEventKind.TEAM_WIPE => new Headline("RAMPAGE", Player(a.Actor), "AnnouncementRampage"),
        _ => null
    };

    private static Headline BuildMultiKill(Announcement a)
    {
        MultiKillWording tier = Vocab.MultiKillTier(a.Magnitude);
        return new Headline(tier.Loud, Player(a.Actor),
            _multiKillThemes[tier.Heat]);
    }

    private static Headline BuildKillStreak(Announcement a)
    {
        StreakWording tier = Vocab.StreakTier(a.Magnitude);
        return new Headline(tier.StreakName, BuildDetail(a, $"{a.Magnitude} KILLS"),
            _streakThemes[tier.Heat]);
    }

    private static readonly StringName[] _multiKillThemes =
    [
        "AnnouncementDoubleKill", "AnnouncementTripleKill", "AnnouncementOverkill",
        "AnnouncementUltraKill", "AnnouncementMassacre", "AnnouncementCarnage"
    ];

    private static readonly StringName[] _streakThemes =
    [
        "AnnouncementBloodlust", "AnnouncementPunishment", "AnnouncementDominating",
        "AnnouncementMachineGod", "AnnouncementPsycho"
    ];

    private record Headline(string Title, RichText Detail, StringName ThemeVariation, RichText? Victim = null);

    private static RichText BuildDetail(Announcement a, string caption) =>
        Player(a.Actor).Add("  -  ").Add(caption);

    private static RichText Player(Combatant p) => new RichText().Add(p.Name, NameStyle(p));

    private static Style NameStyle(Combatant p) => NameStyle(p.Team);

    private static Style NameStyle(Team? team)
    {
        Style style = new Style();
        if (team is Team assigned)
            style.Color(TeamColors.For(assigned));
        return style;
    }

    private void AddLine(Headline text)
    {
        AnnouncementLine line = _lineScene.Instantiate<AnnouncementLine>();
        line.CustomMinimumSize = new Vector2(LineWidth, 0);
        _lines.AddChild(line);
        _lines.MoveChild(line, 0);

        line.Present(text.Title, text.Detail, text.ThemeVariation, text.Victim);

        Tween tween = line.CreateTween();
        tween.TweenInterval(_lineSeconds);
        tween.TweenProperty(line, "modulate:a", 0f, _lineFadeSeconds);
        tween.TweenCallback(Callable.From(line.QueueFree));

        TrimOverflow();
        Restyle();
    }

    private void FitLines()
    {
        foreach (Node child in _lines.GetChildren())
        {
            if (child is Control line && !line.IsQueuedForDeletion())
                line.CustomMinimumSize = new Vector2(LineWidth, 0);
        }
    }

    private void TrimOverflow()
    {
        Array<Node> children = _lines.GetChildren();
        if (children.Count(c => !c.IsQueuedForDeletion()) <= _maxLines)
            return;
        for (int i = children.Count - 1; i >= 0; i--)
        {
            if (children[i].IsQueuedForDeletion())
                continue;
            children[i].QueueFree();
            break;
        }
    }

    private void Restyle()
    {
        int index = 0;
        foreach (Node child in _lines.GetChildren())
        {
            if (child.IsQueuedForDeletion())
                continue;
            var line = (Control)child;
            int tier = Math.Min(index, _tierBrightness.Length - 1);
            float brightness = _tierBrightness[tier];
            line.Modulate = new Color(brightness, brightness, brightness, line.Modulate.A);
            index++;
        }
    }
}
