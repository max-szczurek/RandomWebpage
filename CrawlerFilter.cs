namespace RandomWebpage;

/// <summary>
/// Stops automated clients before a page generation spends a Claude request.
/// "/" stays cheap for everyone. Only "/generate" is refused.
/// </summary>
public static class CrawlerFilter
{
    // Substrings, compared case-insensitively. "bot" covers Googlebot, Bingbot, GPTBot, and most others.
    // Script clients and a few AI fetchers never say "bot", so they are listed on their own.
    // In-app browsers (WhatsApp, Instagram, and similar) are left alone so a person can still open the site.
    private static readonly string[] Markers =
    [
        "bot",
        "spider",
        "crawler",
        "slurp",
        "archiver",
        "headless",
        "lighthouse",
        "pagespeed",
        "wget/",
        "curl/",
        "python-requests",
        "python-urllib",
        "go-http-client",
        "java/",
        "apache-httpclient",
        "libwww",
        "scrapy",
        "httpclient",
        "http-client",
        "axios/",
        "node-fetch",
        "undici",
        "postman",
        "insomnia",
        "httpie",
        "chatgpt-user",
        "claude-user",
        "anthropic-ai",
        "perplexity-user",
        "googleother",
        "google-inspectiontool",
        "mediapartners-google",
        "feedfetcher",
        "apis-google",
        "meta-external",
        "facebookexternalhit",
        "embedly",
        "iframely",
        "vkshare",
        "bingpreview",
        "cohere-ai",
        "pingdom",
        "statuscake",
        "site24x7",
        "httrack",
        "nutch",
        "heritrix",
    ];

    public static bool IsGeneratePath(PathString path) =>
        path.Equals("/generate", StringComparison.OrdinalIgnoreCase);

    /// <summary>True for user agents that identify as crawlers or HTTP libraries, not for a normal browser.</summary>
    public static bool IsKnownCrawler(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
            return false;

        if (userAgent.Length > 512)
            return true;

        // Cubot phones put that brand in the user agent, and the brand contains "bot".
        var cubotPhone = userAgent.Contains("cubot", StringComparison.OrdinalIgnoreCase);
        foreach (var marker in Markers)
        {
            if (cubotPhone && marker == "bot")
                continue;

            if (userAgent.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static bool BlocksGeneration(HttpRequest request, out string reason)
    {
        var userAgent = request.Headers.UserAgent.ToString();
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            reason = "missing user agent";
            return true;
        }

        if (IsKnownCrawler(userAgent))
        {
            reason = "crawler user agent";
            return true;
        }

        if (HasToken(request, "Purpose", "prefetch") ||
            HasToken(request, "Sec-Purpose", "prefetch") ||
            HasToken(request, "X-Purpose", "preview") ||
            HasToken(request, "X-Moz", "prefetch"))
        {
            reason = "prefetch";
            return true;
        }

        if (IsEmbeddedCrossSite(request))
        {
            reason = "cross-site embed";
            return true;
        }

        reason = "";
        return false;
    }

    public static async Task Invoke(HttpContext context, RequestDelegate next)
    {
        context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";

        if (IsGeneratePath(context.Request.Path) && BlocksGeneration(context.Request, out var reason))
        {
            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("RandomWebpage.CrawlerFilter");
            logger.LogInformation(
                "Blocked automated client before generation ({Reason}). UserAgent: {UserAgent}",
                reason,
                context.Request.Headers.UserAgent.ToString());

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.ContentSecurityPolicy =
                "default-src 'none'; script-src 'none'; style-src 'unsafe-inline'; img-src 'none'; base-uri 'none'; form-action 'none'";
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(
                SimplePage.Render(
                    "No page for this client",
                    "This request looks automated. A page is built only for a normal browser visit, because each page calls a paid API."),
                context.RequestAborted);
            return;
        }

        await next(context);
    }

    // A link click is a document navigation and is allowed. An image, iframe, or fetch
    // from another site is not: the browser would still hit /generate and spend a call.
    private static bool IsEmbeddedCrossSite(HttpRequest request)
    {
        if (!request.Headers.TryGetValue("Sec-Fetch-Site", out var site))
            return false;

        var siteValue = site.ToString();
        if (!siteValue.Equals("cross-site", StringComparison.OrdinalIgnoreCase) &&
            !siteValue.Equals("same-site", StringComparison.OrdinalIgnoreCase))
            return false;

        var mode = request.Headers["Sec-Fetch-Mode"].ToString();
        var dest = request.Headers["Sec-Fetch-Dest"].ToString();
        var typedOrClicked = mode.Equals("navigate", StringComparison.OrdinalIgnoreCase)
            && dest.Equals("document", StringComparison.OrdinalIgnoreCase);
        return !typedOrClicked;
    }

    private static bool HasToken(HttpRequest request, string header, string token) =>
        request.Headers.TryGetValue(header, out var value) &&
        value.ToString().Contains(token, StringComparison.OrdinalIgnoreCase);
}
