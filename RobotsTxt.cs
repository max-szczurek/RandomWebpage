namespace RandomWebpage;

/// <summary>
/// Asks every crawler to stay out. Search and AI crawlers that run JavaScript would
/// otherwise load "/" and then call "/generate", which spends a Claude request.
/// </summary>
public static class RobotsTxt
{
    public const string Body = """
        User-agent: *
        Disallow: /

        User-agent: GPTBot
        Disallow: /

        User-agent: ChatGPT-User
        Disallow: /

        User-agent: OAI-SearchBot
        Disallow: /

        User-agent: Googlebot
        Disallow: /

        User-agent: Google-Extended
        Disallow: /

        User-agent: GoogleOther
        Disallow: /

        User-agent: Bingbot
        Disallow: /

        User-agent: ClaudeBot
        Disallow: /

        User-agent: Claude-User
        Disallow: /

        User-agent: anthropic-ai
        Disallow: /

        User-agent: PerplexityBot
        Disallow: /

        User-agent: Perplexity-User
        Disallow: /

        User-agent: Amazonbot
        Disallow: /

        User-agent: CCBot
        Disallow: /

        User-agent: Bytespider
        Disallow: /

        User-agent: meta-externalagent
        Disallow: /

        User-agent: FacebookBot
        Disallow: /

        User-agent: Applebot
        Disallow: /

        User-agent: Applebot-Extended
        Disallow: /

        User-agent: cohere-ai
        Disallow: /

        """;
}
