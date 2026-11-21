using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace RandomWebpage.Tests;

internal sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<Uri> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request.RequestUri!);
        var response = respond(request);
        response.RequestMessage = request;
        return Task.FromResult(response);
    }
}

internal static class TestSupport
{
    public static IOptions<AppOptions> Options(Action<AppOptions>? configure = null)
    {
        var o = new AppOptions { ClaudeApiKey = "test", WebsitePurpose = "A test site." };
        configure?.Invoke(o);
        return Microsoft.Extensions.Options.Options.Create(o);
    }

    public static byte[] Png(SKColor fill, int width = 40, int height = 30)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(fill);
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    public static HttpResponseMessage ImageResponse(byte[] bytes, string contentType = "image/png")
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        response.Content.Headers.ContentType = new(contentType);
        return response;
    }

    public static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpClient Client(StubHandler handler, string? baseAddress = null) =>
        new(handler) { BaseAddress = baseAddress is null ? null : new Uri(baseAddress) };
}
