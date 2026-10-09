using HumeMusic.Core.Covers;

namespace HumeMusic.Core.Tests;

public class CoverFitTests
{
    [Fact]
    public void Text_that_fits_keeps_its_usual_size()
    {
        Assert.Equal(92, CoverFit.Size(92, size => size <= 120));
    }

    [Fact]
    public void Text_too_long_is_made_just_small_enough()
    {
        // It fits at 60 and below: the answer is 60, to within a hair, never above.
        var size = CoverFit.Size(92, s => s <= 60);
        Assert.InRange(size, 59.9, 60);
    }

    [Fact]
    public void Text_that_fits_at_no_size_is_set_at_the_floor()
    {
        Assert.Equal(92 * CoverFit.Floor, CoverFit.Size(92, _ => false), 6);
    }

    [Fact]
    public void A_longer_text_never_ends_up_larger_than_a_shorter_one()
    {
        // The box holds 760 units; each character takes half the size.
        bool Fits(int characters, double size) => characters * size * 0.5 <= 760;
        var short1 = CoverFit.Size(92, s => Fits(14, s));
        var longer = CoverFit.Size(92, s => Fits(30, s));
        var longest = CoverFit.Size(92, s => Fits(60, s));
        Assert.Equal(92, short1);
        Assert.True(longer < short1 && longest < longer);
    }
}
