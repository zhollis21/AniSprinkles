using AniSprinkles.Services.Abstractions;

namespace AniSprinkles.UnitTests.Fakes;

/// <summary>
/// Captures what would have gone to Sentry instead of sending it.
/// <para>
/// The seam this doubles exists because an uninitialised <c>SentrySdk</c> silently discards rather
/// than throwing — so a coordinator calling it directly would no-op in a test and read as a pass.
/// Here the report text is held for inspection, which is what lets the redaction tests assert on
/// exactly the bytes that would have left the device.
/// </para>
/// </summary>
public sealed class RecordingDiagnosticsSink : IDiagnosticsSink
{
    private readonly List<string> _reports = [];

    /// <summary>Every report body handed to the sink, in order.</summary>
    public IReadOnlyList<string> Reports => _reports;

    /// <summary>The description that accompanied the most recent send.</summary>
    public string? LastDescription { get; private set; }

    /// <summary>What <see cref="SendAsync"/> reports back. False models a send that did not land.</summary>
    public bool Result { get; set; } = true;

    /// <summary>When set, thrown instead of returning — the real sink swallows its own failures, so a
    /// throwing double is the only way to prove the coordinator does too.</summary>
    public Exception? Throws { get; set; }

    public Task<bool> SendAsync(string report, string? description, CancellationToken cancellationToken = default)
    {
        if (Throws is not null)
        {
            throw Throws;
        }

        _reports.Add(report);
        LastDescription = description;
        return Task.FromResult(Result);
    }
}
