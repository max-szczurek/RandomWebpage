using System.Security.Cryptography;

namespace RandomWebpage;

/// <summary>
/// Spinner page served at "/". Its script fetches "/generate" and replaces the document with the result.
/// The generated HTML is written into this same document, so this page's CSP (script only via nonce)
/// also prevents any script in the generated HTML from running.
/// </summary>
public static class LoadingPage
{
    public const string ImageSources = "'self' https://picsum.photos https://*.picsum.photos data:";

    public static IResult Render(HttpContext context)
    {
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

        context.Response.Headers.ContentSecurityPolicy =
            $"default-src 'none'; script-src 'nonce-{nonce}'; connect-src 'self'; img-src {ImageSources}; " +
            "style-src 'unsafe-inline'; font-src data:; base-uri 'none'; form-action 'none'";
        context.Response.Headers.CacheControl = "no-store";

        var html = $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>Generating a random website...</title>
              <link rel="icon" href="/favicon.ico">
              <style>
                html, body { height: 100%; margin: 0; }
                body { display: flex; flex-direction: column; align-items: center; justify-content: center;
                       gap: 1.25rem; font-family: system-ui, sans-serif; background: #111; color: #ddd; }
                .spinner { width: 56px; height: 56px; border-radius: 50%;
                           border: 6px solid #333; border-top-color: #7aa2f7; animation: spin 0.9s linear infinite; }
                @keyframes spin { to { transform: rotate(360deg); } }
                p { margin: 0; opacity: 0.8; }
              </style>
            </head>
            <body>
              <div class="spinner" role="status" aria-label="Loading"></div>
              <p>Dreaming up a random website...</p>
              <script nonce="{{nonce}}">
                fetch('/generate', { cache: 'no-store' })
                  .then(r => r.text())
                  .catch(e => '<!DOCTYPE html><html><body style="font-family:sans-serif;margin:3em">' +
                                '<h1>Could not reach the server</h1><p><a href="/">Try again</a></p></body></html>')
                  .then(html => { document.open(); document.write(html); document.close(); });
              </script>
            </body>
            </html>
            """;

        return Results.Content(html, "text/html; charset=utf-8");
    }
}
