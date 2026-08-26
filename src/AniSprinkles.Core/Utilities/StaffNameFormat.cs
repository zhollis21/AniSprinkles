namespace AniSprinkles.Utilities;

/// <summary>
/// Renders a person's name the way AniList says the viewer wants to see it (#130).
/// <para>
/// The Staff Name Language setting was saved to AniList and read by nothing: both name accessors
/// preferred <c>full</c> unconditionally, so every staff and character name rendered identically
/// whatever the viewer had picked. This defers the whole question to <c>userPreferred</c>, which
/// AniList resolves server-side against that setting.
/// </para>
/// <para>
/// <b>Why not compose the name locally from <c>first</c>/<c>last</c>.</b> That was tried, and it is
/// where the interesting finding came from — AniList is not internally consistent. Measured on a real
/// account with the setting on <c>ROMAJI</c>:
/// </para>
/// <code>
/// Kawakami Taiki  full="Kawakami Taiki"   userPreferred="Kawakami Taiki"   native=川上泰樹
/// Caitlyn Bairstow full="Bairstow Caitlyn" userPreferred="Caitlyn Bairstow" native=null
/// </code>
/// <para>
/// So <c>full</c> flips a Latin-script name under ROMAJI while <c>userPreferred</c> leaves it alone:
/// <c>userPreferred</c> is script-aware and <c>full</c> is not. Reproducing that locally would mean
/// inventing a CJK heuristic and hoping it keeps matching theirs. Deferring means the app and the
/// website can never disagree about the same person.
/// </para>
/// <para>
/// The cost is that <c>userPreferred</c> is fixed at fetch time, so changing the setting needs a
/// refetch rather than a re-projection — which is why
/// <see cref="AniSprinkles.Services.IAniListClient.InvalidateEntityCache"/> exists and why this does
/// <em>not</em> participate in <c>DisplaySettingsSnapshot</c>. A deliberate trade: the setting is
/// changed rarely, and correctness against AniList is worth more than saving a request on an
/// uncommon action.
/// </para>
/// <para>
/// <c>full</c> remains the fallback for the handful of selections where a cached or partial name
/// arrives without <c>userPreferred</c>.
/// </para>
/// </summary>
public static class StaffNameFormat
{
    public static string Display(CharacterName? name)
        => FirstNonBlank(name?.UserPreferred)
        ?? FirstNonBlank(name?.Full)
        ?? FirstNonBlank(name?.Native)
        ?? "Unknown";

    private static string? FirstNonBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
