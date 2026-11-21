using RandomWebpage.Services;

namespace RandomWebpage.Tests;

public class PageGeneratorCleanTests
{
    [Fact]
    public void Strips_code_fences() =>
        Assert.Equal("<!DOCTYPE html><html></html>", PageGenerator.Clean("```html\n<!DOCTYPE html><html></html>\n```"));

    [Fact]
    public void Drops_a_chatty_preamble() =>
        Assert.Equal("<html></html>", PageGenerator.Clean("Sure! Here you go:\n<html></html>"));

    [Fact]
    public void Throws_when_there_is_no_document() =>
        Assert.Throws<InvalidOperationException>(() => PageGenerator.Clean("I cannot do that."));
}

public class GeneratedPagePolicyTests
{
    [Fact]
    public void Allows_no_remote_images_by_default() =>
        Assert.Contains("img-src data:;", GeneratedPagePolicy.Build([]));

    [Fact]
    public void Allows_only_the_origin_of_each_image()
    {
        var policy = GeneratedPagePolicy.Build(
        [
            new Uri("https://fastly.picsum.photos/id/1/300/200.jpg?hmac=abc"),
            new Uri("https://example.com:8443/a/b.png"),
            new Uri("https://example.com:8443/c.png")
        ]);

        Assert.Contains("img-src data: https://fastly.picsum.photos https://example.com:8443;", policy);
        Assert.Contains("script-src 'none'", policy);
    }

    [Fact]
    public void Ignores_origins_that_could_inject_directives()
    {
        var policy = GeneratedPagePolicy.Build([new Uri("https://my_host.example/a.png")]);

        Assert.DoesNotContain("my_host", policy);
    }
}

public class ClaudeReplyTests
{
    [Fact]
    public void Parses_tool_use_blocks()
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse("""
            {"stop_reason":"tool_use","content":[
              {"type":"text","text":"Let me look."},
              {"type":"tool_use","id":"toolu_1","name":"get_image_from_url","input":{"url":"https://example.com/a.png"}}]}
            """);

        var reply = ClaudeClient.ParseReply(json);

        Assert.True(reply.WantsTools);
        Assert.Equal("Let me look.", reply.Text);
        var use = Assert.Single(reply.ToolUses);
        Assert.Equal(("toolu_1", "get_image_from_url"), (use.Id, use.Name));
        Assert.Equal("https://example.com/a.png", use.Input["url"]!.GetValue<string>());
        Assert.Equal(2, reply.Content.Count);
    }

    [Fact]
    public void A_plain_text_reply_does_not_want_tools()
    {
        var reply = ClaudeClient.ParseReply(System.Text.Json.Nodes.JsonNode.Parse(
            """{"stop_reason":"end_turn","content":[{"type":"text","text":"<html></html>"}]}"""));

        Assert.False(reply.WantsTools);
        Assert.Equal("<html></html>", reply.Text);
    }
}
