using System.Net;
using RandomWebpage;
using RandomWebpage.Services;

var builder = WebApplication.CreateBuilder(args);

// ClaudeApiKey / WebsitePurpose come from user secrets (Development) or environment variables.
builder.Services
    .AddOptions<AppOptions>()
    .Bind(builder.Configuration)
    .Validate(o => !string.IsNullOrWhiteSpace(o.ClaudeApiKey),
        "ClaudeApiKey is not configured. Set it via user secrets or the ClaudeApiKey environment variable.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.WebsitePurpose), "WebsitePurpose is not configured.")
    .ValidateOnStart();

builder.Services.AddMemoryCache();

builder.Services.AddHttpClient<PicsumImageService>(c => c.Timeout = TimeSpan.FromSeconds(15));

builder.Services.AddHttpClient<ClaudeClient>((sp, c) =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AppOptions>>().Value;
    c.BaseAddress = new Uri("https://api.anthropic.com/");
    c.Timeout = TimeSpan.FromMinutes(2);
    c.DefaultRequestHeaders.Add("x-api-key", opts.ClaudeApiKey);
    c.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
});

builder.Services.AddSingleton<PaletteService>();
builder.Services.AddTransient<ClaudeModelResolver>();
builder.Services.AddTransient<PageGenerator>();

var app = builder.Build();

app.MapGet("/", LoadingPage.Render);

app.MapGet("/generate", async (
    PicsumImageService picsum,
    PaletteService paletteService,
    PageGenerator generator,
    ILogger<Program> logger,
    HttpContext context,
    CancellationToken ct) =>
{
    // Generated HTML is served as-is, so lock it down: no scripts, images only from picsum.
    context.Response.Headers.ContentSecurityPolicy =
        $"default-src 'none'; script-src 'none'; img-src {LoadingPage.ImageSources}; " +
        "style-src 'unsafe-inline'; font-src data:; base-uri 'none'; form-action 'none'";
    context.Response.Headers.CacheControl = "no-store";

    try
    {
        var image = await picsum.GetRandomImageAsync(ct);
        var palette = paletteService.BuildPalette(image.Bytes);
        logger.LogInformation("Image {Url}, palette {Palette}", image.ImageUrl, string.Join(' ', palette));

        var html = await generator.GenerateAsync(image.ImageUrl, palette, ct);
        return Results.Content(html, "text/html; charset=utf-8");
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        return Results.StatusCode(499);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to generate page");
        var message = WebUtility.HtmlEncode(ex.Message);
        return Results.Content(
            $"<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>Error</title></head>" +
            $"<body style=\"font-family:sans-serif;max-width:40em;margin:3em auto\">" +
            $"<h1>Could not generate a page</h1><p>{message}</p><p><a href=\"/\">Try again</a></p></body></html>",
            "text/html; charset=utf-8",
            statusCode: StatusCodes.Status502BadGateway);
    }
});

app.Run();
