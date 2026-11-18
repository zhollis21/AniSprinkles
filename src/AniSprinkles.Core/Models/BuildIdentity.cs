using System.Text.RegularExpressions;

namespace AniSprinkles.Models;

/// <summary>
/// Which build is running, and where it came from (#207): the version Android knows about plus the
/// source stamps <c>AniSprinkles.csproj</c> writes into the app assembly.
/// <para>
/// Only <c>android-release.yml</c> sets a real version. Every other build — local runs and both CI
/// builds — carries the csproj's placeholder <c>1.0</c>, so for those the label is built from the
/// nearest release tag, the branch and the commit instead.
/// </para>
/// </summary>
/// <param name="VersionName">Android's display version, e.g. <c>1.2.3</c>.</param>
/// <param name="VersionCode">Android's version code, e.g. <c>261117042</c>.</param>
/// <param name="Branch">The branch built, or empty when detached or unknown.</param>
/// <param name="Commit">The full commit SHA, or empty when unknown.</param>
/// <param name="Describe"><c>git describe --tags --long</c> output, e.g. <c>v0.15.0-12-g6f69547</c>,
/// or empty when no release tag is reachable.</param>
/// <param name="IsDirty">The working tree had uncommitted changes when it was built.</param>
/// <param name="IsStoreBuild">Built by the release workflow for the Play Store.</param>
public sealed partial record BuildIdentity(
    string VersionName,
    string VersionCode,
    string? Branch,
    string? Commit,
    string? Describe,
    bool IsDirty,
    bool IsStoreBuild)
{
    /// <summary>The length git itself abbreviates to, and what the issue's examples use.</summary>
    public const int ShortCommitLength = 7;

    /// <summary>
    /// The version line at the bottom of Settings. A store build shows <c>v1.2.3 (261117042)</c> —
    /// what Play Console and the diagnostics report show, with no hash in front of users. Anything
    /// else shows <c>v0.15.0+12 · claude/foo @ 6f69547</c>, with <c>*</c> when the tree was dirty.
    /// </summary>
    public string SettingsLabel
    {
        get
        {
            if (IsStoreBuild)
            {
                return string.IsNullOrWhiteSpace(VersionCode) ? $"v{VersionName}" : $"v{VersionName} ({VersionCode})";
            }

            var version = TagVersion() ?? $"v{VersionName}";
            return SourceLine is { } source ? $"{version} · {source}" : version;
        }
    }

    /// <summary>
    /// <c>claude/foo @ 6f69547</c>, either half alone when the other is unknown, or <c>null</c> when
    /// both are. Store builds have one too: the diagnostics report is read by the developer, not shown
    /// to users.
    /// </summary>
    public string? SourceLine
    {
        get
        {
            var commit = ShortCommit();
            var hasBranch = !string.IsNullOrWhiteSpace(Branch);
            return (hasBranch, commit) switch
            {
                (true, not null) => $"{Branch} @ {commit}",
                (true, null) => Branch,
                (false, not null) => commit,
                _ => null,
            };
        }
    }

    private string? ShortCommit()
    {
        if (string.IsNullOrWhiteSpace(Commit))
        {
            return null;
        }

        var shortCommit = Commit.Length > ShortCommitLength ? Commit[..ShortCommitLength] : Commit;
        return IsDirty ? shortCommit + "*" : shortCommit;
    }

    /// <summary>
    /// <c>v0.15.0-12-g6f69547</c> becomes <c>v0.15.0+12</c>, and <c>v0.15.0-0-g…</c> plain
    /// <c>v0.15.0</c>. Parsed from the right, because a prerelease tag (<c>v1.0.0-rc.1</c>) uses the
    /// same <c>-</c> separator git describe does.
    /// </summary>
    private string? TagVersion()
    {
        if (string.IsNullOrWhiteSpace(Describe))
        {
            return null;
        }

        var match = DescribePattern().Match(Describe);
        if (!match.Success)
        {
            return null;
        }

        var tag = match.Groups["tag"].Value;
        var distance = match.Groups["distance"].Value;
        return distance == "0" ? $"v{tag}" : $"v{tag}+{distance}";
    }

    [GeneratedRegex(@"^v?(?<tag>.+)-(?<distance>\d+)-g[0-9a-f]+$")]
    private static partial Regex DescribePattern();
}
