using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace RandomWebpage.Services;

public sealed class ClaudeModelResolver(
    ClaudeClient client,
    IMemoryCache cache,
    IOptions<AppOptions> options,
    ILogger<ClaudeModelResolver> logger)
{
    private const string CacheKey = "claude-latest-haiku";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(12);

    /// <summary>Returns the newest Haiku model id, cached for 12 hours; falls back to configuration on failure.</summary>
    public async Task<string> GetLatestHaikuAsync(CancellationToken ct = default)
    {
        if (cache.TryGetValue(CacheKey, out string? cached) && cached is not null)
            return cached;

        try
        {
            var models = await client.ListModelsAsync(ct);
            var latest = models
                .Where(m => m.Id.Contains("haiku", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(m => m.CreatedAt)
                .FirstOrDefault();

            if (latest is not null)
            {
                logger.LogInformation("Using latest Haiku model {Model}", latest.Id);
                cache.Set(CacheKey, latest.Id, CacheDuration);
                return latest.Id;
            }

            logger.LogWarning("No Haiku models returned by the API; using fallback model.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to list Claude models; using fallback model.");
        }

        return options.Value.Claude.FallbackModel;
    }
}
