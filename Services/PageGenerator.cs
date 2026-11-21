using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace RandomWebpage.Services;

public sealed partial class PageGenerator(
    ClaudeClient claude,
    ClaudeModelResolver models,
    IOptions<AppOptions> options)
{
    public async Task<string> GenerateAsync(string imageUrl, IReadOnlyList<string> palette, CancellationToken ct = default)
    {
        var model = await models.GetLatestHaikuAsync(ct);

        var system = $"""
            You are a creative web designer. Output ONLY a complete, self-contained HTML5 document with inline CSS
            (no markdown, no code fences, no explanations, no JavaScript, no external stylesheets, fonts or scripts).
            You must include <img src="{imageUrl}" width="300" height="200" alt="..."> prominently on the page,
            using exactly that URL, and use the provided color palette as the site's color scheme.
            Keep it SIMPLE and SHORT: one page, a header, the image, two or three brief sections, and a footer.
            Use a small <style> block (about 30 rules at most), short copy, and aim for well under 150 lines total.
            """;

        var user = new StringBuilder()
            .AppendLine("Website purpose:")
            .AppendLine(options.Value.WebsitePurpose)
            .AppendLine()
            .AppendLine($"Image URL: {imageUrl}")
            .AppendLine("Color palette (sampled from the image):")
            .AppendLine(string.Join(", ", palette))
            .AppendLine()
            .AppendLine("Design a fresh, simple layout with new content. Be creative but concise.")
            .ToString();

        var raw = await claude.CreateMessageAsync(model, system, user, ct);
        return Clean(raw);
    }

    internal static string Clean(string raw)
    {
        var text = raw.Trim();

        var fence = CodeFenceRegex().Match(text);
        if (fence.Success)
            text = fence.Groups[1].Value.Trim();

        if (!text.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) &&
            !text.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
        {
            // Drop any chatty preamble before the document starts.
            var start = text.IndexOf("<!DOCTYPE", StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                start = text.IndexOf("<html", StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                throw new InvalidOperationException("Claude did not return an HTML document.");

            text = text[start..];
        }

        return text;
    }

    [GeneratedRegex(@"```(?:html)?\s*(.*?)```", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex CodeFenceRegex();
}
