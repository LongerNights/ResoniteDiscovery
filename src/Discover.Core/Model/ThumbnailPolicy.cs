namespace Discover.Core.Model;

/// <summary>
/// Thumbnail links come from other players' sessions, so only schemes the engine serves its own assets with are accepted.
/// Anything else (file:, UNC shares, http:, ftp:) falls back to the "no thumbnail" image instead of being fetched.
/// </summary>
public static class ThumbnailPolicy
{
    private static readonly string[] AllowedSchemes = ["resdb", "https", "local"];

    public static bool TryAccept(string? raw, out Uri uri)
    {
        if (Uri.TryCreate(raw, UriKind.Absolute, out var parsed)
            && AllowedSchemes.Contains(parsed.Scheme, StringComparer.OrdinalIgnoreCase))
        {
            uri = parsed;
            return true;
        }

        uri = null!;
        return false;
    }
}
