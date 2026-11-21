using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace RandomWebpage;

/// <summary>
/// Caps how often /generate may call Claude. Crawlers that spoof a browser user agent
/// still have to spend these permits, and so does a visitor mashing refresh.
/// </summary>
public static class TrafficLimits
{
    private const string ContentSecurityPolicy =
        "default-src 'none'; script-src 'none'; style-src 'unsafe-inline'; img-src 'none'; base-uri 'none'; form-action 'none'";

    public static void Configure(RateLimiterOptions options)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = OnRejected;
        options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
            PartitionedRateLimiter.Create<HttpContext, string>(PerIpConcurrency),
            PartitionedRateLimiter.Create<HttpContext, string>(PerIpWindow),
            PartitionedRateLimiter.Create<HttpContext, string>(GlobalWindow));
    }

    private static RateLimitPartition<string> PerIpConcurrency(HttpContext context)
    {
        if (!CrawlerFilter.IsGeneratePath(context.Request.Path))
            return RateLimitPartition.GetNoLimiter("other");

        var traffic = Limits(context);
        return RateLimitPartition.GetConcurrencyLimiter(
            "conc:" + ClientIp.Resolve(context),
            _ => new ConcurrencyLimiterOptions
            {
                PermitLimit = traffic.PerIpConcurrency,
                QueueLimit = 0
            });
    }

    private static RateLimitPartition<string> PerIpWindow(HttpContext context)
    {
        if (!CrawlerFilter.IsGeneratePath(context.Request.Path))
            return RateLimitPartition.GetNoLimiter("other");

        var traffic = Limits(context);
        return RateLimitPartition.GetFixedWindowLimiter(
            "ip:" + ClientIp.Resolve(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = traffic.PerIpPermitLimit,
                Window = TimeSpan.FromMinutes(traffic.PerIpWindowMinutes),
                QueueLimit = 0
            });
    }

    private static RateLimitPartition<string> GlobalWindow(HttpContext context)
    {
        if (!CrawlerFilter.IsGeneratePath(context.Request.Path))
            return RateLimitPartition.GetNoLimiter("other");

        var traffic = Limits(context);
        return RateLimitPartition.GetFixedWindowLimiter(
            "global",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = traffic.GlobalPermitLimit,
                Window = TimeSpan.FromMinutes(traffic.GlobalWindowMinutes),
                QueueLimit = 0
            });
    }

    private static TrafficOptions Limits(HttpContext context) =>
        context.RequestServices.GetRequiredService<IOptions<AppOptions>>().Value.Traffic;

    private static async ValueTask OnRejected(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var http = context.HttpContext;
        var logger = http.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("RandomWebpage.Traffic");
        logger.LogInformation("Rate limited /generate from {Ip}", ClientIp.Resolve(http));

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
            http.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        }

        http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers.ContentSecurityPolicy = ContentSecurityPolicy;
        http.Response.ContentType = "text/html; charset=utf-8";
        await http.Response.WriteAsync(
            SimplePage.Render(
                "Too many pages",
                "A lot of pages were generated in a short time. Wait a few minutes, then try again.",
                offerRetry: true),
            cancellationToken);
    }
}
