namespace RandomWebpage;

/// <summary>
/// Bound from the root of configuration. ClaudeApiKey and WebsitePurpose come from
/// user secrets (Development) or environment variables; the rest has defaults in appsettings.json.
/// </summary>
public sealed class AppOptions
{
    public string ClaudeApiKey { get; set; } = "";
    public string WebsitePurpose { get; set; } = "";
    public ClaudeOptions Claude { get; set; } = new();
    public PaletteOptions Palette { get; set; } = new();
    public ImageOptions Images { get; set; } = new();
    public TrafficOptions Traffic { get; set; } = new();
}

public sealed class ClaudeOptions
{
    public string FallbackModel { get; set; } = "claude-haiku-4-5";
    public int MaxTokens { get; set; } = 8000;
    public int MaxToolRounds { get; set; } = 4;
}

public sealed class PaletteOptions
{
    public int SampleCount { get; set; } = 8;
}

public sealed class ImageOptions
{
    public int MaxBytes { get; set; } = 5_000_000;
    public int TimeoutSeconds { get; set; } = 10;
    public int MaxPerPage { get; set; } = 3;
    public int CacheMinutes { get; set; } = 60;
    public long CacheMaxBytes { get; set; } = 50_000_000;
}

public sealed class TrafficOptions
{
    public int PerIpPermitLimit { get; set; } = 20;
    public int PerIpWindowMinutes { get; set; } = 10;
    public int PerIpConcurrency { get; set; } = 2;
    public int GlobalPermitLimit { get; set; } = 120;
    public int GlobalWindowMinutes { get; set; } = 60;
}
