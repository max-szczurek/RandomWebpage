using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace RandomWebpage.Services;

public sealed class PaletteService(IOptions<AppOptions> options)
{
    private const int PixelSamples = 200;
    private const int BucketStep = 32;
    private const int MinColorDistance = 40;
    private const int MaxCachedPalettes = 1000;

    private readonly MemoryCache _palettes = new(new MemoryCacheOptions { SizeLimit = MaxCachedPalettes });

    /// <summary>
    /// Samples random pixels and returns the most common (quantized) colors as hex strings. The result is cached
    /// by image content, so the same image gets the same palette on every visit until the entry expires
    /// (<c>Images:CacheMinutes</c>).
    /// </summary>
    public IReadOnlyList<string> BuildPalette(byte[] imageBytes, int? count = null)
    {
        var wanted = Math.Max(1, count ?? options.Value.Palette.SampleCount);
        var minutes = options.Value.Images.CacheMinutes;
        if (minutes <= 0)
            return Sample(imageBytes, wanted);

        var key = $"{Convert.ToHexString(SHA256.HashData(imageBytes))}:{wanted}";
        if (_palettes.TryGetValue(key, out IReadOnlyList<string>? cached) && cached is not null)
            return cached;

        var palette = Sample(imageBytes, wanted);
        _palettes.Set(key, palette, new MemoryCacheEntryOptions
        {
            Size = 1,
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(minutes)
        });
        return palette;
    }

    private static IReadOnlyList<string> Sample(byte[] imageBytes, int wanted)
    {

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

    /// <summary>
    /// Picks a background, text and accent color from a palette. The background is the lightest swatch if it
    /// is light enough to be a page background, otherwise the darkest; text is the swatch with the best contrast against it, or
    /// black or white when no swatch reaches a readable ratio; the accent is the most saturated other swatch.
    /// </summary>
    public static PaletteRoles SuggestRoles(IReadOnlyList<string> palette)
    {
        var colors = palette.Select(hex => (Hex: hex, Rgb: ParseHex(hex))).ToList();
        if (colors.Count == 0)
            return new PaletteRoles("#FFFFFF", "#000000", "#000000");

        var lightest = colors.MaxBy(c => Luminance(c.Rgb));
        var background = Luminance(lightest.Rgb) >= 0.5 ? lightest : colors.MinBy(c => Luminance(c.Rgb));

        var text = colors
            .Where(c => c.Hex != background.Hex)
            .OrderByDescending(c => Contrast(c.Rgb, background.Rgb))
            .FirstOrDefault();
        var textHex = text.Hex is not null && Contrast(text.Rgb, background.Rgb) >= 4.5
            ? text.Hex
            : (Luminance(background.Rgb) > 0.5 ? "#000000" : "#FFFFFF");

        var accent = colors
            .Where(c => c.Hex != background.Hex && c.Hex != textHex)
            .OrderByDescending(c => Saturation(c.Rgb))
            .FirstOrDefault();

        return new PaletteRoles(background.Hex, textHex, accent.Hex ?? textHex);
    }

    private static (int R, int G, int B) ParseHex(string hex) =>
        (Convert.ToInt32(hex[1..3], 16), Convert.ToInt32(hex[3..5], 16), Convert.ToInt32(hex[5..7], 16));

    private static double Luminance((int R, int G, int B) c)
    {
        static double Channel(int v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    private static double Contrast((int R, int G, int B) a, (int R, int G, int B) b)
    {
        var (hi, lo) = (Math.Max(Luminance(a), Luminance(b)), Math.Min(Luminance(a), Luminance(b)));
        return (hi + 0.05) / (lo + 0.05);
    }

    private static double Saturation((int R, int G, int B) c)
    {
        var max = Math.Max(c.R, Math.Max(c.G, c.B));
        var min = Math.Min(c.R, Math.Min(c.G, c.B));
        return max == 0 ? 0 : (max - min) / (double)max;
    }

    // Round to the nearest multiple of BucketStep, clamped to 0..255.
    private static int Quantize(byte value) =>
        Math.Min(255, (int)Math.Round(value / (double)BucketStep) * BucketStep);

    private static double Distance((int R, int G, int B) a, (int R, int G, int B) b) =>
        Math.Sqrt(Math.Pow(a.R - b.R, 2) + Math.Pow(a.G - b.G, 2) + Math.Pow(a.B - b.B, 2));
}

public sealed record PaletteRoles(string Background, string Text, string Accent);
