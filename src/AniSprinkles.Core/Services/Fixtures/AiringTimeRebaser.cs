#if DEBUG
using System.Text.Json.Nodes;

namespace AniSprinkles.Services.Fixtures;

/// <summary>
/// Shifts recorded airing times forward so countdowns stay live (#134).
/// <para>
/// A recording is a snapshot of a moment. Served back verbatim a week later, every "airs in 3 hours"
/// has become "aired 6 days ago" and the whole airing surface — the countdown chips, the "eps behind"
/// badge, the notification worker's window — is exercised only in its past-tense state. Worse, it
/// degrades continuously, so a screenshot that was right in October is subtly wrong in November for
/// reasons unrelated to any change.
/// </para>
/// <para>
/// Shifting by the age of the recording keeps each episode the same distance from "now" as it was
/// when captured, which is what makes the fixture describe a *situation* rather than a timestamp.
/// The hand-written stub it replaces did the same thing with <c>UtcNow.AddHours(3)</c>; this is that
/// idea applied to real data.
/// </para>
/// </summary>
public static class AiringTimeRebaser
{
    /// <param name="now">
    /// Injectable so a test can pin it. Without that the only assertion available is "roughly the
    /// right shift, within a tolerance", which passes just as happily against an off-by-one-day bug.
    /// </param>
    public static void Rebase(JsonNode? response, DateTimeOffset recordedAt, DateTimeOffset? now = null)
    {
        var effectiveNow = now ?? DateTimeOffset.UtcNow;
        var shift = (long)(effectiveNow - recordedAt).TotalSeconds;
        if (shift <= 0)
        {
            return;
        }

        Walk(response, shift, effectiveNow.ToUnixTimeSeconds());
    }

    private static void Walk(JsonNode? node, long shift, long nowUnix)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var property in obj.ToList())
                {
                    if (property.Key is "airingAt"
                        && property.Value is JsonValue airing
                        && airing.TryGetValue<long>(out var airingAt))
                    {
                        var shifted = airingAt + shift;
                        obj["airingAt"] = shifted;

                        // Recomputed rather than shifted: it is a duration from "now", not an
                        // instant, so adding the same offset would leave it exactly as stale as
                        // doing nothing. AniList sends both and the app reads both.
                        if (obj.ContainsKey("timeUntilAiring"))
                        {
                            obj["timeUntilAiring"] = shifted - nowUnix;
                        }

                        continue;
                    }

                    Walk(property.Value, shift, nowUnix);
                }

                break;

            case JsonArray array:
                foreach (var item in array)
                {
                    Walk(item, shift, nowUnix);
                }

                break;
        }
    }
}
#endif
