using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace RandomWebpage.Services;

/// <summary>
/// Holds images downloaded from URLs so a purpose that names the same image on every visit does not fetch it
/// again. Entries are sized by their byte length against <c>Images:CacheMaxBytes</c>, so many distinct URLs
/// cannot grow memory without bound. A separate cache from the app-wide one, because a size limit applies to
/// every entry of the cache it is set on.
/// </summary>
public sealed class ImageCache(IOptions<AppOptions> options) : IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = options.Value.Images.CacheMaxBytes });

    private TimeSpan Lifetime => TimeSpan.FromMinutes(options.Value.Images.CacheMinutes);

    public bool Enabled => options.Value.Images.CacheMinutes > 0;

    public bool TryGet(string key, out FetchedImage image)
    {
        if (Enabled && _cache.TryGetValue(key, out FetchedImage? found) && found is not null)
        {
            image = found;
            return true;
        }

        image = null!;
        return false;
    }

    public void Set(string key, FetchedImage image)
    {
        if (!Enabled)
            return;

        _cache.Set(key, image, new MemoryCacheEntryOptions { Size = image.Bytes.Length, AbsoluteExpirationRelativeToNow = Lifetime });
    }

    public void Dispose() => _cache.Dispose();
}
