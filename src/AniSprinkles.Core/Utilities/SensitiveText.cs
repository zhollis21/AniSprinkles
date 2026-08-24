using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace AniSprinkles.Utilities;

/// <summary>
/// Strips credentials out of text on its way into somewhere that persists or transmits it.
/// <para>
/// The one shape that matters here is an OAuth bearer token echoed back inside an AniList error
/// body: <c>AniListClient</c> embeds up to 500 characters of the raw response into the exception
/// message, so a 4xx whose body reads <c>Invalid token: Bearer eyJ...</c> would otherwise carry the
/// credential into the rotating file log, logcat and Sentry.
/// </para>
/// <para>
/// Applied at the point the text is produced rather than at each sink (#124). Sink-by-sink
/// sanitising means remembering to redact at every future <c>LogError(ex, ...)</c> call site, and
/// redacting an exception before handing it to Sentry costs the stack trace that its grouping
/// depends on.
/// </para>
/// </summary>
public static class SensitiveText
{
    /// <summary>
    /// Matches the token in free text, not in a header — the credential arrives inside a JSON error
    /// body echoed back by AniList, so the surrounding characters are arbitrary.
    /// <para>
    /// The character class spans BOTH base64 alphabets on purpose: <c>A-Za-z0-9</c> plus <c>-</c>
    /// and <c>_</c> (base64url, which is what a JWT uses) and <c>+</c> and <c>/</c> (standard
    /// base64), with <c>.</c> for the JWT's segment separators and <c>~</c> completing the RFC 3986
    /// unreserved set. The trailing <c>=*</c> is outside the class and covers the padding either
    /// alphabet can end on.
    /// </para>
    /// <para>
    /// Do not narrow it to one alphabet. Over-matching here costs a few extra characters of an
    /// error message; under-matching leaks a credential into the log file and Sentry (#124).
    /// </para>
    /// </summary>
    private static readonly Regex BearerTokenRegex =
        new(@"Bearer\s+[A-Za-z0-9\-\._~\+\/]+=*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public const string RedactedBearer = "Bearer <redacted>";

    /// <summary>
    /// Returns <paramref name="value"/> with any bearer token replaced. Null in, null out, so call
    /// sites can redact an optional message without a null check of their own — and the
    /// <see cref="NotNullIfNotNullAttribute"/> means a non-null argument yields a non-null result to
    /// the compiler, so redacting a string that is already known to exist needs no suppression.
    /// </summary>
    [return: NotNullIfNotNull(nameof(value))]
    public static string? Redact(string? value)
        => value is null ? null : BearerTokenRegex.Replace(value, RedactedBearer);
}
