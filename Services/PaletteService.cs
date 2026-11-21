using Microsoft.Extensions.Options;
using SkiaSharp;

namespace RandomWebpage.Services;

public sealed class PaletteService(IOptions<AppOptions> options)
{
    private const int PixelSamples = 200;
    private const int BucketStep = 32;
    private const int MinColorDistance = 40;

    /// <summary>Samples random pixels and returns the most common (quantized) colors as hex strings.</summary>
    public IReadOnlyList<string> BuildPalette(byte[] imageBytes)
    {
        var wanted = Math.Max(1, options.Value.Palette.SampleCount);

        using var image = SKBitmap.Decode(imageBytes)
            ?? throw new InvalidOperationException("Could not decode the downloaded image.");

        var counts = new Dictionary<(int R, int G, int B), int>();
        for (var i = 0; i < PixelSamples; i++)
        {
            var pixel = image.GetPixel(Random.Shared.Next(image.Width), Random.Shared.Next(image.Height));
            var key = (Quantize(pixel.Red), Quantize(pixel.Green), Quantize(pixel.Blue));
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        var chosen = new List<(int R, int G, int B)>();
        foreach (var (color, _) in counts.OrderByDescending(kv => kv.Value))
        {
            if (chosen.Any(c => Distance(c, color) < MinColorDistance))
                continue;

            chosen.Add(color);
            if (chosen.Count == wanted)
                break;
        }

        return chosen.Select(c => $"#{c.R:X2}{c.G:X2}{c.B:X2}").ToList();
    }

    // Round to the nearest multiple of BucketStep, clamped to 0..255.
    private static int Quantize(byte value) =>
        Math.Min(255, (int)Math.Round(value / (double)BucketStep) * BucketStep);

    private static double Distance((int R, int G, int B) a, (int R, int G, int B) b) =>
        Math.Sqrt(Math.Pow(a.R - b.R, 2) + Math.Pow(a.G - b.G, 2) + Math.Pow(a.B - b.B, 2));
}
