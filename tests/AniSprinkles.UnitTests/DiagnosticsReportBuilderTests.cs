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

    [Test]
    public void TheHeaderCarriesEveryEnvironmentFactAReaderNeeds()
    {
        var report = Build();

        Assert.That(report, Does.Contain("1.2.3 (45)"));
        Assert.That(report, Does.Contain("Release"));
        Assert.That(report, Does.Contain("Google Pixel 7"));
        Assert.That(report, Does.Contain("Android 15"));
        Assert.That(report, Does.Contain(CapturedAt.ToString("O")));
    }

    [Test]
    public void TheBuildConfigurationIsStatedRatherThanLeftToBeInferred()
    {
        // This repo genuinely diverges between Debug and Release — log levels, CI stubs, fault
        // injection — so a report that does not say which one it came from is missing a fact the
        // reader would otherwise guess wrong.
        Assert.That(Build(context: Context with { BuildConfiguration = "Debug" }), Does.Contain("Debug"));
    }

    [TestCase(true, "yes")]
    [TestCase(false, "no")]
    public void SignedInStateIsStatedOutright(bool signedIn, string expected)
    {
        var report = Build(context: Context with { IsSignedIn = signedIn });

        Assert.That(report, Does.Contain($"signed in: {expected}"));
    }

    [Test]
    public void TheHeaderCarriesNoSentryEventCrossReference()
    {
        // An earlier draft printed a "related event:" id linking the report to the automatic
        // exception capture. On device it was always empty — neither ErrorReportService's
        // CaptureException return value nor SentrySdk.LastEventId holds the id by report time — so
        // the field was removed rather than shipped reliably blank. The exception text and the
        // timestamps below are what correlate the two, and both are already in full.
        Assert.That(Build(), Does.Not.Contain("related event").IgnoreCase);
    }

    // ── The user's own words ─────────────────────────────────────────

    [Test]
    public void TheDescriptionIsIncludedUnderItsOwnHeader()
    {
        var report = Build(description: "I kept tapping manga and they all failed");

        Assert.That(report, Does.Contain(DiagnosticsReportBuilder.DescriptionHeader));
        Assert.That(report, Does.Contain("I kept tapping manga and they all failed"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void TheDescriptionSectionIsOmittedWhenNothingWasTyped(string? description)
    {
        // The box is optional. An empty section would imply the user declined to explain, when in
        // fact they may simply have wanted the log.
        Assert.That(Build(description: description), Does.Not.Contain(DiagnosticsReportBuilder.DescriptionHeader));
    }

    // ── Sessions ─────────────────────────────────────────────────────

    [Test]
    public void ThePreviousSessionIsIncludedUnderItsOwnHeaderWhenOneWasFlushed()
    {
        var report = Build(previous: ["crashed here"], current: ["and now this"]);

        Assert.That(report, Does.Contain(DiagnosticsSessionLog.PreviousSessionHeader));
        Assert.That(report, Does.Contain("crashed here"));
        Assert.That(report, Does.Contain("and now this"));
    }

    [Test]
    public void ThePreviousSessionSectionIsOmittedWhenThereIsNone()
    {
        Assert.That(Build(), Does.Not.Contain(DiagnosticsSessionLog.PreviousSessionHeader));
    }

    [Test]
    public void ThePreviousSessionComesBeforeTheCurrentOne()
    {
        // Chronological order, so the report reads as one narrative rather than two piles.
        var report = Build(previous: ["older"], current: ["newer"]);

        Assert.That(report.IndexOf("older", StringComparison.Ordinal) < report.IndexOf("newer", StringComparison.Ordinal), Is.True);
    }

    [Test]
    public void AnEmptyRingSaysSoRatherThanLookingTruncated()
    {
        // A report ending abruptly after the header is indistinguishable from one that was cut off.
        // Saying "nothing was recorded" is a fact; silence is a puzzle.
        var report = Build(current: []);

        Assert.That(report, Does.Contain(DiagnosticsReportBuilder.NoActivityPlaceholder));
    }

    [Test]
    public void EveryLogLineSurvivesIntact()
    {
        var lines = Enumerable.Range(0, 50).Select(i => $"line {i}").ToArray();

        var report = Build(current: lines);

        Assert.Multiple(() =>
        {
            foreach (var line in lines)
            {
                Assert.That(report, Does.Contain(line));
            }
        });
    }

    // ── Redaction: the part that must not regress ────────────────────

    [Test]
    public void ABearerTokenInALogLineIsRedacted()
    {
        // The real shape: AniListClient embeds up to 500 characters of a raw error body into an
        // exception message, and an auth-failure body can echo the credential back.
        var report = Build(current: ["[Error] Api call failed: {\"errors\":[{\"message\":\"Invalid token: Bearer eyJhbGciOiJIUzI1NiJ9.abc-_123\"}]}"]);

        Assert.That(report, Does.Not.Contain("eyJhbGciOiJIUzI1NiJ9"));
        Assert.That(report, Does.Contain(SensitiveText.RedactedBearer));
    }

    [Test]
    public void ABearerTokenInThePreviousSessionIsRedactedToo()
    {
        // The flushed session takes a different route into the report — off disk rather than out of
        // memory — and an earlier design that redacted only the live ring would have missed it.
        var report = Build(previous: ["Authorization: Bearer eyJsecrettoken"], current: ["fine"]);

        Assert.That(report, Does.Not.Contain("eyJsecrettoken"));
    }

    [Test]
    public void ABearerTokenPastedIntoTheDescriptionIsRedacted()
    {
        // Free text the user controls. They can paste anything into it, including something they
        // copied out of an error message.
        var report = Build(description: "it said Bearer eyJpastedbyhand and then failed");

        Assert.That(report, Does.Not.Contain("eyJpastedbyhand"));
    }

    [Test]
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

        Assert.That(report, Does.Not.Contain("eyJfirsttoken"));
        Assert.That(report, Does.Not.Contain("eyJsecondtoken"));
        Assert.That(report, Does.Not.Contain("eyJthirdtoken"));
        Assert.That(report.Split(SensitiveText.RedactedBearer).Length - 1, Is.EqualTo(3));
    }

    [Test]
    public void ATokenWrappedAcrossALineBreakIsStillRedacted()
    {
        // Records are not strictly one line each — an appended stack trace keeps its breaks — so the
        // redaction must not depend on line structure. It doesn't: the pass runs over the whole
        // assembled string, and `Bearer\s+…` matches `\s` including a newline.
        var report = Build(current: ["exception text ending in Bearer\n  eyJwrappedacrossabreak and more"]);

        Assert.That(report, Does.Not.Contain("eyJwrappedacrossabreak"));
    }

    [Test]
    public void RedactionRunsOverTheWholeReportIncludingSectionsAddedLater()
    {
        // A single pass over the finished text rather than per-section, so a section someone adds
        // later cannot quietly skip it. Placing a token in every section at once is the assertion
        // that the pass is global.
        var report = Build(
            previous: ["Bearer eyJinprevious"],
            current: ["Bearer eyJincurrent"],
            description: "Bearer eyJindescription");

        Assert.That(report, Does.Not.Contain("eyJinprevious"));
        Assert.That(report, Does.Not.Contain("eyJincurrent"));
        Assert.That(report, Does.Not.Contain("eyJindescription"));
    }

    // ── Format ───────────────────────────────────────────────────────

    [Test]
    public void TheReportIsPlainTextWithNoTrailingSurprises()
    {
        var report = Build();

        Assert.That(string.IsNullOrWhiteSpace(report), Is.False);
        Assert.That(report, Does.StartWith("AniSprinkles diagnostic report"));
    }
}
