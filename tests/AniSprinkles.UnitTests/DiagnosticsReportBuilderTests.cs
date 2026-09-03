namespace AniSprinkles.UnitTests;

/// <summary>
/// The text a user sends when they report a problem (#112).
/// <para>
/// The redaction section below is the most load-bearing part of this feature. <c>SentryScrubber</c>
/// runs on <c>SetBeforeSend</c> over the <c>SentryEvent</c> and does <b>not</b> walk attachments —
/// so this builder is the only thing between the log and Sentry. A token that survives here leaves
/// the device. That is also why the builder is pure: so it can be asserted on directly rather than
/// inferred from a send that cannot be observed.
/// </para>
/// </summary>
public class DiagnosticsReportBuilderTests
{
    private static readonly DateTimeOffset CapturedAt = new(2026, 9, 2, 12, 34, 56, TimeSpan.Zero);

    private static readonly DiagnosticsContext Context = new(
        AppVersion: "1.2.3 (45)",
        BuildConfiguration: "Release",
        Device: "Google Pixel 7",
        OsVersion: "Android 15",
        IsSignedIn: true);

    private static string Build(
        IReadOnlyList<string>? previous = null,
        IReadOnlyList<string>? current = null,
        string? description = null,
        DiagnosticsContext? context = null)
        => DiagnosticsReportBuilder.Build(
            context ?? Context,
            CapturedAt,
            previous ?? [],
            current ?? ["12:00 [Information] Nav NAVTRACE load start (manga 42)"],
            description);

    // ── Header ───────────────────────────────────────────────────────

    [Fact]
    public void TheHeaderCarriesEveryEnvironmentFactAReaderNeeds()
    {
        var report = Build();

        Assert.Contains("1.2.3 (45)", report);
        Assert.Contains("Release", report);
        Assert.Contains("Google Pixel 7", report);
        Assert.Contains("Android 15", report);
        Assert.Contains(CapturedAt.ToString("O"), report);
    }

    [Fact]
    public void TheBuildConfigurationIsStatedRatherThanLeftToBeInferred()
    {
        // This repo genuinely diverges between Debug and Release — log levels, CI stubs, fault
        // injection — so a report that does not say which one it came from is missing a fact the
        // reader would otherwise guess wrong.
        Assert.Contains("Debug", Build(context: Context with { BuildConfiguration = "Debug" }));
    }

    [Theory]
    [InlineData(true, "yes")]
    [InlineData(false, "no")]
    public void SignedInStateIsStatedOutright(bool signedIn, string expected)
    {
        var report = Build(context: Context with { IsSignedIn = signedIn });

        Assert.Contains($"signed in: {expected}", report);
    }

    [Fact]
    public void TheHeaderCarriesNoSentryEventCrossReference()
    {
        // An earlier draft printed a "related event:" id linking the report to the automatic
        // exception capture. On device it was always empty — neither ErrorReportService's
        // CaptureException return value nor SentrySdk.LastEventId holds the id by report time — so
        // the field was removed rather than shipped reliably blank. The exception text and the
        // timestamps below are what correlate the two, and both are already in full.
        Assert.DoesNotContain("related event", Build(), StringComparison.OrdinalIgnoreCase);
    }

    // ── The user's own words ─────────────────────────────────────────

    [Fact]
    public void TheDescriptionIsIncludedUnderItsOwnHeader()
    {
        var report = Build(description: "I kept tapping manga and they all failed");

        Assert.Contains(DiagnosticsReportBuilder.DescriptionHeader, report);
        Assert.Contains("I kept tapping manga and they all failed", report);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TheDescriptionSectionIsOmittedWhenNothingWasTyped(string? description)
    {
        // The box is optional. An empty section would imply the user declined to explain, when in
        // fact they may simply have wanted the log.
        Assert.DoesNotContain(DiagnosticsReportBuilder.DescriptionHeader, Build(description: description));
    }

    // ── Sessions ─────────────────────────────────────────────────────

    [Fact]
    public void ThePreviousSessionIsIncludedUnderItsOwnHeaderWhenOneWasFlushed()
    {
        var report = Build(previous: ["crashed here"], current: ["and now this"]);

        Assert.Contains(DiagnosticsSessionLog.PreviousSessionHeader, report);
        Assert.Contains("crashed here", report);
        Assert.Contains("and now this", report);
    }

    [Fact]
    public void ThePreviousSessionSectionIsOmittedWhenThereIsNone()
    {
        Assert.DoesNotContain(DiagnosticsSessionLog.PreviousSessionHeader, Build());
    }

    [Fact]
    public void ThePreviousSessionComesBeforeTheCurrentOne()
    {
        // Chronological order, so the report reads as one narrative rather than two piles.
        var report = Build(previous: ["older"], current: ["newer"]);

        Assert.True(report.IndexOf("older", StringComparison.Ordinal) < report.IndexOf("newer", StringComparison.Ordinal));
    }

    [Fact]
    public void AnEmptyRingSaysSoRatherThanLookingTruncated()
    {
        // A report ending abruptly after the header is indistinguishable from one that was cut off.
        // Saying "nothing was recorded" is a fact; silence is a puzzle.
        var report = Build(current: []);

        Assert.Contains(DiagnosticsReportBuilder.NoActivityPlaceholder, report);
    }

    [Fact]
    public void EveryLogLineSurvivesIntact()
    {
        var lines = Enumerable.Range(0, 50).Select(i => $"line {i}").ToArray();

        var report = Build(current: lines);

        Assert.All(lines, line => Assert.Contains(line, report));
    }

    // ── Redaction: the part that must not regress ────────────────────

    [Fact]
    public void ABearerTokenInALogLineIsRedacted()
    {
        // The real shape: AniListClient embeds up to 500 characters of a raw error body into an
        // exception message, and an auth-failure body can echo the credential back.
        var report = Build(current: ["[Error] Api call failed: {\"errors\":[{\"message\":\"Invalid token: Bearer eyJhbGciOiJIUzI1NiJ9.abc-_123\"}]}"]);

        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiJ9", report);
        Assert.Contains(SensitiveText.RedactedBearer, report);
    }

    [Fact]
    public void ABearerTokenInThePreviousSessionIsRedactedToo()
    {
        // The flushed session takes a different route into the report — off disk rather than out of
        // memory — and an earlier design that redacted only the live ring would have missed it.
        var report = Build(previous: ["Authorization: Bearer eyJsecrettoken"], current: ["fine"]);

        Assert.DoesNotContain("eyJsecrettoken", report);
    }

    [Fact]
    public void ABearerTokenPastedIntoTheDescriptionIsRedacted()
    {
        // Free text the user controls. They can paste anything into it, including something they
        // copied out of an error message.
        var report = Build(description: "it said Bearer eyJpastedbyhand and then failed");

        Assert.DoesNotContain("eyJpastedbyhand", report);
    }

    [Fact]
    public void EveryTokenIsRedactedNotJustTheFirst()
    {
        // A retry storm writes the same failing request several times over. Redacting only the first
        // occurrence would leak every one after it.
        var report = Build(current:
        [
            "attempt 1 Bearer eyJfirsttoken failed",
            "attempt 2 Bearer eyJsecondtoken failed",
            "attempt 3 Bearer eyJthirdtoken failed",
        ]);

        Assert.DoesNotContain("eyJfirsttoken", report);
        Assert.DoesNotContain("eyJsecondtoken", report);
        Assert.DoesNotContain("eyJthirdtoken", report);
        Assert.Equal(3, report.Split(SensitiveText.RedactedBearer).Length - 1);
    }

    [Fact]
    public void ATokenWrappedAcrossALineBreakIsStillRedacted()
    {
        // Records are not strictly one line each — an appended stack trace keeps its breaks — so the
        // redaction must not depend on line structure. It doesn't: the pass runs over the whole
        // assembled string, and `Bearer\s+…` matches `\s` including a newline.
        var report = Build(current: ["exception text ending in Bearer\n  eyJwrappedacrossabreak and more"]);

        Assert.DoesNotContain("eyJwrappedacrossabreak", report);
    }

    [Fact]
    public void RedactionRunsOverTheWholeReportIncludingSectionsAddedLater()
    {
        // A single pass over the finished text rather than per-section, so a section someone adds
        // later cannot quietly skip it. Placing a token in every section at once is the assertion
        // that the pass is global.
        var report = Build(
            previous: ["Bearer eyJinprevious"],
            current: ["Bearer eyJincurrent"],
            description: "Bearer eyJindescription");

        Assert.DoesNotContain("eyJinprevious", report);
        Assert.DoesNotContain("eyJincurrent", report);
        Assert.DoesNotContain("eyJindescription", report);
    }

    // ── Format ───────────────────────────────────────────────────────

    [Fact]
    public void TheReportIsPlainTextWithNoTrailingSurprises()
    {
        var report = Build();

        Assert.False(string.IsNullOrWhiteSpace(report));
        Assert.StartsWith("AniSprinkles diagnostic report", report, StringComparison.Ordinal);
    }
}
