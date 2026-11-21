# RandomWebpage
A .NET 10 web app that builds a brand-new website every time you load it.

**Live site:** [https://cassave.ai/](https://cassave.ai/)

On each visit the server:
1. Fetches a random 300x200 photo from [picsum.photos](https://picsum.photos).
2. Samples random pixels from it to build a color palette.
3. Sends the palette, the image URL, and your `WebsitePurpose` prompt to the latest Claude Haiku model.
4. Serves the HTML that Claude writes, with the photo embedded and styled in the photo's colors.
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
| `Palette:SampleCount` | No | Number of colors in the palette. Default `8`. |
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
- `Services/PicsumImageService.cs` downloads the random image and records its final URL, so the page shows the same image that was sampled.
- `Services/PaletteService.cs` samples about 200 random pixels with SkiaSharp, groups similar colors, and returns the most common distinct ones as hex codes.
- `Services/ClaudeModelResolver.cs` looks up the newest Haiku model from the Anthropic models API and caches the result for 12 hours.
- `Services/ClaudeClient.cs` calls the Anthropic Messages API.
- `Services/PageGenerator.cs` builds the prompt and cleans up Claude's output.
## Security notes
The generated HTML is served as-is, so a Content-Security-Policy restricts what it can do. Scripts are blocked, except the loading page's own script, which is allowed through a per-request nonce. Images can only come from picsum.photos.
Automated clients are turned away before that work starts. `/robots.txt` disallows every crawler, and every response sends `X-Robots-Tag: noindex, nofollow, noarchive`. `/generate` also refuses a missing or crawler user agent, a prefetch, and a cross-site embed (an image, iframe, or fetch from another site). A link click from another site is served.
## Cost
Every accepted visit to `/generate` makes one Claude API call. The prompt asks for a short, simple page and responses are capped by `Claude:MaxTokens`.
Crawlers that run JavaScript, and anyone who loops on the public URL, would otherwise repeat that call without limit. Known crawlers never get that far. Everyone else shares the `Traffic` limits: by default 2 generations at a time and 20 every 10 minutes for each IP address, and 120 every hour for the whole site. The counters are kept in memory and reset when the app restarts. Raise the limits in `appsettings.json` or with `Traffic__*` environment variables if real visitors start hitting them. Responses are not cached, so each accepted visit is a new call.
