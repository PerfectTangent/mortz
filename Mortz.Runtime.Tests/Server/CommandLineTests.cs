using Mortz.Server.Hosting;
using Xunit;

namespace Mortz.Runtime.Tests.Server;

public class CommandLineTests
{
    [Fact]
    public void ValueFollowsItsFlag()
    {
        CommandLine args = new(["--net-stats", "--content-root", "D:/portable/content"]);

        Assert.Equal("D:/portable/content", args.GetValue("--content-root"));
        Assert.True(args.HasFlag("--net-stats"));
    }

    [Fact]
    public void TrailingFlagHasNoValue() =>
        Assert.Null(new CommandLine(["--net-stats", "--content-root"]).GetValue("--content-root"));

    [Fact]
    public void UnparsableIntFallsBack() =>
        Assert.Equal(7777, new CommandLine(["--port", "abc"]).GetInt("--port", 7777));
}
