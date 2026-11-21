using System.Net;
using Microsoft.Extensions.Options;
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
    .Validate(o => o.Traffic.PerIpPermitLimit > 0, "Traffic:PerIpPermitLimit must be greater than 0.")
    .Validate(o => o.Traffic.PerIpWindowMinutes is > 0 and <= 1440, "Traffic:PerIpWindowMinutes must be from 1 to 1440.")
    .Validate(o => o.Traffic.PerIpConcurrency > 0, "Traffic:PerIpConcurrency must be greater than 0.")
    .Validate(o => o.Traffic.GlobalPermitLimit > 0, "Traffic:GlobalPermitLimit must be greater than 0.")
    .Validate(o => o.Traffic.GlobalWindowMinutes is > 0 and <= 1440, "Traffic:GlobalWindowMinutes must be from 1 to 1440.")
    .Validate(o => o.Claude.MaxToolRounds is >= 0 and <= 10, "Claude:MaxToolRounds must be from 0 to 10.")
    .Validate(o => o.Images.MaxBytes > 0, "Images:MaxBytes must be greater than 0.")
    .Validate(o => o.Images.TimeoutSeconds is > 0 and <= 120, "Images:TimeoutSeconds must be from 1 to 120.")
    .Validate(o => o.Images.MaxPerPage > 0, "Images:MaxPerPage must be greater than 0.")
    .Validate(o => o.Images.CacheMinutes is >= 0 and <= 1440, "Images:CacheMinutes must be from 0 to 1440.")
    .Validate(o => o.Images.CacheMaxBytes > 0, "Images:CacheMaxBytes must be greater than 0.")
    .ValidateOnStart();

builder.Services.AddMemoryCache();
builder.Services.AddRateLimiter(TrafficLimits.Configure);

builder.Services.AddHttpClient<PicsumImageService>(c => c.Timeout = TimeSpan.FromSeconds(15));

builder.Services
    .AddHttpClient<UrlImageService>((sp, c) =>
        c.Timeout = TimeSpan.FromSeconds(sp.GetRequiredService<IOptions<AppOptions>>().Value.Images.TimeoutSeconds))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        // Redirects are followed by UrlImageService so every hop is re-validated, and DNS results are
        // checked in the connect callback, so no proxy may sit in between.
        AllowAutoRedirect = false,
        UseProxy = false,
        ConnectCallback = UrlImageService.ConnectToPublicHostAsync
    });

builder.Services.AddHttpClient<ClaudeClient>((sp, c) =>
{
    var opts = sp.GetRequiredService<IOptions<AppOptions>>().Value;
    c.BaseAddress = new Uri("https://api.anthropic.com/");
    c.Timeout = TimeSpan.FromMinutes(2);
    c.DefaultRequestHeaders.Add("x-api-key", opts.ClaudeApiKey);
    c.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
});

builder.Services.AddSingleton<ImageCache>();
builder.Services.AddSingleton<PaletteService>();
builder.Services.AddTransient<ClaudeModelResolver>();
builder.Services.AddScoped<WebsiteTools>();
builder.Services.AddScoped<PageGenerator>();

var app = builder.Build();

var traffic = app.Services.GetRequiredService<IOptions<AppOptions>>().Value.Traffic;
app.Logger.LogInformation(
    "Page generation limits: {PerIpConcurrency} at a time and {PerIpPermitLimit} per {PerIpWindowMinutes} min for each IP, {GlobalPermitLimit} per {GlobalWindowMinutes} min for the whole site",
    traffic.PerIpConcurrency,
    traffic.PerIpPermitLimit,
    traffic.PerIpWindowMinutes,
    traffic.GlobalPermitLimit,
    traffic.GlobalWindowMinutes);

// Reject crawlers before the rate limiter, so a bot does not consume a visitor's budget or a Claude call.
app.Use(CrawlerFilter.Invoke);
app.UseRateLimiter();

app.MapGet("/", LoadingPage.Render);

app.MapGet("/robots.txt", async (HttpContext context) =>
{
    context.Response.Headers.CacheControl = "public, max-age=86400";
    context.Response.ContentType = "text/plain; charset=utf-8";
    await context.Response.WriteAsync(RobotsTxt.Body);
});

app.MapGet("/generate", async (
    PageGenerator generator,
    ILogger<Program> logger,
    HttpContext context,
    CancellationToken ct) =>
{
    // Generated HTML is served as-is, so lock it down: no scripts, and images only from the hosts the
    // image tools actually returned for this page. Replaced below once the page is generated.
    context.Response.Headers.ContentSecurityPolicy = GeneratedPagePolicy.Build([]);
    context.Response.Headers.CacheControl = "no-store";

    try
    {
        var page = await generator.GenerateAsync(ct);
        context.Response.Headers.ContentSecurityPolicy = GeneratedPagePolicy.Build(page.ImageUrls);
        return Results.Content(page.Html, "text/html; charset=utf-8");
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
