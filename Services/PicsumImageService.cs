namespace RandomWebpage.Services;

public sealed record PicsumImage(byte[] Bytes, string ImageUrl);

public sealed class PicsumImageService(HttpClient http)
{
    public const string RandomImageUrl = "https://picsum.photos/300/200.jpg";

    /// <summary>
    /// Fetches a random 300x200 image. picsum redirects to a fixed image URL, and HttpClient follows
    /// the redirect, so the final request URI is the stable URL the generated page can embed.
    /// </summary>
    public async Task<PicsumImage> GetRandomImageAsync(CancellationToken ct = default)
    {
        using var response = await http.GetAsync(RandomImageUrl, ct);
        response.EnsureSuccessStatusCode();

        var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? RandomImageUrl;
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        return new PicsumImage(bytes, finalUrl);
    }
}
