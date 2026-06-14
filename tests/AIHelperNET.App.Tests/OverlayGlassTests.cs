using System.Windows.Media;
using AIHelperNET.App.Windows;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.App.Tests;

public sealed class OverlayGlassTests
{
    private static readonly Color Base = Color.FromRgb(0x21, 0x21, 0x3A); // Brush.Background.Window

    [Fact]
    public void WithAlpha_keeps_rgb_and_sets_alpha_from_opacity()
    {
        var c = OverlayGlass.WithAlpha(Base, 0.75);

        c.R.Should().Be(0x21);
        c.G.Should().Be(0x21);
        c.B.Should().Be(0x3A);
        c.A.Should().Be(191); // round(0.75 * 255)
    }

    [Fact]
    public void WithAlpha_opacity_one_is_fully_opaque()
        => OverlayGlass.WithAlpha(Base, 1.0).A.Should().Be(255);

    [Fact]
    public void WithAlpha_opacity_zero_is_fully_transparent()
        => OverlayGlass.WithAlpha(Base, 0.0).A.Should().Be(0);

    [Theory]
    [InlineData(-0.5, 0)]
    [InlineData(1.5, 255)]
    public void WithAlpha_clamps_out_of_range_opacity(double opacity, byte expectedAlpha)
        => OverlayGlass.WithAlpha(Base, opacity).A.Should().Be(expectedAlpha);
}
