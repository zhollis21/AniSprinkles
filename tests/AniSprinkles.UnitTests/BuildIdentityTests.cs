namespace AniSprinkles.UnitTests;

/// <summary>
/// The version line at the bottom of Settings, and the source line of a diagnostic report (#207).
/// <para>
/// The label exists to answer "which build is this?". Store releases answer it with the version and
/// build code Play Console shows; everything else — local runs, both CI builds — with the nearest
/// release tag, the branch and the commit, because their version is the csproj's placeholder
/// <c>1.0</c> and says nothing.
/// </para>
/// </summary>
public class BuildIdentityTests
{
    private const string Sha = "6f69547a1b2c3d4e5f60718293a4b5c6d7e8f901";

    private static BuildIdentity Local(
        string? branch = "claude/foo",
        string? commit = Sha,
        string? describe = "v0.15.0-12-g6f69547",
        bool dirty = false)
        => new("1.0", "1", branch, commit, describe, dirty, IsStoreBuild: false);

    [Test]
    public void AStoreBuild_ShowsVersionAndBuildCode()
    {
        var build = new BuildIdentity("1.2.3", "261117042", Branch: null, Sha, Describe: null, IsDirty: false, IsStoreBuild: true);

        Assert.That(build.SettingsLabel, Is.EqualTo("v1.2.3 (261117042)"));
    }

    [Test]
    public void AStoreBuild_ShowsNoSourceDetail_EvenWhenItHasThem()
    {
        // Option A on #207: no hash and no branch in front of users. Sentry and the diagnostics
        // report identify the exact build.
        var build = new BuildIdentity("1.2.3", "261117042", "main", Sha, "v1.2.3-0-g6f69547", IsDirty: true, IsStoreBuild: true);

        Assert.That(build.SettingsLabel, Is.EqualTo("v1.2.3 (261117042)"));
    }

    [Test]
    public void AStoreBuildWithoutABuildCode_ShowsTheVersionAlone()
    {
        var build = new BuildIdentity("1.2.3", "", Branch: null, Commit: null, Describe: null, IsDirty: false, IsStoreBuild: true);

        Assert.That(build.SettingsLabel, Is.EqualTo("v1.2.3"));
    }

    [Test]
    public void ALocalBuild_ShowsTagDistanceBranchAndShortCommit()
    {
        Assert.That(Local().SettingsLabel, Is.EqualTo("v0.15.0+12 · claude/foo @ 6f69547"));
    }

    [Test]
    public void ALocalBuildWithUncommittedChanges_MarksTheCommit()
    {
        // The commit alone would claim the build is exactly 6f69547, which it is not.
        Assert.That(Local(dirty: true).SettingsLabel, Is.EqualTo("v0.15.0+12 · claude/foo @ 6f69547*"));
    }

    [Test]
    public void ABuildExactlyOnATag_ShowsTheTagWithoutADistance()
    {
        Assert.That(Local(describe: "v0.15.0-0-g6f69547").SettingsLabel, Is.EqualTo("v0.15.0 · claude/foo @ 6f69547"));
    }

    [Test]
    public void APrereleaseTag_KeepsItsSuffix()
    {
        // git describe joins with '-', and so do prerelease tags, so the split has to come from the
        // right: the last two fields are the distance and the hash.
        Assert.That(Local(describe: "v1.0.0-rc.1-3-g6f69547").SettingsLabel, Is.EqualTo("v1.0.0-rc.1+3 · claude/foo @ 6f69547"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("6f69547")]
    [TestCase("not-a-describe")]
    public void NoReachableTag_FallsBackToTheProjectVersion(string? describe)
    {
        Assert.That(Local(describe: describe).SettingsLabel, Is.EqualTo("v1.0 · claude/foo @ 6f69547"));
    }

    [Test]
    public void NoBranch_ShowsTheCommitAlone()
    {
        Assert.That(Local(branch: null).SettingsLabel, Is.EqualTo("v0.15.0+12 · 6f69547"));
    }

    [Test]
    public void NoCommit_ShowsTheBranchAlone()
    {
        Assert.That(Local(commit: null).SettingsLabel, Is.EqualTo("v0.15.0+12 · claude/foo"));
    }

    [Test]
    public void NoSourceInformationAtAll_ShowsTheVersionAlone()
    {
        // A build outside a git checkout: the stamps are empty, not missing.
        var build = new BuildIdentity("1.0", "1", "", "", "", IsDirty: false, IsStoreBuild: false);

        Assert.That(build.SettingsLabel, Is.EqualTo("v1.0"));
    }

    [Test]
    public void AShortCommitIsShownAsIs()
    {
        Assert.That(Local(commit: "abc12").SettingsLabel, Is.EqualTo("v0.15.0+12 · claude/foo @ abc12"));
    }

    [Test]
    public void TheReportSourceLine_CarriesBranchAndCommit()
    {
        Assert.That(Local(dirty: true).SourceLine, Is.EqualTo("claude/foo @ 6f69547*"));
    }

    [Test]
    public void TheReportSourceLine_IsPresentForStoreBuildsToo()
    {
        // Unlike the Settings label, the report is read by the developer, so a store build's
        // commit belongs in it.
        var build = new BuildIdentity("1.2.3", "261117042", Branch: null, Sha, Describe: null, IsDirty: false, IsStoreBuild: true);

        Assert.That(build.SourceLine, Is.EqualTo("6f69547"));
    }

    [Test]
    public void TheReportSourceLine_IsNullWithNothingToSay()
    {
        var build = new BuildIdentity("1.0", "1", null, null, null, IsDirty: false, IsStoreBuild: false);

        Assert.That(build.SourceLine, Is.Null);
    }
}
