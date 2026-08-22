namespace AniSprinkles.Services.Abstractions;

/// <summary>
/// Opens a URL outside the app (the "View on AniList" links). Abstracted over
/// <c>Browser.Default</c>, whose <c>OpenAsync</c> throws
/// <c>NotImplementedInReferenceAssemblyException</c> on the plain <c>net10.0</c> TFM the unit tests
/// run on. Mirrors why <see cref="INavigationService"/> and <see cref="IUserFeedback"/> exist.
/// </summary>
public interface IExternalBrowser
{
    /// <summary>Opens <paramref name="uri"/> in the system browser. Never throws — failures are logged.</summary>
    Task OpenAsync(Uri uri);
}
