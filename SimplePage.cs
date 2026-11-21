using System.Net;

namespace RandomWebpage;

/// <summary>Small HTML document for responses this app writes itself.</summary>
public static class SimplePage
{
    public static string Render(string title, string message, bool offerRetry = false)
    {
        var safeTitle = WebUtility.HtmlEncode(title);
        var safeMessage = WebUtility.HtmlEncode(message);
        var retry = offerRetry ? "\n              <p><a href=\"/\">Try again</a></p>" : "";

        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <meta name="robots" content="noindex, nofollow, noarchive">
              <title>{{safeTitle}}</title>
            </head>
            <body style="font-family:sans-serif;max-width:40em;margin:3em auto;line-height:1.45">
              <h1>{{safeTitle}}</h1>
              <p>{{safeMessage}}</p>{{retry}}
            </body>
            </html>
            """;
    }
}
