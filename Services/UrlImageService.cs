using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace RandomWebpage.Services;

public sealed record FetchedImage(byte[] Bytes, Uri Url, int Width, int Height);

/// <summary>Thrown for a URL or response the app refuses to use. The message is safe to show to the model.</summary>
public sealed class ImageFetchException(string message) : Exception(message);

public static class ImageProbe
{
    /// <summary>Reads the pixel size from the image header, or returns null if SkiaSharp cannot decode the format.</summary>
    public static (int Width, int Height)? TryGetSize(byte[] bytes)
    {
        using var codec = SKCodec.Create(new MemoryStream(bytes, writable: false));
        return codec is null ? null : (codec.Info.Width, codec.Info.Height);
    }
}

/// <summary>
/// Downloads an image from a URL given in the website purpose. The URL is untrusted input as far as the
/// network is concerned, so only public https hosts are allowed, redirects are re-checked hop by hop, and
/// the body is size-limited and must decode as an image. Only images that pass every check are cached, so a
/// refused URL or a transient failure is retried on the next visit.
/// </summary>
public sealed class UrlImageService(HttpClient http, IOptions<AppOptions> options, ImageCache? cache = null)
{
    private const int MaxRedirects = 3;

    public async Task<FetchedImage> GetImageAsync(string url, CancellationToken ct = default)
    {
        var current = ParseHttpsUrl(url);
        var cacheKey = current.AbsoluteUri;
        if (cache is not null && cache.TryGet(cacheKey, out var cached))
            return cached;

        var maxBytes = options.Value.Images.MaxBytes;

        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.Accept.ParseAdd("image/*");
            request.Headers.UserAgent.ParseAdd("RandomWebpage/1.0 (image fetch tool)");

            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            }
            catch (HttpRequestException ex)
            {
                throw new ImageFetchException($"Could not download the image: {ex.InnerException?.Message ?? ex.Message}");
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new ImageFetchException("The image download timed out.");
            }

            using (response)
            {
                if (IsRedirect(response.StatusCode))
                {
                    var location = response.Headers.Location
                        ?? throw new ImageFetchException("The server sent a redirect with no Location.");
                    current = ParseHttpsUrl(new Uri(current, location).ToString());
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                    throw new ImageFetchException($"The server returned HTTP {(int)response.StatusCode}.");

                var mediaType = response.Content.Headers.ContentType?.MediaType;
                if (mediaType is null || !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    throw new ImageFetchException($"The URL did not return an image (content type: {mediaType ?? "none"}).");

                if (response.Content.Headers.ContentLength > maxBytes)
                    throw new ImageFetchException($"The image is larger than the {maxBytes} byte limit.");

                var bytes = await ReadLimitedAsync(response, maxBytes, ct);
                var size = ImageProbe.TryGetSize(bytes)
                    ?? throw new ImageFetchException("The downloaded file is not an image format that can be read (try JPEG, PNG, GIF or WebP).");

                var image = new FetchedImage(bytes, current, size.Width, size.Height);
                cache?.Set(cacheKey, image);
                return image;
            }
        }

        throw new ImageFetchException($"Too many redirects (more than {MaxRedirects}).");
    }

    internal static Uri ParseHttpsUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new ImageFetchException("The URL is not a valid absolute URL.");
        if (uri.Scheme != Uri.UriSchemeHttps)
            throw new ImageFetchException("Only https:// URLs are allowed.");
        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw new ImageFetchException("URLs with embedded credentials are not allowed.");
        return uri;
    }

    private static bool IsRedirect(HttpStatusCode code) =>
        code is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static async Task<byte[]> ReadLimitedAsync(HttpResponseMessage response, int maxBytes, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > maxBytes)
                throw new ImageFetchException($"The image is larger than the {maxBytes} byte limit.");
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Connect callback for the production handler. Resolving DNS here, and connecting to the address that
    /// was checked, closes the gap where a hostname resolves to a public address for a check and a private
    /// one for the connection.
    /// </summary>
    public static async ValueTask<Stream> ConnectToPublicHostAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var host = context.DnsEndPoint.Host;
        var addresses = await Dns.GetHostAddressesAsync(host, ct);
        if (addresses.Length == 0 || addresses.Any(a => !IsPublicAddress(a)))
            throw new HttpRequestException($"Host '{host}' does not resolve to a public address.");

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) ||
            address.Equals(IPAddress.None))
            return false;

        var b = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return !(b[0] == 0 ||                                   // 0.0.0.0/8
                     b[0] == 10 ||                                  // private
                     (b[0] == 100 && b[1] is >= 64 and <= 127) ||   // carrier-grade NAT
                     b[0] == 127 ||                                 // loopback
                     (b[0] == 169 && b[1] == 254) ||                // link-local, cloud metadata
                     (b[0] == 172 && b[1] is >= 16 and <= 31) ||    // private
                     (b[0] == 192 && b[1] == 0 && b[2] == 0) ||     // IETF protocol assignments
                     (b[0] == 192 && b[1] == 168) ||                // private
                     (b[0] == 198 && b[1] is 18 or 19) ||           // benchmarking
                     b[0] >= 224);                                  // multicast and reserved
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return !(address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast ||
                     address.IsIPv6Teredo ||
                     (b[0] & 0xFE) == 0xFC ||                        // unique local fc00::/7
                     (b[0] == 0x20 && b[1] == 0x02) ||               // 6to4 embeds an IPv4 address
                     (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B)); // NAT64
        }

        return false;
    }
}
