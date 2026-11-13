namespace AniSprinkles.UnitTests;

/// <summary>
/// Guards the promises the send-diagnostics popup makes (#112).
/// <para>
/// The disclosure is the only thing standing between "the user consented" and "the app took their
/// data" — and it is trusted precisely because it is specific. That specificity is also what makes it
/// fragile: every claim in it is a statement about code somewhere else, and nothing else would notice
/// if one of them stopped being true. These tests are what notice.
/// </para>
/// </summary>
public class DiagnosticsDisclosureTests
{
    [Test]
    public void TheDisclosuresFiveMinutesMatchesTheRingsActualRetention()
    {
        // Change DefaultRetention without changing the copy and the app starts lying to users about
        // how far back it reaches. This is the drift that would never be caught by hand.
        Assert.That(DiagnosticsLogBuffer.DefaultRetention, Is.EqualTo(TimeSpan.FromMinutes(5)));
        Assert.That(DiagnosticsReportCoordinator.DisclosureSummary, Does.Contain("5 minutes"));
    }

    [Test]
    public void TheConfirmationRepeatsTheWindowTheDisclosurePromised()
    {
        // Sentry says nothing back to the sender, so these two strings are the entire user-visible
        // account of what happened. They have to agree.
        Assert.That(DiagnosticsReportCoordinator.SentMessage, Does.Contain("5 minutes"));
    }

    [TestCase("app activity")]
    [TestCase("AniList ids")]
    [TestCase("requests")]
    [TestCase("app version")]
    [TestCase("device")]
    public void TheDisclosureNamesEachCategoryTheReportActuallyCarries(string claim)
    {
        // Each of these corresponds to something genuinely in the report: the ring's NAVTRACE and
        // PageState lines, the ids they carry, LoggingHandler's HTTP lines, and the header built from
        // IAppEnvironment. A category that quietly leaves the disclosure is data sent unannounced.
        // The constant really is the actual value here; NUnit2007 reads a const in that slot as a
        // swapped assertion, so it goes through a local.
        var summary = DiagnosticsReportCoordinator.DisclosureSummary;
        Assert.That(summary, Does.Contain(claim).IgnoreCase);
    }

    [Test]
    public void TheDisclosurePromisesTheLoginIsExcluded_AndTheBuilderKeepsThatPromise()
    {
        // The one hard guarantee the popup makes. It is kept by the redaction pass in the builder and
        // nowhere else — SentryScrubber runs over the SentryEvent and does not walk attachments — so
        // the claim and its enforcement are asserted together here rather than trusting either alone.
        Assert.That(DiagnosticsReportCoordinator.DisclosureSummary, Does.Contain("login is never included"));

        var report = DiagnosticsReportBuilder.Build(
            new DiagnosticsContext("1.0", "Release", "Pixel", "Android 15", IsSignedIn: true),
            DateTimeOffset.UnixEpoch,
            previousSession: ["Authorization: Bearer eyJfromtheprevioussession"],
            currentSession: ["Invalid token: Bearer eyJfromthisone"],
            description: "and I pasted Bearer eyJbyhand too");

        Assert.That(report, Does.Not.Contain("eyJfromtheprevioussession"));
        Assert.That(report, Does.Not.Contain("eyJfromthisone"));
        Assert.That(report, Does.Not.Contain("eyJbyhand"));
    }

    // ── The Settings card must not out-promise the popup ─────────────

    [Test]
    public void TheSettingsCardDoesNotPromiseAPreviewOfTheReportItself()
    {
        // It used to say "You'll see exactly what gets sent first", and that was false: the popup
        // lists the categories it collects, it does not show the report, which runs to thousands of
        // lines. An overclaim in the one sentence whose entire value is being trustworthy is the
        // worst possible place for one — so the copy is pinned here rather than left to review.
        var xaml = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "src", "AniSprinkles", "Views", "DiagnosticsReportView.xaml"));

        Assert.That(xaml, Does.Not.Contain("exactly what gets sent").IgnoreCase);
        Assert.That(xaml, Does.Contain("You'll see what's included before anything is sent."));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AniSprinkles.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException(
                $"Could not locate repo root (AniSprinkles.slnx) walking up from {AppContext.BaseDirectory}");
    }

    [Test]
    public void TheFailureMessageDoesNotClaimAnythingWasSent()
    {
        // A cheerful confirmation after a failed send is the worst outcome this flow has: the user
        // stops chasing the problem and nobody ever sees it.
        Assert.That(DiagnosticsReportCoordinator.FailedMessage, Does.Not.Contain("sent").IgnoreCase);
    }
}
