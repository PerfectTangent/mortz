using Mortz.Core.Features;
using Xunit;

namespace Mortz.Runtime.Tests.Core;

public class FeatureScopeTests
{
    private interface IPing
    {
        void Ping();
    }

    private sealed class Feature(string name, List<string> events, Action<Feature>? onPing = null)
        : IPing, IDisposable
    {
        public void Ping()
        {
            events.Add(name);
            onPing?.Invoke(this);
        }

        public void Dispose() => events.Add("dispose " + name);
    }

    private sealed class State : IDisposable
    {
        public static List<string>? Events;
        public string Name = "";
        public int Value;

        public void Dispose() => Events?.Add("dispose " + Name);
    }

    private sealed class OtherState : IDisposable
    {
        public string Name = "";

        public void Dispose() => State.Events?.Add("dispose other " + Name);
    }

    private sealed class Peer;

    [Fact]
    public void RaiseRunsRootBeforeChildAndReverseRunsChildBeforeRoot()
    {
        List<string> events = [];
        FeatureScope root = new("root");
        root.Register(new Feature("a", events));
        root.Register(new Feature("b", events));
        root.Start();
        FeatureScope child = root.OpenChild("child");
        child.Register(new Feature("c", events));
        child.Register(new Feature("d", events));
        child.Start();

        child.Raise<IPing>(feature => feature.Ping());
        Assert.Equal(["a", "b", "c", "d"], events);

        events.Clear();
        root.RaiseReverse<IPing>(feature => feature.Ping());
        Assert.Equal(["d", "c", "b", "a"], events);
    }

    [Fact]
    public void AChildOpenedMidRaiseIsNotReachedAndTheClosedOneIsSkipped()
    {
        List<string> events = [];
        FeatureScope root = new("root");
        root.Register(new Feature("swap", events, _ =>
        {
            FeatureScope next = root.OpenChild("next");
            next.Register(new Feature("new", events));
            next.Start();
        }));
        root.Register(new Feature("after", events));
        root.Start();
        FeatureScope old = root.OpenChild("old");
        old.Register(new Feature("old", events));
        old.Start();

        root.Raise<IPing>(feature => feature.Ping());

        Assert.Equal(["swap", "dispose old", "after"], events);
        Assert.True(old.IsClosed);
        Assert.Equal("next", root.Child?.Name);
    }

    [Fact]
    public void AnUnstartedChildIsNotRaised()
    {
        List<string> events = [];
        FeatureScope root = new("root");
        root.Start();
        root.OpenChild("child").Register(new Feature("child", events));

        root.Raise<IPing>(feature => feature.Ping());

        Assert.Empty(events);
    }

    [Fact]
    public void ChildHandlersAreAttachedAndDetachedWithTheChild()
    {
        List<string> routed = [];
        FeatureScope root = new("root", feature => routed.Add("+" + feature),
            feature => routed.Add("-" + feature));
        root.Register("server");
        root.Start();

        FeatureScope lobby = root.OpenChild("lobby");
        lobby.Register("lobby");
        lobby.Start();
        FeatureScope match = root.OpenChild("match");
        match.Register("match");
        match.Start();
        root.CloseChild();

        Assert.Equal(["+server", "+lobby", "-lobby", "+match", "-match"], routed);
        Assert.Null(root.Child);
    }

    [Fact]
    public void ForgetDropsThePlayerFromEveryTableDeepestFirstAndFreshStateFollows()
    {
        List<string> events = [];
        State.Events = events;
        FeatureScope root = new("root");
        PlayerTable<Peer, State> first = root.Table<Peer, State>();
        PlayerTable<Peer, OtherState> second = root.Table<Peer, OtherState>();
        root.Start();
        FeatureScope match = root.OpenChild("match");
        PlayerTable<Peer, State> matchTable = match.Table<Peer, State>();
        match.Start();
        Peer peer = new();
        Peer other = new();
        first[peer].Name = "first";
        first[peer].Value = 3;
        second[peer].Name = "second";
        matchTable[peer].Name = "match";
        first[other].Name = "kept";

        root.Forget(peer);

        Assert.Equal(["dispose match", "dispose other second", "dispose first"], events);
        Assert.False(first.TryGet(peer, out _));
        Assert.Equal(0, first[peer].Value);
        Assert.True(first.TryGet(other, out State? kept));
        Assert.Equal("kept", kept.Name);
        State.Events = null;
    }

    [Fact]
    public void DisposeClosesTheChildThenTablesInReverseClaimOrderThenFeatures()
    {
        List<string> events = [];
        State.Events = events;
        FeatureScope root = new("root", _ => events.Add("route"), _ => events.Add("unroute"));
        root.Register(new Feature("owner", events));
        PlayerTable<Peer, State> first = root.Table<Peer, State>();
        PlayerTable<Peer, OtherState> second = root.Table<Peer, OtherState>();
        root.Own(() => events.Add("cleanup"));
        root.Start();
        FeatureScope child = root.OpenChild("child");
        child.Register(new Feature("child", events));
        child.Start();
        first[new Peer()].Name = "first";
        second[new Peer()].Name = "second";
        events.Clear();

        root.Dispose();
        root.Dispose();

        Assert.Equal(
        [
            "unroute", "dispose child", "unroute", "cleanup", "dispose other second", "dispose first",
            "dispose owner",
        ], events);
        Assert.Throws<ObjectDisposedException>(() => first[new Peer()]);
        State.Events = null;
    }

    [Fact]
    public void ADisposedScopeRejectsRegistrationAndTables()
    {
        FeatureScope scope = new("root");
        scope.Dispose();

        Assert.Throws<ObjectDisposedException>(() => scope.Register(new object()));
        Assert.Throws<ObjectDisposedException>(() => scope.Table<Peer, State>());
        Assert.Throws<ObjectDisposedException>(() => scope.OpenChild("child"));
    }

    [Fact]
    public void RegistrationClosesAtStart()
    {
        FeatureScope scope = new("root");
        scope.Start();

        Assert.Throws<InvalidOperationException>(() => scope.Register(new object()));
    }

    [Fact]
    public void OneFailingCleanupDoesNotLeaveSensitiveFeaturesAlive()
    {
        List<string> events = [];
        FeatureScope scope = new("root");
        scope.Register(new Feature("sensitive", events));
        scope.Own(() => throw new InvalidOperationException("state failure"));
        scope.Start();

        Assert.Throws<AggregateException>(scope.Dispose);
        Assert.Equal(["dispose sensitive"], events);
        Assert.True(scope.IsClosed);
    }

    [Fact]
    public void DescribeShowsTheTreeFeaturesAndTableCounts()
    {
        FeatureScope root = new("server");
        root.Register(new object());
        PlayerTable<Peer, State> table = root.Table<Peer, State>();
        root.Start();
        FeatureScope match = root.OpenChild("match");
        match.Register("text");
        match.Start();
        _ = table[new Peer()];
        _ = table[new Peer()];

        string description = root.Describe();

        Assert.Contains("server", description);
        Assert.Contains("State[2]", description);
        Assert.Contains("  match", description);
        Assert.Contains("String", description);
    }

    [Fact]
    public void FeatureFindsByExactTypeAndByInterface()
    {
        List<string> events = [];
        FeatureScope scope = new("root");
        Feature feature = scope.Register(new Feature("a", events));

        Assert.Same(feature, scope.Feature<Feature>());
        Assert.Same(feature, scope.Feature<IPing>());
    }

    [Fact]
    public void FeatureWalksUpFromTheChildButNeverDown()
    {
        List<string> events = [];
        FeatureScope root = new("root");
        Feature rootFeature = root.Register(new Feature("a", events));
        root.Start();
        FeatureScope child = root.OpenChild("child");
        State childFeature = child.Register(new State());

        Assert.Same(rootFeature, child.Feature<Feature>());
        Assert.Same(childFeature, child.Feature<State>());
        InvalidOperationException missing = Assert.Throws<InvalidOperationException>(root.Feature<State>);
        Assert.Contains("'root'", missing.Message);
    }

    [Fact]
    public void FeaturePrefersTheNearestScope()
    {
        List<string> events = [];
        FeatureScope root = new("root");
        root.Register(new Feature("a", events));
        root.Start();
        FeatureScope child = root.OpenChild("child");
        Feature near = child.Register(new Feature("b", events));

        Assert.Same(near, child.Feature<IPing>());
    }

    [Fact]
    public void MissingFeatureNamesTheWholeChain()
    {
        FeatureScope root = new("connection");
        root.Start();
        FeatureScope child = root.OpenChild("match");

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(child.Feature<Peer>);

        Assert.Contains("Peer", error.Message);
        Assert.Contains("'match' -> 'connection'", error.Message);
    }

    [Fact]
    public void TwoMatchesInOneScopeThrow()
    {
        List<string> events = [];
        FeatureScope scope = new("root");
        scope.Register(new Feature("a", events));
        scope.Register(new Feature("b", events));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(scope.Feature<IPing>);

        Assert.Contains("root", error.Message);
        Assert.Contains("Feature, Feature", error.Message);
    }

    [Fact]
    public void FeatureWorksBeforeStartAndAfterDispose()
    {
        FeatureScope root = new("root");
        State state = root.Register(new State());
        Assert.Same(state, root.Feature<State>());
        root.Start();
        FeatureScope child = root.OpenChild("child");
        OtherState other = child.Register(new OtherState());
        child.Start();

        root.Dispose();

        Assert.Same(state, child.Feature<State>());
        Assert.Same(other, child.Feature<OtherState>());
    }

    [Fact]
    public void FeatureSeesALaterRegistration()
    {
        FeatureScope scope = new("root");
        Assert.Throws<InvalidOperationException>(scope.Feature<State>);
        State state = scope.Register(new State());

        Assert.Same(state, scope.Feature<State>());
    }
}
