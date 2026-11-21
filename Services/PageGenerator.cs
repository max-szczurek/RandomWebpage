using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace RandomWebpage.Services;

public sealed record GeneratedPage(string Html, IReadOnlyList<Uri> ImageUrls);

public sealed partial class PageGenerator(
    ClaudeClient claude,
    ClaudeModelResolver models,
    WebsiteTools tools,
    IOptions<AppOptions> options,
    ILogger<PageGenerator> logger)
{
    private const string SystemPrompt = """
        You are a creative web designer. Output ONLY a complete, self-contained HTML5 document with inline CSS
        (no markdown, no code fences, no explanations, no JavaScript, no external stylesheets, fonts or scripts).
        Keep it SIMPLE and SHORT: one page, a header, two or three brief sections, and a footer.
        Use a small <style> block (about 30 rules at most), short copy, and aim for well under 150 lines total.

        You have tools for working with images. Use them ONLY when the website purpose asks for them:
        - get_random_image: when the purpose asks for a random image or photo.
        - get_image_from_url: when the purpose names a specific image URL to use.
        - sample_image_colors: when the purpose asks for colors taken from an image.
        If the purpose asks for none of these, call no tools and do not include any <img> element.

        Rules for the page:
        - Embed a downloaded image prominently with <img src="URL" alt="..."> using exactly the url a tool returned,
          never any other image URL. Size it with CSS (for example max-width:100%; height:auto) or the width and
          height the tool reported.
        - If the purpose gives its own colors or palette, use those as the site's color scheme.
        - If you sampled colors, use them as the color scheme; the suggested background, text and accent are a
          readable starting point.
        - If the purpose says nothing about colors, choose a palette that fits the purpose.
        """;

    public async Task<GeneratedPage> GenerateAsync(CancellationToken ct = default)
    {
        var model = await models.GetLatestHaikuAsync(ct);
        var definitions = WebsiteTools.Definitions();

        var user = new StringBuilder()
            .AppendLine("Website purpose:")
            .AppendLine(options.Value.WebsitePurpose)
            .AppendLine()
            .AppendLine("Design a fresh, simple layout with new content. Be creative but concise.")
            .ToString();

        var messages = new List<JsonNode> { ClaudeClient.UserMessage(user) };
        var maxRounds = Math.Max(0, options.Value.Claude.MaxToolRounds);

        for (var round = 0; ; round++)
        {
            var lastChance = round >= maxRounds;
            var reply = await claude.CreateMessageAsync(model, SystemPrompt, messages, definitions, forbidTools: lastChance, ct);

            if (!reply.WantsTools)
            {
                if (reply.StopReason == "max_tokens")
                    logger.LogWarning(
                        "Claude hit the {MaxTokens} token limit, so the page is cut off. Thinking tokens count toward this limit; raise Claude:MaxTokens.",
                        options.Value.Claude.MaxTokens);

                try
                {
                    return new GeneratedPage(Clean(reply.Text), tools.ImageUrls);
                }
                catch (InvalidOperationException)
                {
                    var preview = reply.Text.Length > 300 ? reply.Text[..300] : reply.Text;
                    logger.LogWarning("Claude stopped with {StopReason} and returned no HTML. Start of reply: {Preview}", reply.StopReason, preview);
                    throw;
                }
            }

            if (lastChance)
                throw new InvalidOperationException("Claude kept requesting tools after tool use was disabled.");

            logger.LogInformation("Round {Round}: Claude called {Tools}", round + 1, string.Join(", ", reply.ToolUses.Select(t => t.Name)));
            messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = reply.Content });

            var results = new JsonArray();
            foreach (var use in reply.ToolUses)
            {
                var result = await tools.ExecuteAsync(use.Name, use.Input, ct);
                results.Add(new JsonObject
                {
                    ["type"] = "tool_result",
                    ["tool_use_id"] = use.Id,
                    ["content"] = result.Content,
                    ["is_error"] = result.IsError
                });
            }

            if (round + 1 >= maxRounds)
                results.Add(new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = "No more tool calls are available. Write the final HTML document now."
                });

            messages.Add(new JsonObject { ["role"] = "user", ["content"] = results });
        }
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
