using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace RandomWebpage.Services;

public sealed record ClaudeModel(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

public sealed record ClaudeToolUse(string Id, string Name, JsonObject Input);

/// <summary>
/// A parsed Messages API response. <see cref="Content"/> is the raw block list, so it can be sent back
/// unchanged as the assistant turn when the model asked for tools.
/// </summary>
public sealed record ClaudeReply(
    string StopReason,
    string Text,
    IReadOnlyList<ClaudeToolUse> ToolUses,
    JsonArray Content)
{
    public bool WantsTools => StopReason == "tool_use" && ToolUses.Count > 0;
}

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
        var messages = new JsonNode[] { UserMessage(user) };
        var reply = await CreateMessageAsync(model, system, messages, tools: null, forbidTools: false, ct);
        return reply.Text.Length > 0
            ? reply.Text
            : throw new InvalidOperationException("Claude response contained no text block.");
    }

    /// <summary>
    /// Sends a conversation. When <paramref name="forbidTools"/> is set the tool definitions are still sent
    /// (the API needs them to accept earlier tool_use blocks) but the model may not call any.
    /// </summary>
    public async Task<ClaudeReply> CreateMessageAsync(
        string model,
        string system,
        IReadOnlyList<JsonNode> messages,
        JsonArray? tools,
        bool forbidTools,
        CancellationToken ct = default)
    {
        var request = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = options.Value.Claude.MaxTokens,
            ["system"] = system,
            ["messages"] = new JsonArray(messages.Select(m => m.DeepClone()).ToArray())
        };

        if (tools is { Count: > 0 })
        {
            request["tools"] = tools.DeepClone();
            if (forbidTools)
                request["tool_choice"] = new JsonObject { ["type"] = "none" };
        }

        using var response = await http.PostAsJsonAsync("v1/messages", request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Claude API returned {(int)response.StatusCode}: {body}");
        }

        var json = await response.Content.ReadFromJsonAsync<JsonNode>(ct);
        return ParseReply(json);
    }

    public static JsonObject UserMessage(string text) => new() { ["role"] = "user", ["content"] = text };

    public static ClaudeReply ParseReply(JsonNode? json)
    {
        if (json?["content"] is not JsonArray content)
            throw new InvalidOperationException("Claude response contained no content.");
        var stopReason = json["stop_reason"]?.GetValue<string>() ?? "";

        var text = new System.Text.StringBuilder();
        var toolUses = new List<ClaudeToolUse>();
        foreach (var block in content)
        {
            switch (block?["type"]?.GetValue<string>())
            {
                case "text":
                    text.Append(block["text"]?.GetValue<string>());
                    break;
                case "tool_use":
                    toolUses.Add(new ClaudeToolUse(
                        block["id"]?.GetValue<string>() ?? "",
                        block["name"]?.GetValue<string>() ?? "",
                        block["input"] is JsonObject input ? (JsonObject)input.DeepClone() : new JsonObject()));
                    break;
            }
        }

        return new ClaudeReply(stopReason, text.ToString(), toolUses, (JsonArray)content.DeepClone());
    }

    private sealed record ModelsResponse([property: JsonPropertyName("data")] List<ClaudeModel> Data);
}
