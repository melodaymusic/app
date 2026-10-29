using MelodayMusic.Core.Artwork;
using MelodayMusic.Core.Covers;

namespace MelodayMusic.Core.Tests;

public class ShapeLayoutTests
{
    private static readonly ShapeMask Circle = ShapeMask.From(48, (u, v) => (u - 0.5) * (u - 0.5) + (v - 0.5) * (v - 0.5) <= 0.25);
    private static readonly ShapeMask Square = ShapeMask.From(48, (_, _) => true);
    private static readonly ShapeMask Ell = ShapeMask.From(48, (u, v) => u < 0.35 || v > 0.65);
    private static readonly ShapeMask[] Shapes = [Circle, Square, Ell];
    private static readonly uint Ground = MelodayPalette.Families.First(f => f.Name == "Vivid Purple").Darker;

    [Fact]
    public void Two_different_shapes_on_different_edges_barely_overlapping()
    {
        for (var seed = 0; seed < 200; seed++)
        {
            var layout = ShapeLayout.Arrange(Shapes, seed, Ground);
            Assert.NotEqual(layout.Back.Shape, layout.Front.Shape);
            var (back, front) = (Shapes[layout.Back.Shape], Shapes[layout.Front.Shape]);
            Assert.True(ShapeLayout.OffCover(back, layout.Back) <= ShapeLayout.MaxOffCover, $"seed {seed}: back off the cover");
            Assert.True(ShapeLayout.OffCover(front, layout.Front) <= ShapeLayout.MaxOffCover, $"seed {seed}: front off the cover");
            Assert.True(ShapeLayout.Covered(back, layout.Back, front, layout.Front) <= ShapeLayout.MaxOverlap, $"seed {seed}: back hidden");
            Assert.True(ShapeLayout.Covered(front, layout.Front, back, layout.Back) <= ShapeLayout.MaxOverlap, $"seed {seed}: front hidden");
            Assert.Equal(ShapeEdges.None, ShapeLayout.Edges(back, layout.Back) & ShapeLayout.Edges(front, layout.Front));
        }
    }

    [Fact]
    public void The_shapes_take_the_two_shades_the_cover_isnt_and_the_higher_one_the_bars_shade()
    {
        var family = MelodayPalette.Families.First(f => f.Name == "Vivid Purple");
        for (var seed = 0; seed < 50; seed++)
        {
            var layout = ShapeLayout.Arrange(Shapes, seed, family.Darker);
            Assert.Equal(CoverEdge.Colour(family.Darker), layout.Bars);
            Assert.Equal(new[] { family.Lighter, family.Original }.Order(), new[] { layout.BackColour, layout.FrontColour }.Order());
            var backY = ShapeLayout.Centre(Shapes[layout.Back.Shape], layout.Back).Y;
            var frontY = ShapeLayout.Centre(Shapes[layout.Front.Shape], layout.Front).Y;
            Assert.Equal(layout.Bars, backY <= frontY ? layout.BackColour : layout.FrontColour);
        }
    }

    [Fact]
    public void The_same_mix_always_gets_the_same_cover() =>
        Assert.Equal(ShapeLayout.Arrange(Shapes, 42, Ground), ShapeLayout.Arrange(Shapes, 42, Ground));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void A_turned_shape_maps_back_to_itself(int turns)
    {
        var at = new ShapePlacement(0, 0.2, 0.1, 0.5, turns);
        var (x, y) = at.ToCover(0.1, 0.8);
        var (u, v) = at.FromCover(x, y);
        Assert.Equal(0.1, u, 9);
        Assert.Equal(0.8, v, 9);
        if (turns == 1) Assert.Equal(at.Y, at.ToCover(0, 0.5).Y, 9);
    }

    [Fact]
    public void Only_shapes_inside_their_circle_count_as_round()
    {
        Assert.True(ShapeLayout.IsRound(Circle));
        Assert.False(ShapeLayout.IsRound(Square));
        Assert.False(ShapeLayout.IsRound(Ell));
        Assert.True(ShapeLayout.IsRound(ShapeMask.From(48, (u, v) => Math.Abs(u - 0.5) + Math.Abs(v - 0.5) <= 0.5)));
    }

    [Fact]
    public void The_shapes_keep_clear_of_the_name()
    {
        var covered = Enumerable.Range(0, 60).Select(seed =>
        {
            var layout = ShapeLayout.Arrange(Shapes, seed, Ground);
            return ShapeLayout.LabelCovered([(Shapes[layout.Back.Shape], layout.Back), (Shapes[layout.Front.Shape], layout.Front)]);
        }).ToList();
        Assert.True(covered.Average() <= 0.12, $"on average {covered.Average():P0} of the name is behind shapes");
    }
}
