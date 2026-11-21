using System.Net;
using RandomWebpage.Services;
using SkiaSharp;

namespace RandomWebpage.Tests;

public class ImageCachingTests
{
    private static (UrlImageService Service, StubHandler Handler) Create(
        Func<HttpRequestMessage, HttpResponseMessage> respond, Action<AppOptions>? configure = null)
    {
        var options = TestSupport.Options(configure);
        var handler = new StubHandler(respond);
        return (new UrlImageService(TestSupport.Client(handler), options, new ImageCache(options)), handler);
    }

    private static HttpResponseMessage Png(HttpRequestMessage _) => TestSupport.ImageResponse(TestSupport.Png(SKColors.Red));

    [Fact]
    public async Task Fetches_the_same_url_only_once()
    {
        var (service, handler) = Create(Png);

        var first = await service.GetImageAsync("https://example.com/cake.png");
        var second = await service.GetImageAsync("https://example.com/cake.png");

        Assert.Single(handler.Requests);
        Assert.Same(first, second);
    }

    [Fact]
    public async Task A_cache_hit_keeps_the_final_url_of_a_redirect()
    {
        var (service, handler) = Create(r => r.RequestUri!.AbsolutePath == "/old"
            ? new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("/new.png", UriKind.Relative) } }
            : Png(r));

        await service.GetImageAsync("https://example.com/old");
        var again = await service.GetImageAsync("https://example.com/old");

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(new Uri("https://example.com/new.png"), again.Url);
    }

    [Fact]
    public async Task Different_urls_are_cached_separately()
    {
        var (service, handler) = Create(Png);

        await service.GetImageAsync("https://example.com/a.png");
        await service.GetImageAsync("https://example.com/b.png");
        await service.GetImageAsync("https://example.com/a.png");

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Failures_are_not_cached()
    {
        var calls = 0;
        var (service, handler) = Create(r => ++calls == 1 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Png(r));

        await Assert.ThrowsAsync<ImageFetchException>(() => service.GetImageAsync("https://example.com/a.png"));
        var image = await service.GetImageAsync("https://example.com/a.png");

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(40, image.Width);
    }

    [Fact]
    public async Task Zero_minutes_turns_the_cache_off()
    {
        var (service, handler) = Create(Png, o => o.Images.CacheMinutes = 0);

        await service.GetImageAsync("https://example.com/a.png");
        await service.GetImageAsync("https://example.com/a.png");

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Does_not_cache_beyond_the_size_limit()
    {
        var png = TestSupport.Png(SKColors.Red);
        var (service, handler) = Create(_ => TestSupport.ImageResponse(png), o => o.Images.CacheMaxBytes = png.Length + 10);

        await service.GetImageAsync("https://example.com/a.png");
        await service.GetImageAsync("https://example.com/b.png");
        await service.GetImageAsync("https://example.com/b.png");

        Assert.Equal(2, handler.Requests.Count(u => u.AbsolutePath == "/b.png"));
    }

    [Fact]
    public async Task Validation_still_applies_on_a_miss_after_a_cached_hit()
    {
        var (service, _) = Create(Png);
        await service.GetImageAsync("https://example.com/a.png");

        await Assert.ThrowsAsync<ImageFetchException>(() => service.GetImageAsync("http://example.com/a.png"));
    }
}

public class PaletteCachingTests
{
    private static byte[] NoisyImage()
    {
        var random = new Random(7);
        using var bitmap = new SKBitmap(64, 64);
        for (var x = 0; x < 64; x++)
            for (var y = 0; y < 64; y++)
                bitmap.SetPixel(x, y, new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256)));
        using var data = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public void The_same_image_gets_the_same_palette_every_time()
    {
        var service = new PaletteService(TestSupport.Options());
        var bytes = NoisyImage();

        var first = service.BuildPalette(bytes, 6);

        for (var i = 0; i < 10; i++)
            Assert.Same(first, service.BuildPalette(bytes, 6));
    }

    [Fact]
    public void A_different_count_is_a_different_entry()
    {
        var service = new PaletteService(TestSupport.Options());
        var bytes = NoisyImage();

        Assert.Equal(3, service.BuildPalette(bytes, 3).Count);
        Assert.Equal(5, service.BuildPalette(bytes, 5).Count);
    }

    [Fact]
    public void Different_images_do_not_share_a_palette()
    {
        var service = new PaletteService(TestSupport.Options());

        var red = service.BuildPalette(TestSupport.Png(SKColors.Red));
        var blue = service.BuildPalette(TestSupport.Png(SKColors.Blue));

        Assert.NotEqual(red, blue);
    }

    [Fact]
    public void Zero_minutes_turns_the_cache_off()
    {
        var service = new PaletteService(TestSupport.Options(o => o.Images.CacheMinutes = 0));
        var bytes = NoisyImage();

        Assert.NotSame(service.BuildPalette(bytes, 6), service.BuildPalette(bytes, 6));
    }
}
