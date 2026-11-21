using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace RandomWebpage.Services;

public sealed record ClaudeModel(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

/// <summary>Typed HttpClient for the Anthropic API. BaseAddress and default headers are set in Program.cs.</summary>
public sealed class ClaudeClient(HttpClient http, IOptions<AppOptions> options)
{
    public async Task<IReadOnlyList<ClaudeModel>> ListModelsAsync(CancellationToken ct = default)
    {
        var result = await http.GetFromJsonAsync<ModelsResponse>("v1/models?limit=1000", ct);
        return result?.Data ?? [];
    }

    public async Task<string> CreateMessageAsync(string model, string system, string user, CancellationToken ct = default)
    {
        var request = new
        {
            model,
            max_tokens = options.Value.Claude.MaxTokens,
            system,
            messages = new[] { new { role = "user", content = user } }
        };

        using var response = await http.PostAsJsonAsync("v1/messages", request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Claude API returned {(int)response.StatusCode}: {body}");
        }

        var message = await response.Content.ReadFromJsonAsync<MessageResponse>(ct);
        var text = message?.Content?.FirstOrDefault(c => c.Type == "text")?.Text;
        return text ?? throw new InvalidOperationException("Claude response contained no text block.");
    }

    private sealed record ModelsResponse([property: JsonPropertyName("data")] List<ClaudeModel> Data);

    private sealed record MessageResponse([property: JsonPropertyName("content")] List<ContentBlock>? Content);

    private sealed record ContentBlock(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text")] string? Text);
}
