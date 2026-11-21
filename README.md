# RandomWebpage
A .NET 10 web app that builds a brand-new website every time you load it.
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
| `GET /` | Loading page with a spinner. Its script calls `/generate` and swaps in the result. |
| `GET /generate` | Runs the full pipeline and returns the generated HTML. |
Key files in `RandomWebpage/`:
- `Program.cs` sets up configuration and services and defines both endpoints.
- `LoadingPage.cs` contains the spinner page.
- `Services/PicsumImageService.cs` downloads the random image and records its final URL, so the page shows the same image that was sampled.
- `Services/PaletteService.cs` samples about 200 random pixels with SkiaSharp, groups similar colors, and returns the most common distinct ones as hex codes.
- `Services/ClaudeModelResolver.cs` looks up the newest Haiku model from the Anthropic models API and caches the result for 12 hours.
- `Services/ClaudeClient.cs` calls the Anthropic Messages API.
- `Services/PageGenerator.cs` builds the prompt and cleans up Claude's output.
## Security notes
The generated HTML is served as-is, so a Content-Security-Policy restricts what it can do. Scripts are blocked, except the loading page's own script, which is allowed through a per-request nonce. Images can only come from picsum.photos.
## Cost
Every page load makes one Claude API call. The prompt asks for a short, simple page and responses are capped by `Claude:MaxTokens`, but there is no caching or rate limiting, so keep that in mind before exposing the site publicly.
