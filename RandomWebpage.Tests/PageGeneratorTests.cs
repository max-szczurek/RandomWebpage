using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using RandomWebpage.Services;

namespace RandomWebpage.Tests;

public class PageGeneratorTests
{
    private const string Html = "<!DOCTYPE html><html><body>Hi</body></html>";

    private static string Text(string text) =>
        new JsonObject { ["stop_reason"] = "end_turn", ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }) }
            .ToJsonString();

    private static string ToolUse(string id, string name, string inputJson) =>
        $$"""{"stop_reason":"tool_use","content":[{"type":"tool_use","id":"{{id}}","name":"{{name}}","input":{{inputJson}}}]}""";

    private static (PageGenerator Generator, List<JsonNode> Requests) Create(
        Queue<string> replies, Action<AppOptions>? configure = null, WebsiteTools? tools = null)
    {
        var options = TestSupport.Options(configure);
        var requests = new List<JsonNode>();
        var handler = new StubHandler(r =>
        {
            if (r.RequestUri!.AbsolutePath.EndsWith("v1/models"))
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);

            requests.Add(JsonNode.Parse(r.Content!.ReadAsStringAsync().Result)!);
            return TestSupport.Json(replies.Dequeue());
        });

        var claude = new ClaudeClient(TestSupport.Client(handler, "https://api.test/"), options);
        var models = new ClaudeModelResolver(claude, new MemoryCache(new MemoryCacheOptions()), options, NullLogger<ClaudeModelResolver>.Instance);
        var generator = new PageGenerator(claude, models, tools ?? WebsiteToolsTests.Create(configure), options, NullLogger<PageGenerator>.Instance);
        return (generator, requests);
    }

    [Fact]
    public async Task A_purpose_without_tool_use_makes_one_call_and_no_images()
    {
        var (generator, requests) = Create(new([Text(Html)]));

        var page = await generator.GenerateAsync();

        Assert.Equal(Html, page.Html);
        Assert.Empty(page.ImageUrls);
        Assert.Single(requests);
        Assert.Contains("A test site.", requests[0]["messages"]![0]!["content"]!.GetValue<string>());
        Assert.Equal(3, requests[0]["tools"]!.AsArray().Count);
        Assert.Null(requests[0]["tool_choice"]);
    }

    [Fact]
    public async Task Runs_requested_tools_and_returns_the_results_to_claude()
    {
        var tools = WebsiteToolsTests.Create();
        var (generator, requests) = Create(new(
        [
            ToolUse("t1", "get_image_from_url", """{"url":"https://example.com/cake.png"}"""),
            ToolUse("t2", "sample_image_colors", """{"image_id":"img1"}"""),
            Text(Html)
        ]), tools: tools);

        var page = await generator.GenerateAsync();

        Assert.Equal([new Uri("https://example.com/cake.png")], page.ImageUrls);
        Assert.Equal(3, requests.Count);

        var secondMessages = requests[1]["messages"]!.AsArray();
        Assert.Equal(["user", "assistant", "user"], secondMessages.Select(m => m!["role"]!.GetValue<string>()));
        var result = secondMessages[2]!["content"]![0]!;
        Assert.Equal("tool_result", result["type"]!.GetValue<string>());
        Assert.Equal("t1", result["tool_use_id"]!.GetValue<string>());
        Assert.False(result["is_error"]!.GetValue<bool>());
        Assert.Contains("img1", result["content"]!.GetValue<string>());

        var thirdResult = requests[2]["messages"]!.AsArray()[4]!["content"]![0]!;
        Assert.Contains("colors", thirdResult["content"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_failing_tool_is_reported_to_claude_as_an_error_result()
    {
        var (generator, requests) = Create(new(
        [
            ToolUse("t1", "get_image_from_url", """{"url":"http://insecure.example/a.png"}"""),
            Text(Html)
        ]));

        var page = await generator.GenerateAsync();

        Assert.Equal(Html, page.Html);
        var result = requests[1]["messages"]!.AsArray()[2]!["content"]![0]!;
        Assert.True(result["is_error"]!.GetValue<bool>());
        Assert.Contains("https", result["content"]!.GetValue<string>());
    }

    [Fact]
    public async Task Disables_tools_on_the_final_round()
    {
        var (generator, requests) = Create(new(
        [
            ToolUse("t1", "get_random_image", "{}"),
            Text(Html)
        ]), o => o.Claude.MaxToolRounds = 1);

        await generator.GenerateAsync();

        Assert.Null(requests[0]["tool_choice"]);
        Assert.Equal("none", requests[1]["tool_choice"]!["type"]!.GetValue<string>());
        var lastContent = requests[1]["messages"]!.AsArray()[2]!["content"]!.AsArray();
        Assert.Equal("text", lastContent[^1]!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task Fails_if_claude_ignores_the_tool_ban()
    {
        var (generator, _) = Create(new([ToolUse("t1", "get_random_image", "{}"), ToolUse("t2", "get_random_image", "{}")]),
            o => o.Claude.MaxToolRounds = 1);

        await Assert.ThrowsAsync<InvalidOperationException>(() => generator.GenerateAsync());
    }
}
