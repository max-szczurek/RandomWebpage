using System.Net;
using RandomWebpage.Services;
using SkiaSharp;

namespace RandomWebpage.Tests;

public class UrlImageServiceTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("64:ff9b::a00:1")]
    [InlineData("2002:a00:1::")]
    public void Refuses_non_public_addresses(string address) =>
        Assert.False(UrlImageService.IsPublicAddress(IPAddress.Parse(address)));

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("172.32.0.1")]
    [InlineData("100.128.0.1")]
    [InlineData("151.101.1.1")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("::ffff:8.8.8.8")]
    public void Allows_public_addresses(string address) =>
        Assert.True(UrlImageService.IsPublicAddress(IPAddress.Parse(address)));

    [Theory]
    [InlineData("http://example.com/a.png", "https")]
    [InlineData("ftp://example.com/a.png", "https")]
    [InlineData("file:///etc/passwd", "https")]
    [InlineData("/relative.png", "https")]
    [InlineData("not a url", "absolute")]
    [InlineData("https://user:pass@example.com/a.png", "credentials")]
    public async Task Rejects_bad_urls_without_a_request(string url, string messagePart)
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("must not be called"));
        var service = new UrlImageService(TestSupport.Client(handler), TestSupport.Options());

        var ex = await Assert.ThrowsAsync<ImageFetchException>(() => service.GetImageAsync(url));
        Assert.Contains(messagePart, ex.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Downloads_and_measures_an_image()
    {
        var png = TestSupport.Png(SKColors.Red, 40, 30);
        var service = new UrlImageService(
            TestSupport.Client(new StubHandler(_ => TestSupport.ImageResponse(png))), TestSupport.Options());

        var image = await service.GetImageAsync("https://example.com/cake.png");

        Assert.Equal(new Uri("https://example.com/cake.png"), image.Url);
        Assert.Equal((40, 30), (image.Width, image.Height));
        Assert.Equal(png, image.Bytes);
    }

    [Fact]
    public async Task Follows_redirects_and_reports_the_final_url()
    {
        var png = TestSupport.Png(SKColors.Blue);
        var handler = new StubHandler(r => r.RequestUri!.AbsolutePath == "/old"
            ? new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("/new.png", UriKind.Relative) } }
            : TestSupport.ImageResponse(png));
        var service = new UrlImageService(TestSupport.Client(handler), TestSupport.Options());

        var image = await service.GetImageAsync("https://example.com/old");

        Assert.Equal(new Uri("https://example.com/new.png"), image.Url);
    }

    [Fact]
    public async Task Refuses_a_redirect_to_http()
    {
        var handler = new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("http://internal.example/a.png") } });
        var service = new UrlImageService(TestSupport.Client(handler), TestSupport.Options());

        var ex = await Assert.ThrowsAsync<ImageFetchException>(() => service.GetImageAsync("https://example.com/a"));
        Assert.Contains("https", ex.Message);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Stops_after_too_many_redirects()
    {
        var handler = new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("https://example.com/loop") } });
        var service = new UrlImageService(TestSupport.Client(handler), TestSupport.Options());

        var ex = await Assert.ThrowsAsync<ImageFetchException>(() => service.GetImageAsync("https://example.com/loop"));
        Assert.Contains("redirects", ex.Message);
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task Refuses_a_non_image_content_type()
    {
        var service = new UrlImageService(
            TestSupport.Client(new StubHandler(_ => TestSupport.ImageResponse([1, 2, 3], "text/html"))), TestSupport.Options());

        var ex = await Assert.ThrowsAsync<ImageFetchException>(() => service.GetImageAsync("https://example.com/page"));
        Assert.Contains("text/html", ex.Message);
    }

    [Fact]
    public async Task Refuses_an_oversized_body()
    {
        var png = TestSupport.Png(SKColors.Green, 200, 200);
        var service = new UrlImageService(
            TestSupport.Client(new StubHandler(_ => TestSupport.ImageResponse(png))),
            TestSupport.Options(o => o.Images.MaxBytes = 100));

        var ex = await Assert.ThrowsAsync<ImageFetchException>(() => service.GetImageAsync("https://example.com/big.png"));
        Assert.Contains("limit", ex.Message);
    }

    [Fact]
    public async Task Refuses_an_unreadable_image()
    {
        var service = new UrlImageService(
            TestSupport.Client(new StubHandler(_ => TestSupport.ImageResponse([1, 2, 3, 4, 5, 6, 7, 8]))), TestSupport.Options());

        await Assert.ThrowsAsync<ImageFetchException>(() => service.GetImageAsync("https://example.com/junk.png"));
    }

    [Fact]
    public async Task Reports_an_http_error_status()
    {
        var service = new UrlImageService(
            TestSupport.Client(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))), TestSupport.Options());

        var ex = await Assert.ThrowsAsync<ImageFetchException>(() => service.GetImageAsync("https://example.com/missing.png"));
        Assert.Contains("404", ex.Message);
    }
}

public class UrlImageServiceGuardedHandlerTests
{
    private static UrlImageService Create() => new(
        new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            ConnectCallback = UrlImageService.ConnectToPublicHostAsync
        }),
        TestSupport.Options());

    [Theory]
    [InlineData("https://127.0.0.1/a.png")]
    [InlineData("https://localhost/a.png")]
    [InlineData("https://169.254.169.254/latest/meta-data/")]
    [InlineData("https://[::1]/a.png")]
    [InlineData("https://10.0.0.5:8443/a.png")]
    public async Task Real_handler_never_connects_to_non_public_hosts(string url)
    {
        var ex = await Assert.ThrowsAsync<ImageFetchException>(() => Create().GetImageAsync(url));

        Assert.Contains("public address", ex.Message);
    }
}
