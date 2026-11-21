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
}

public sealed class ClaudeOptions
{
    public string FallbackModel { get; set; } = "claude-haiku-4-5";
    public int MaxTokens { get; set; } = 3000;
}

public sealed class PaletteOptions
{
    public int SampleCount { get; set; } = 8;
}
