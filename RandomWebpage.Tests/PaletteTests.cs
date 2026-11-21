using RandomWebpage.Services;
using SkiaSharp;

namespace RandomWebpage.Tests;

public class PaletteTests
{
    [Fact]
    public void Samples_the_requested_number_of_distinct_colors()
    {
        using var bitmap = new SKBitmap(100, 100);
        using (var canvas = new SKCanvas(bitmap))
        {
            var colors = new[] { SKColors.Red, SKColors.Blue, SKColors.Lime, SKColors.Yellow };
            for (var i = 0; i < colors.Length; i++)
                canvas.DrawRect(i * 25, 0, 25, 100, new SKPaint { Color = colors[i] });
        }

        using var data = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);
        var service = new PaletteService(TestSupport.Options());

        var palette = service.BuildPalette(data.ToArray(), count: 3);

        Assert.Equal(3, palette.Count);
        Assert.Equal(palette.Count, palette.Distinct().Count());
        Assert.All(palette, hex => Assert.Matches("^#[0-9A-F]{6}$", hex));
    }

    [Fact]
    public void Suggests_readable_roles()
    {
        var roles = PaletteService.SuggestRoles(["#F5E6CA", "#2B1B17", "#C0392B"]);

        Assert.Equal("#F5E6CA", roles.Background);
        Assert.Equal("#2B1B17", roles.Text);
        Assert.Equal("#C0392B", roles.Accent);
    }

    [Fact]
    public void Falls_back_to_black_or_white_text_when_no_swatch_is_readable()
    {
        var roles = PaletteService.SuggestRoles(["#808080", "#858585"]);

        Assert.Contains(roles.Text, new[] { "#000000", "#FFFFFF" });
    }

    [Fact]
    public void Handles_an_empty_palette() =>
        Assert.Equal("#FFFFFF", PaletteService.SuggestRoles([]).Background);
}
