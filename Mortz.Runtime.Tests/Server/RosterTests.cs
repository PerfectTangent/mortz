using Mortz.Server.Players;
using Xunit;

namespace Mortz.Runtime.Tests.Server;

public class RosterTests
{
    [Fact]
    public void ASelectedSkinBecomesPartOfThePlayerIdentity()
    {
        Roster roster = new();

        Player player = roster.Join(1, "hello", requestedSkin: 24);

        Assert.Equal(24, player.Skin);
        Assert.Throws<ArgumentOutOfRangeException>(() => roster.Join(2, "bad", -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => roster.Join(3, "bad", 25));
    }

    [Fact]
    public void ABlankNameFallsBackToThePeerId()
    {
        Roster roster = new();

        Assert.Equal("Player 7", roster.Join(7, "   ").Name);
        Assert.Equal("Player 8", roster.Join(8, "").Name);
    }

    [Fact]
    public void ANameOfNothingButControlRunesFallsBackToo()
    {
        Roster roster = new();

        Assert.Equal("Player 3", roster.Join(3, "\r\n‮").Name);
    }

    [Fact]
    public void ANameIsSanitizedOnce()
    {
        Roster roster = new();

        Assert.Equal("hello", roster.Join(1, "  hello‮  ").Name);
    }

    [Fact]
    public void IterationIsAscendingPeerIdRegardlessOfJoinOrder()
    {
        Roster roster = new();
        roster.Join(9, "nine");
        roster.Join(2, "two");
        roster.Join(5, "five");

        Assert.Equal([2, 5, 9], roster.Select(player => player.PeerId).ToArray());
    }

    [Fact]
    public void FindReturnsTheSamePlayerAndNullForAStranger()
    {
        Roster roster = new();
        Player joined = roster.Join(4, "four");

        Assert.Same(joined, roster.Find(4));
        Assert.Null(roster.Find(5));
    }

    [Fact]
    public void LeavingRemovesThePlayerAndReturnsThemOnce()
    {
        Roster roster = new();
        Player joined = roster.Join(4, "four");

        Assert.Same(joined, roster.Leave(4));
        Assert.Null(roster.Leave(4));
        Assert.Null(roster.Find(4));
        Assert.Empty(roster);
    }

    [Fact]
    public void RejoiningMintsAFreshPlayer()
    {
        Roster roster = new();
        Player before = roster.Join(4, "four");

        roster.Leave(4);
        Player after = roster.Join(4, "four");

        Assert.NotSame(before, after);
    }

    [Fact]
    public void JoiningTwiceOnOnePeerReplacesTheEntry()
    {
        Roster roster = new();
        roster.Join(4, "four");
        Player second = roster.Join(4, "again");

        Assert.Same(second, Assert.Single(roster));
    }
}
