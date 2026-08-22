using System.ComponentModel;

namespace AniSprinkles.Services.Abstractions;

/// <summary>
/// Tracks whether the AniList API is currently reporting a service-level outage
/// (HTTP 5xx, "temporarily disabled", etc.). Page models and a global banner bind
/// to the observable properties so the user gets persistent feedback — not a
/// fleeting snackbar — while the service is down.
/// </summary>
public interface IOutageStateService : INotifyPropertyChanged
{
    bool IsOutage { get; }

    /// <summary>Short user-facing title (e.g. "AniList is Temporarily Down").</summary>
    string Title { get; }

    /// <summary>Longer user-facing subtitle with guidance.</summary>
    string Subtitle { get; }

    /// <summary>FluentIcon glyph string to show alongside the message.</summary>
    string IconGlyph { get; }

    /// <summary>Report a failed API call. Does nothing for non-outage error kinds.</summary>
    void ReportFailure(Exception ex);

    /// <summary>Report a successful API call. Clears any active outage state.</summary>
    void ReportSuccess();
}
