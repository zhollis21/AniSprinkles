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
    [Fact]
    public void TheDisclosuresFiveMinutesMatchesTheRingsActualRetention()
    {
        // Change DefaultRetention without changing the copy and the app starts lying to users about
        // how far back it reaches. This is the drift that would never be caught by hand.
        Assert.Equal(TimeSpan.FromMinutes(5), DiagnosticsLogBuffer.DefaultRetention);
        Assert.Contains("5 minutes", DiagnosticsReportCoordinator.DisclosureSummary);
    }

    [Fact]
    public void TheConfirmationRepeatsTheWindowTheDisclosurePromised()
    {
        // Sentry says nothing back to the sender, so these two strings are the entire user-visible
        // account of what happened. They have to agree.
        Assert.Contains("5 minutes", DiagnosticsReportCoordinator.SentMessage);
    }

    [Theory]
    [InlineData("app activity")]
    [InlineData("AniList ids")]
    [InlineData("requests")]
    [InlineData("app version")]
    [InlineData("device")]
    public void TheDisclosureNamesEachCategoryTheReportActuallyCarries(string claim)
    {
        // Each of these corresponds to something genuinely in the report: the ring's NAVTRACE and
        // PageState lines, the ids they carry, LoggingHandler's HTTP lines, and the header built from
        // IAppEnvironment. A category that quietly leaves the disclosure is data sent unannounced.
        Assert.Contains(claim, DiagnosticsReportCoordinator.DisclosureSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheDisclosurePromisesTheLoginIsExcluded_AndTheBuilderKeepsThatPromise()
    {
        // The one hard guarantee the popup makes. It is kept by the redaction pass in the builder and
        // nowhere else — SentryScrubber runs over the SentryEvent and does not walk attachments — so
        // the claim and its enforcement are asserted together here rather than trusting either alone.
        Assert.Contains("login is never included", DiagnosticsReportCoordinator.DisclosureSummary);

        var report = DiagnosticsReportBuilder.Build(
            new DiagnosticsContext("1.0", "Release", "Pixel", "Android 15", IsSignedIn: true),
            DateTimeOffset.UnixEpoch,
            previousSession: ["Authorization: Bearer eyJfromtheprevioussession"],
            currentSession: ["Invalid token: Bearer eyJfromthisone"],
            description: "and I pasted Bearer eyJbyhand too");

        Assert.DoesNotContain("eyJfromtheprevioussession", report);
        Assert.DoesNotContain("eyJfromthisone", report);
        Assert.DoesNotContain("eyJbyhand", report);
    }

    // ── The Settings card must not out-promise the popup ─────────────

    [Fact]
    public void TheSettingsCardDoesNotPromiseAPreviewOfTheReportItself()
    {
        // It used to say "You'll see exactly what gets sent first", and that was false: the popup
        // lists the categories it collects, it does not show the report, which runs to thousands of
        // lines. An overclaim in the one sentence whose entire value is being trustworthy is the
        // worst possible place for one — so the copy is pinned here rather than left to review.
        var xaml = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "src", "AniSprinkles", "Views", "DiagnosticsReportView.xaml"));

        Assert.DoesNotContain("exactly what gets sent", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("You'll see what's included before anything is sent.", xaml, StringComparison.Ordinal);
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

    [Fact]
    public void TheFailureMessageDoesNotClaimAnythingWasSent()
    {
        // A cheerful confirmation after a failed send is the worst outcome this flow has: the user
        // stops chasing the problem and nobody ever sees it.
        Assert.DoesNotContain("sent", DiagnosticsReportCoordinator.FailedMessage, StringComparison.OrdinalIgnoreCase);
    }
}
