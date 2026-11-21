# RandomWebpage
A .NET 10 web app that builds a brand-new website every time you load it.

**Live site:** [https://cassave.ai/](https://cassave.ai/)

On each visit the server asks the latest Claude Haiku model to write a page for your `WebsitePurpose` prompt. Claude can call tools while it works, but only when the purpose asks for them:
- `get_random_image` downloads a random 300x200 photo from [picsum.photos](https://picsum.photos).
- `get_image_from_url` downloads one specific image from an `https://` URL written in the purpose.
- `sample_image_colors` samples pixels from a downloaded image and returns its most common colors, plus a suggested background, text, and accent color.

Claude then writes the HTML, embedding any downloaded image and styling the page with the sampled colors, or with a palette you spell out in the purpose. A purpose that mentions no image and no colors makes no tool calls and gets a page with no photo.

Example purposes:
- `A museum of lost socks.` No tools, Claude picks its own look.
- `A museum of lost socks. Use a random photo and build the color scheme from its colors.` Random image, then color sampling.
- `Get https://example.com/sock.jpg, sample its colors, and build a fan site for it.` Image from that URL, then color sampling.
- `A museum of lost socks, using only #2B1B17, #F5E6CA and #C0392B.` No tools, your palette.

While the page is being generated, a loading spinner is shown.
## Requirements
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- An [Anthropic API key](https://console.anthropic.com/)
## Configuration
| Setting | Required | Description |
| --- | --- | --- |
| `ClaudeApiKey` | Yes | Your Anthropic API key. The app will not start without it. |
| `WebsitePurpose` | Yes | The prompt describing what kind of site to generate. A placeholder default is in `appsettings.json`. |
| `Claude:FallbackModel` | No | Model used if the latest Haiku model can't be looked up. Default `claude-haiku-4-5`. |
| `Claude:MaxTokens` | No | Maximum response length. Default `3000`. |
| `Claude:MaxToolRounds` | No | How many times Claude may call tools before it must write the page. Default `4`. |
| `Palette:SampleCount` | No | Number of colors `sample_image_colors` returns when Claude does not ask for a count. Default `8`. |
| `Images:MaxBytes` | No | Largest image `get_image_from_url` will download. Default `5000000`. |
| `Images:TimeoutSeconds` | No | Time limit for downloading one image from a URL. Default `10`. |
| `Images:MaxPerPage` | No | How many images one page may fetch. Default `3`. |
| `Traffic:PerIpPermitLimit` | No | Page generations allowed for one IP address in the window below. Default `20`. |
| `Traffic:PerIpWindowMinutes` | No | Length of that per-IP window, in minutes. Default `10`. |
| `Traffic:PerIpConcurrency` | No | How many generations one IP address may have in flight at once. Default `2`. |
| `Traffic:GlobalPermitLimit` | No | Page generations allowed for the whole site in the window below. Default `120`. |
| `Traffic:GlobalWindowMinutes` | No | Length of the site-wide window, in minutes. Default `60`. |
### User secrets (local development)
```powershell
cd RandomWebpage
dotnet user-secrets set ClaudeApiKey "sk-ant-..."
dotnet user-secrets set WebsitePurpose "A fan site for an imaginary guild of retired lighthouse keepers who sell bottled fog."
```
### Environment variables
Environment variables with the same names override user secrets and `appsettings.json`. Nested settings use a double underscore:
```powershell
$env:ClaudeApiKey = "sk-ant-..."
$env:WebsitePurpose = "A museum of lost socks."
$env:Claude__MaxTokens = "4000"
```
## Running
```powershell
dotnet run --project RandomWebpage
```
Open the URL shown in the console and refresh to get a new site each time.
## How it works
| Endpoint | Purpose |
| --- | --- |
| `GET /` | Loading page with a spinner. Its script calls `/generate` and swaps in the result. A known crawler gets a static page with no script. |
| `GET /generate` | Runs the full pipeline and returns the generated HTML. Refuses automated clients before any API call. |
| `GET /robots.txt` | Tells every crawler, including the major search and AI bots, not to fetch the site. |
Key files in `RandomWebpage/`:
- `Program.cs` sets up configuration, services, crawler blocking, rate limits, and the endpoints.
- `LoadingPage.cs` contains the spinner page.
- `Services/WebsiteTools.cs` defines the three tools, keeps the downloaded images on the server for one page (Claude only sees an image id, URL, and size), and records which image URLs were handed out.
- `Services/PicsumImageService.cs` downloads the random image and records its final URL, so the page shows the same image that was sampled.
- `Services/UrlImageService.cs` downloads an image from a URL in the purpose, with the safety checks described below.
- `Services/PaletteService.cs` samples about 200 random pixels with SkiaSharp, groups similar colors, returns the most common distinct ones as hex codes, and suggests background, text, and accent colors.
- `Services/ClaudeModelResolver.cs` looks up the newest Haiku model from the Anthropic models API and caches the result for 12 hours.
- `Services/ClaudeClient.cs` calls the Anthropic Messages API, including tool definitions and tool results.
- `Services/PageGenerator.cs` builds the prompt, runs the tool loop, and cleans up Claude's output.
- `GeneratedPagePolicy.cs` builds the Content-Security-Policy for each generated page.

Tests are in `RandomWebpage.Tests/`. Run them with `dotnet test`.
## Security notes
The generated HTML is served as-is, so a Content-Security-Policy restricts what it can do. Scripts are blocked, except the loading page's own script, which is allowed through a per-request nonce. Images can only come from the hosts of images the tools downloaded for that page: the policy is built per response, so a page that fetched no image can load none.

The `get_image_from_url` tool fetches a URL from the server, so it is restricted. Only `https://` URLs are accepted, and so are redirects to `https://`, at most 3 of them. The hostname is resolved by the app, and a host that resolves to a loopback, private, link-local (including cloud metadata addresses such as `169.254.169.254`), or other non-public address is refused before any connection is made. The response must be an `image/*` type no larger than `Images:MaxBytes`, and it must decode as an image. A refused URL is reported back to Claude as a tool error, so the page is still generated. Because the browser loads a downloaded image straight from its host, that host sees the visitor's request.
Automated clients are turned away before that work starts. `/robots.txt` disallows every crawler, and every response sends `X-Robots-Tag: noindex, nofollow, noarchive`. `/generate` also refuses a missing or crawler user agent, a prefetch, and a cross-site embed (an image, iframe, or fetch from another site). A link click from another site is served.
## Cost
Every accepted visit to `/generate` makes at least one Claude API call. A purpose with no image or color tools makes exactly one. One that uses tools makes about three (one to ask for the image, one to ask for its colors, one to write the page), and never more than `Claude:MaxToolRounds` + 1. The prompt asks for a short, simple page and responses are capped by `Claude:MaxTokens`.
Crawlers that run JavaScript, and anyone who loops on the public URL, would otherwise repeat that call without limit. Known crawlers never get that far. Everyone else shares the `Traffic` limits: by default 2 generations at a time and 20 every 10 minutes for each IP address, and 120 every hour for the whole site. The counters are kept in memory and reset when the app restarts. Raise the limits in `appsettings.json` or with `Traffic__*` environment variables if real visitors start hitting them. Responses are not cached, so each accepted visit is a new call.
