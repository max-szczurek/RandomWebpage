using System.Text.RegularExpressions;

namespace RandomWebpage;

/// <summary>Content-Security-Policy for the generated page, which is served as-is.</summary>
public static partial class GeneratedPagePolicy
{
    public static string Build(IEnumerable<Uri> imageUrls)
    {
        var origins = imageUrls
            .Select(u => $"{u.Scheme}://{u.Authority}")
            .Where(o => SafeOriginRegex().IsMatch(o))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var imgSrc = string.Join(' ', origins.Prepend("data:"));
        return $"default-src 'none'; script-src 'none'; img-src {imgSrc}; " +
               "style-src 'unsafe-inline'; font-src data:; base-uri 'none'; form-action 'none'";
    }

    // Only plain https origins go into the header, so a crafted URL cannot add directives.
    [GeneratedRegex(@"^https://[A-Za-z0-9.-]+(:\d{1,5})?$")]
    private static partial Regex SafeOriginRegex();
}
