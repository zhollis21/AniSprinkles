using System.Text.Json.Nodes;
using AniSprinkles.Services.Fixtures;

namespace AniSprinkles.UnitTests;

/// <summary>
/// Keeping recorded airing times live (#134).
/// <para>
/// A recording is a snapshot of a moment. Replayed verbatim a week later every "airs in 3 hours" has
/// become "aired 6 days ago", and the countdown chips, the "eps behind" badge and the notification
/// window are only ever exercised in their past-tense state. Worse, it decays continuously — a
/// screenshot that was right in October is quietly wrong in November for reasons unrelated to any
/// change.
/// </para>
/// </summary>
public class AiringTimeRebaserTests
{
    private static readonly DateTimeOffset RecordedAt = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = RecordedAt.AddDays(5);

    [Fact]
    public void AiringAt_MovesForwardByTheAgeOfTheRecording()
    {
        // The property that matters is not the absolute value but the distance from "now": an episode
        // recorded three hours out should still be three hours out whenever it is replayed.
        var threeHoursAfterRecording = RecordedAt.AddHours(3).ToUnixTimeSeconds();
        var response = AiringResponse(threeHoursAfterRecording);

        AiringTimeRebaser.Rebase(response, RecordedAt, Now);

        var rebased = response["data"]!["Media"]!["nextAiringEpisode"]!["airingAt"]!.GetValue<long>();
        Assert.Equal(Now.AddHours(3).ToUnixTimeSeconds(), rebased);
    }

    [Fact]
    public void TimeUntilAiring_IsRecomputedRatherThanShifted()
    {
        // It is a duration from "now", not an instant. Adding the same offset the timestamp got would
        // leave it exactly as stale as doing nothing — a bug that looks correct in the diff.
        var response = AiringResponse(RecordedAt.AddHours(3).ToUnixTimeSeconds(), timeUntilAiring: 10_800);

        AiringTimeRebaser.Rebase(response, RecordedAt, Now);

        var remaining = response["data"]!["Media"]!["nextAiringEpisode"]!["timeUntilAiring"]!.GetValue<long>();
        Assert.Equal(3 * 60 * 60, remaining);
    }

    [Fact]
    public void AnEpisodeAlreadyPastAtRecordTime_StaysPast()
    {
        // Shifting is uniform, so history stays history. An episode that had already aired must not
        // be dragged into the future — "12 eps behind" is a state worth being able to screenshot.
        var response = AiringResponse(RecordedAt.AddHours(-10).ToUnixTimeSeconds());

        AiringTimeRebaser.Rebase(response, RecordedAt, Now);

        var rebased = response["data"]!["Media"]!["nextAiringEpisode"]!["airingAt"]!.GetValue<long>();
        Assert.True(rebased < Now.ToUnixTimeSeconds());
        Assert.Equal(Now.AddHours(-10).ToUnixTimeSeconds(), rebased);
    }

    [Fact]
    public void AFixtureRecordedInTheFuture_IsLeftAlone()
    {
        // Clock skew rather than a real case, but shifting backwards would silently corrupt a fixture
        // instead of leaving it recognisably odd.
        var original = RecordedAt.AddHours(3).ToUnixTimeSeconds();
        var response = AiringResponse(original);

        AiringTimeRebaser.Rebase(response, RecordedAt, RecordedAt.AddDays(-1));

        Assert.Equal(original, response["data"]!["Media"]!["nextAiringEpisode"]!["airingAt"]!.GetValue<long>());
    }

    [Fact]
    public void EveryAiringTimeInTheResponse_IsRebased()
    {
        // AiringSchedule returns a page of them, and the notification worker reads the whole page.
        // Rebasing only the first would leave the rest describing the wrong week.
        var response = new JsonObject
        {
            ["data"] = new JsonObject
            {
                ["Page"] = new JsonObject
                {
                    ["airingSchedules"] = new JsonArray(
                        new JsonObject { ["airingAt"] = RecordedAt.AddHours(1).ToUnixTimeSeconds() },
                        new JsonObject { ["airingAt"] = RecordedAt.AddHours(2).ToUnixTimeSeconds() }),
                },
            },
        };

        AiringTimeRebaser.Rebase(response, RecordedAt, Now);

        var schedules = response["data"]!["Page"]!["airingSchedules"]!.AsArray();
        Assert.Equal(Now.AddHours(1).ToUnixTimeSeconds(), schedules[0]!["airingAt"]!.GetValue<long>());
        Assert.Equal(Now.AddHours(2).ToUnixTimeSeconds(), schedules[1]!["airingAt"]!.GetValue<long>());
    }

    [Fact]
    public void ANodeWithoutTimeUntilAiring_DoesNotGainOne()
    {
        // Only ever answer the shape AniList sent. Inventing a field the query never asked for would
        // make the fixture describe a response that cannot occur.
        var response = AiringResponse(RecordedAt.AddHours(3).ToUnixTimeSeconds());

        AiringTimeRebaser.Rebase(response, RecordedAt, Now);

        Assert.Null(response["data"]!["Media"]!["nextAiringEpisode"]!["timeUntilAiring"]);
    }

    private static JsonNode AiringResponse(long airingAt, long? timeUntilAiring = null)
    {
        var episode = new JsonObject { ["episode"] = 5, ["airingAt"] = airingAt };
        if (timeUntilAiring is not null)
        {
            episode["timeUntilAiring"] = timeUntilAiring;
        }

        return new JsonObject
        {
            ["data"] = new JsonObject
            {
                ["Media"] = new JsonObject { ["id"] = 21, ["nextAiringEpisode"] = episode },
            },
        };
    }
}
