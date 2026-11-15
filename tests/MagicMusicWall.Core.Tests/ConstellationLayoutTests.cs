using MagicMusicWall.Core;
using Xunit;

namespace MagicMusicWall.Core.Tests;

public sealed class ConstellationLayoutTests
{
    [Fact]
    public void StrongerRelationshipSettlesNoticeablyCloserInSparseScene()
    {
        ConstellationLayout Scene(double strength) => new([new("focus", 180, 58, true), new("neighbor", 150, 46, false)],
            [new("focus", "neighbor", strength)], 1040, 680);
        var strong = Scene(.9); var weak = Scene(.1);
        Settle(strong); Settle(weak);
        Assert.True(Distance(strong.Nodes[0], strong.Nodes[1]) + 100 < Distance(weak.Nodes[0], weak.Nodes[1]));
    }

    [Theory]
    [InlineData(1040, 680)]
    [InlineData(760, 520)]
    public void NineteenLabelsSettleInsideCanvasWithoutOverlapping(double width, double height)
    {
        var nodes = Enumerable.Range(0, 19).Select(i => new ConstellationNodeSpec("n" + i,
            i == 0 ? 180 : 150, i == 0 ? 58 : 46, i == 0)).ToArray();
        var links = Enumerable.Range(1, 18).Select(i => new ConstellationLink("n0", "n" + i, .1 + .85 * i / 18)).ToList();
        for (var i = 1; i < 18; i++) links.Add(new("n" + i, "n" + (i + 1), .65));
        // A warm start with every label at the same position exercises zero-distance handling.
        var previous = nodes.ToDictionary(x => x.Id, _ => new ConstellationPoint(width / 2, height / 2));
        var layout = new ConstellationLayout(nodes, links, width, height, previous);
        Settle(layout);
        AssertBounds(layout, width, height);
        for (var i = 0; i < layout.Nodes.Count; i++)
            for (var j = i + 1; j < layout.Nodes.Count; j++)
            {
                var a = layout.Nodes[i]; var b = layout.Nodes[j];
                Assert.True(Math.Abs(a.X - b.X) >= (a.Width + b.Width) / 2 - .01
                    || Math.Abs(a.Y - b.Y) >= (a.Height + b.Height) / 2 - .01,
                    $"Labels {a.Id} and {b.Id} overlap at {a.X:F1},{a.Y:F1} / {b.X:F1},{b.Y:F1}.");
            }
    }

    [Fact]
    public void DragHoldsNodeReleaseReturnsFocusAndReheatWakesSettledLayout()
    {
        var layout = new ConstellationLayout([new("focus", 180, 58, true), new("other", 150, 46, false)],
            [new("focus", "other", .8)], 1040, 680);
        Settle(layout);
        layout.Drag("other", 100, 100);
        Assert.False(layout.IsSettled);
        layout.Step(.05);
        var other = layout.Nodes.Single(x => x.Id == "other");
        Assert.Equal(100, other.X); Assert.Equal(100, other.Y);
        layout.Release("other"); layout.Step(.05);
        Assert.True(other.X != 100 || other.Y != 100);
        layout.Drag("focus", 220, 160); layout.Step(.05);
        var focus = layout.Nodes.Single(x => x.IsFocus);
        Assert.Equal(220, focus.X); Assert.Equal(160, focus.Y);
        layout.Release("focus");
        Assert.Equal(520, focus.X); Assert.Equal(340, focus.Y);
        Settle(layout);
        layout.Reheat(); Assert.False(layout.IsSettled);
        Settle(layout);
    }

    [Fact]
    public void SeedingIsDeterministicIrregularAndWarmStartsPreserveCoordinates()
    {
        var nodes = Enumerable.Range(0, 6).Select(i => new ConstellationNodeSpec(i.ToString(), 150, 46, i == 0)).ToArray();
        var a = new ConstellationLayout(nodes, [], 1040, 680);
        var b = new ConstellationLayout(nodes.Reverse().ToArray(), [], 1040, 680);
        Assert.Equal(a.Nodes.Select(n => (n.Id, n.X, n.Y)), b.Nodes.Select(n => (n.Id, n.X, n.Y)));
        var focus = a.Nodes.Single(n => n.IsFocus);
        Assert.True(a.Nodes.Where(n => !n.IsFocus).Select(n => Math.Round(Distance(focus, n))).Distinct().Count() > 2);
        a.Drag("2", 200, 210);
        var warm = new ConstellationLayout(nodes, [], 1040, 680, a.Nodes.ToDictionary(n => n.Id, n => new ConstellationPoint(n.X, n.Y)));
        Assert.Equal(200, warm.Nodes.Single(n => n.Id == "2").X);
        Assert.Equal(210, warm.Nodes.Single(n => n.Id == "2").Y);
    }

    [Fact]
    public void LongFrameIsClampedInvalidTimeIgnoredAndTinyCanvasRemainsFinite()
    {
        var nodes = new[] { new ConstellationNodeSpec("f", 180, 58, true), new ConstellationNodeSpec("n", 150, 46, false) };
        var links = new[] { new ConstellationLink("f", "n", .5) };
        var a = new ConstellationLayout(nodes, links, 1040, 680);
        var b = new ConstellationLayout(nodes, links, 1040, 680);
        a.Step(double.NaN); a.Step(double.PositiveInfinity); a.Step(-1); a.Step(0);
        Assert.Equal(a.Nodes.Select(n => (n.X, n.Y)), b.Nodes.Select(n => (n.X, n.Y)));
        a.Step(1000); b.Step(.05);
        Assert.Equal(a.Nodes.Select(n => (n.X, n.Y)), b.Nodes.Select(n => (n.X, n.Y)));
        var tiny = new ConstellationLayout(nodes, links, 10, 10);
        Settle(tiny);
        Assert.All(tiny.Nodes, n => { Assert.Equal(5, n.X); Assert.Equal(5, n.Y); });
        Assert.True(new ConstellationLayout([], [], 10, 10).IsSettled);
    }

    private static void Settle(ConstellationLayout layout)
    {
        for (var i = 0; i < 1200 && !layout.IsSettled; i++) layout.Step(1.0 / 60);
        Assert.True(layout.IsSettled);
    }

    private static double Distance(ConstellationNode a, ConstellationNode b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
    private static void AssertBounds(ConstellationLayout layout, double width, double height) => Assert.All(layout.Nodes, n =>
    {
        Assert.True(double.IsFinite(n.X) && double.IsFinite(n.Y));
        Assert.InRange(n.X, n.Width / 2, width - n.Width / 2); Assert.InRange(n.Y, n.Height / 2, height - n.Height / 2);
    });
}
