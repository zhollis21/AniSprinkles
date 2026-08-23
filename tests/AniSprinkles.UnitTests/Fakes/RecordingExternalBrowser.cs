using AniSprinkles.Services.Abstractions;

namespace AniSprinkles.UnitTests.Fakes;

/// <summary>
/// Records the URLs a page model asked the system browser to open. The real <c>MauiExternalBrowser</c>
/// swallows its own failures, so "did the View on AniList command actually fire, and with what?" is
/// only answerable against a recording double.
/// </summary>
public sealed class RecordingExternalBrowser : IExternalBrowser
{
    private readonly List<Uri> _opened = [];

    public IReadOnlyList<Uri> Opened => _opened;

    public Uri? LastOpened => _opened.Count > 0 ? _opened[^1] : null;

    public Task OpenAsync(Uri uri)
    {
        _opened.Add(uri);
        return Task.CompletedTask;
    }
}
