using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace RandomWebpage.Services;

public sealed record ToolResult(string Content, bool IsError);

/// <summary>
/// The tools Claude may call while building a page. One instance serves one page generation: it keeps the
/// downloaded image bytes on the server (Claude only ever sees an image id, URL and size) and records the
/// image URLs that were handed out, so the response's Content-Security-Policy can allow exactly those.
/// </summary>
public sealed class WebsiteTools(
    PicsumImageService picsum,
    UrlImageService urlImages,
    PaletteService palette,
    IOptions<AppOptions> options,
    ILogger<WebsiteTools> logger)
{
    public const string GetRandomImage = "get_random_image";
    public const string GetImageFromUrl = "get_image_from_url";
    public const string SampleImageColors = "sample_image_colors";

    private const int MaxPaletteColors = 16;

    private readonly Dictionary<string, FetchedImage> _images = [];

    /// <summary>URLs of every image the tools returned during this page generation.</summary>
    public IReadOnlyList<Uri> ImageUrls => _images.Values.Select(i => i.Url).ToList();

    public static JsonArray Definitions() =>
    [
        Tool(GetRandomImage,
            "Downloads a random photo from picsum.photos (300x200). Returns an image_id, the stable https url to " +
            "embed in an <img> tag, and the pixel size. Call this only when the website purpose asks for a random image.",
            new JsonObject()),
        Tool(GetImageFromUrl,
            "Downloads the image at a specific https URL that the website purpose provides. Returns an image_id, the " +
            "final https url to embed in an <img> tag, and the pixel size. Only https URLs to public hosts work. " +
            "Call this only when the website purpose names an image URL.",
            new JsonObject { ["url"] = new JsonObject { ["type"] = "string", ["description"] = "Absolute https URL of the image." } },
            "url"),
        Tool(SampleImageColors,
            "Samples pixels from an image returned by get_random_image or get_image_from_url and returns its most " +
            "common distinct colors as hex codes, most common first, plus a suggested background, text and accent " +
            "color. Call this only when the website purpose asks for colors taken from an image.",
            new JsonObject
            {
                ["image_id"] = new JsonObject { ["type"] = "string", ["description"] = "The image_id of a downloaded image." },
                ["count"] = new JsonObject
                {
                    ["type"] = "integer",
                    ["description"] = $"How many colors to return (1 to {MaxPaletteColors}). Optional."
                }
            },
            "image_id")
    ];

    public async Task<ToolResult> ExecuteAsync(string name, JsonObject input, CancellationToken ct = default)
    {
        try
        {
            var result = name switch
            {
                GetRandomImage => await RandomImageAsync(ct),
                GetImageFromUrl => await ImageFromUrlAsync(input, ct),
                SampleImageColors => SampleColors(input),
                _ => throw new ToolInputException($"Unknown tool '{name}'.")
            };

            logger.LogInformation("Tool {Tool} succeeded: {Result}", name, result);
            return new ToolResult(result, IsError: false);
        }
        catch (Exception ex) when (ex is ToolInputException or ImageFetchException)
        {
            logger.LogInformation("Tool {Tool} refused: {Message}", name, ex.Message);
            return new ToolResult(ex.Message, IsError: true);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Tool {Tool} failed", name);
            return new ToolResult($"The tool failed: {ex.Message}", IsError: true);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Tool {Tool} failed", name);
            return new ToolResult(ex.Message, IsError: true);
        }
    }

    private async Task<string> RandomImageAsync(CancellationToken ct)
    {
        EnsureRoomForImage();
        var image = await picsum.GetRandomImageAsync(ct);
        var url = UrlImageService.ParseHttpsUrl(image.ImageUrl);
        var size = ImageProbe.TryGetSize(image.Bytes)
            ?? throw new ToolInputException("The random image could not be read.");
        return Register(new FetchedImage(image.Bytes, url, size.Width, size.Height));
    }

    private async Task<string> ImageFromUrlAsync(JsonObject input, CancellationToken ct)
    {
        var url = input["url"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(url))
            throw new ToolInputException("The 'url' argument is required.");

        EnsureRoomForImage();
        return Register(await urlImages.GetImageAsync(url, ct));
    }

    private string SampleColors(JsonObject input)
    {
        var id = input["image_id"]?.GetValue<string>();
        if (id is null || !_images.TryGetValue(id, out var image))
            throw new ToolInputException($"Unknown image_id '{id}'. Download an image first and use the image_id it returns.");

        int? count = null;
        if (input["count"] is JsonValue c && c.TryGetValue<int>(out var n))
            count = Math.Clamp(n, 1, MaxPaletteColors);

        var colors = palette.BuildPalette(image.Bytes, count);
        var roles = PaletteService.SuggestRoles(colors);
        return new JsonObject
        {
            ["image_id"] = id,
            ["colors"] = new JsonArray(colors.Select(c => (JsonNode)JsonValue.Create(c)!).ToArray()),
            ["suggested"] = new JsonObject
            {
                ["background"] = roles.Background,
                ["text"] = roles.Text,
                ["accent"] = roles.Accent
            }
        }.ToJsonString();
    }

    private void EnsureRoomForImage()
    {
        var max = options.Value.Images.MaxPerPage;
        if (_images.Count >= max)
            throw new ToolInputException($"At most {max} images can be used on one page.");
    }

    private string Register(FetchedImage image)
    {
        var id = $"img{_images.Count + 1}";
        _images[id] = image;
        return new JsonObject
        {
            ["image_id"] = id,
            ["url"] = image.Url.ToString(),
            ["width"] = image.Width,
            ["height"] = image.Height
        }.ToJsonString();
    }

    private static JsonObject Tool(string name, string description, JsonObject properties, params string[] required) => new()
    {
        ["name"] = name,
        ["description"] = description,
        ["input_schema"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = new JsonArray(required.Select(r => (JsonNode)JsonValue.Create(r)!).ToArray())
        }
    };

    private sealed class ToolInputException(string message) : Exception(message);
}
