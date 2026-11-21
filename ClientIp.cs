using System.Net;

namespace RandomWebpage;

/// <summary>
/// Address used for generation rate limits.
/// Azure App Service appends the client it actually saw to X-Forwarded-For, so the last hop
/// is the visitor. A value the caller sent themselves sits further left and is ignored.
/// Outside App Service the socket address is used, and a caller-supplied forwarded header is not.
/// </summary>
public static class ClientIp
{
    public static string Resolve(HttpContext context)
    {
        if (IsAzureAppService() &&
            context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwarded))
        {
            var parsed = LastHop(forwarded.ToString());
            if (parsed is not null)
                return parsed;
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private static string? LastHop(string header)
    {
        var parts = header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return null;

        var last = parts[^1].Trim().Trim('"');
        if (IPAddress.TryParse(last, out var ip))
            return ip.ToString();

        if (last.StartsWith('[') && last.Contains(']'))
        {
            var end = last.IndexOf(']');
            if (IPAddress.TryParse(last[1..end], out ip))
                return ip.ToString();
        }

        // IPv4 with a port, which some proxies append.
        var colon = last.LastIndexOf(':');
        if (colon > 0 && last.IndexOf(':') == colon &&
            IPAddress.TryParse(last[..colon], out ip))
            return ip.ToString();

        return null;
    }

    private static bool IsAzureAppService() =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME"));
}
