using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using RandomWebpage.Services;
using SkiaSharp;

namespace RandomWebpage.Tests;

public class WebsiteToolsTests
{
    private static readonly byte[] RedPng = TestSupport.Png(SKColors.Red);

    internal static WebsiteTools Create(Action<AppOptions>? configure = null)
    {
        var options = TestSupport.Options(configure);
        var picsum = new PicsumImageService(TestSupport.Client(new StubHandler(_ => TestSupport.ImageResponse(RedPng, "image/jpeg"))));
        var urls = new UrlImageService(TestSupport.Client(new StubHandler(_ => TestSupport.ImageResponse(RedPng))), options);
        return new WebsiteTools(picsum, urls, new PaletteService(options), options, NullLogger<WebsiteTools>.Instance);
    }

    private static JsonObject Args(string json) => JsonNode.Parse(json)!.AsObject();

    [Fact]
    public void Defines_the_three_tools()
    {
        var names = WebsiteTools.Definitions().Select(t => t!["name"]!.GetValue<string>());

        Assert.Equal(["get_random_image", "get_image_from_url", "sample_image_colors"], names);
    }

    [Fact]
    public async Task Unknown_tool_is_an_error()
    {
        var result = await Create().ExecuteAsync("delete_everything", new JsonObject());

        Assert.True(result.IsError);
        Assert.Contains("Unknown tool", result.Content);
    }

    [Fact]
    public async Task Sampling_an_unknown_image_id_is_an_error()
    {
        var result = await Create().ExecuteAsync("sample_image_colors", Args("""{"image_id":"img9"}"""));

        Assert.True(result.IsError);
        Assert.Contains("img9", result.Content);
    }

    [Fact]
    public async Task Missing_url_is_an_error()
    {
        var result = await Create().ExecuteAsync("get_image_from_url", new JsonObject());

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task Non_https_url_is_a_tool_error_not_an_exception()
    {
        var tools = Create();

        var result = await tools.ExecuteAsync("get_image_from_url", Args("""{"url":"http://example.com/a.png"}"""));

        Assert.True(result.IsError);
        Assert.Empty(tools.ImageUrls);
    }

    [Fact]
    public async Task Random_image_then_sampling_colors()
    {
        var tools = Create();

        var image = await tools.ExecuteAsync("get_random_image", new JsonObject());
        Assert.False(image.IsError);
        var parsed = JsonNode.Parse(image.Content)!;
        Assert.Equal("img1", parsed["image_id"]!.GetValue<string>());
        Assert.Equal(40, parsed["width"]!.GetValue<int>());
        Assert.StartsWith("https://picsum.photos/", parsed["url"]!.GetValue<string>());

        var colors = await tools.ExecuteAsync("sample_image_colors", Args("""{"image_id":"img1","count":2}"""));
        Assert.False(colors.IsError);
        var sampled = JsonNode.Parse(colors.Content)!;
        Assert.NotEmpty(sampled["colors"]!.AsArray());
        Assert.NotNull(sampled["suggested"]!["background"]);
        Assert.Single(tools.ImageUrls);
    }

    [Fact]
    public async Task Image_from_url_is_registered_with_its_final_url()
    {
        var tools = Create();

        var result = await tools.ExecuteAsync("get_image_from_url", Args("""{"url":"https://example.com/cake.png"}"""));

        Assert.False(result.IsError);
        Assert.Equal("https://example.com/cake.png", JsonNode.Parse(result.Content)!["url"]!.GetValue<string>());
        Assert.Equal([new Uri("https://example.com/cake.png")], tools.ImageUrls);
    }

    [Fact]
    public async Task Enforces_the_per_page_image_limit()
    {
        var tools = Create(o => o.Images.MaxPerPage = 1);

        Assert.False((await tools.ExecuteAsync("get_random_image", new JsonObject())).IsError);
        var second = await tools.ExecuteAsync("get_random_image", new JsonObject());

        Assert.True(second.IsError);
        Assert.Single(tools.ImageUrls);
    }
}
