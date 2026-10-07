using Godot;
using Mortz.Extensions;
using Xunit;

namespace Mortz.Tests.Extensions;

[Collection(nameof(MortzGodotCollection))]
public class NodeExtensionsTests
{
    [Fact]
    public void PredicateSelectsTheSemanticMatch()
    {
        Node root = new();
        Node branch = new();
        root.AddChild(new Button { Text = "SKIP" });
        root.AddChild(branch);
        Button join = new() { Text = "JOIN" };
        branch.AddChild(join);

        try
        {
            Button match = root.GetDescendantByType<Button>(
                button => button.Text == "JOIN");
            Assert.Same(join, match);
        }
        finally
        {
            root.Free();
        }
    }

    [Fact]
    public void OrNullLookupsRejectAFreedReceiver()
    {
        Node root = new();
        root.Free();

        Assert.Null(root.GetChildByTypeOrNull<Node>());
        Assert.Null(root.GetDescendantByTypeOrNull<Node>());
        Assert.Empty(root.GetDescendantsByType<Node>());
    }

    // Exit order on Godot 4.7.1: _exit_tree, tree_exiting (the unsubscribe),
    // NOTIFICATION_EXIT_TREE (AutoInject's OnExitTree), tree_exited.
    [Fact]
    public void SubscriptionEndsWhenTheNodeLeavesTheTree()
    {
        Node node = new();
        List<string> trace = [];
        Action handler = () => trace.Add("raised");
        Action? source = null;
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(node);
        node.TreeExited += () => trace.Add("exited");
        node.Subscribe(() => source += handler, () =>
        {
            source -= handler;
            trace.Add("unsubscribed");
        });

        source?.Invoke();
        node.GetParent().RemoveChild(node);
        source?.Invoke();

        try
        {
            Assert.Null(source);
            Assert.Equal(["raised", "unsubscribed", "exited"], trace);
        }
        finally
        {
            node.Free();
        }
    }
}
