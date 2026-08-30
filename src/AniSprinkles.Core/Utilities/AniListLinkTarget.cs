namespace AniSprinkles.Utilities;

/// <summary>
/// Maps an <c>anilist.co</c> URL found in a character or staff bio onto the in-app route that shows
/// the same entity (#137). Most links in these bios point back at entities the app already has pages
/// for — across the 50 most-favourited characters and staff, 162 of 235 links were AniList
/// character/anime/staff/studio URLs — so following one in-app is almost always better than handing
/// it to the browser.
/// <para>
/// A <see langword="null"/> result means "not one of ours"; the caller opens those externally. This
/// is deliberately the whole decision, kept pure and off the Android side so it can be tested.
/// </para>
/// </summary>
public sealed record AniListLinkTarget
{
    private const string AniListHost = "anilist.co";
    private const string WwwPrefix = "www.";

    /// <summary>Shell route name, matching the registrations in <c>AppShell</c>.</summary>
    public required string Route { get; init; }

    /// <summary>Navigation parameter key the destination page model reads the id from.</summary>
    public required string ParameterName { get; init; }

    public required int Id { get; init; }

    public static AniListLinkTarget? Resolve(string? url)
    {
        if (!TryParseEntity(url, out var kind, out var id))
        {
            return null;
        }

        var (route, parameterName) = RouteFor(kind);
        if (route is null || parameterName is null)
        {
            return null;
        }

        return new AniListLinkTarget
        {
            Route = route,
            ParameterName = parameterName,
            Id = id,
        };
    }

    /// <summary>
    /// Parses an <c>anilist.co/{kind}/{id}</c> URL. <paramref name="kind"/> comes back lower-cased;
    /// whether the app can show it is the callers' business.
    /// </summary>
    private static bool TryParseEntity(string? url, out string kind, out int id)
    {
        kind = string.Empty;
        id = 0;

        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        // Uri.Host is already lower-cased. Compare the whole host rather than using EndsWith, so a
        // look-alike like anilist.co.example.com doesn't get treated as ours.
        var host = uri.Host;
        if (host.StartsWith(WwwPrefix, StringComparison.Ordinal))
        {
            host = host[WwwPrefix.Length..];
        }

        if (!string.Equals(host, AniListHost, StringComparison.Ordinal))
        {
            return false;
        }

        // AniList writes the entity name after the id (/character/725/Buggy-the-Clown), so take the
        // first two segments and ignore whatever follows.
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
        {
            return false;
        }

        if (!int.TryParse(segments[1], out id) || id <= 0)
        {
            id = 0;
            return false;
        }

        kind = segments[0].ToLowerInvariant();
        return true;
    }

    private static (string? Route, string? ParameterName) RouteFor(string kind)
        => kind switch
        {
            "character" => ("character-details", "characterId"),
            "staff" => ("staff-details", "staffId"),
            "anime" => ("media-details", "mediaId"),
            "manga" => ("media-details", "mediaId"),
            "studio" => ("studio-details", "studioId"),
            _ => (null, null),
        };
}
