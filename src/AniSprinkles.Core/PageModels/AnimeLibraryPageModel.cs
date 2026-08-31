using Microsoft.Extensions.Logging;

namespace AniSprinkles.PageModels;

/// <summary>
/// The anime half of the Library tab. All behaviour lives in <see cref="MediaListPageModel"/>;
/// this exists so DI and XAML's <c>x:DataType</c> have a distinct type to name, and to carry the
/// airing-notification side effects, which are the one thing genuinely specific to anime.
/// </summary>
public partial class AnimeLibraryPageModel(
    IAniListClient aniListClient,
    IAuthService authService,
    IAiringNotificationService airingNotificationService,
    ErrorReportService errorReportService,
    IPreferences preferences,
    INavigationService navigationService,
    IDialogService dialogs,
    IUserFeedback feedback,
    ListEntryStatusFlow statusFlow,
    TimeProvider timeProvider,
    ILogger<MediaListPageModel> logger)
    : MediaListPageModel(
        MediaKind.Anime,
        aniListClient,
        authService,
        airingNotificationService,
        errorReportService,
        preferences,
        navigationService,
        dialogs,
        feedback,
        statusFlow,
        timeProvider,
        logger)
{
    /// <inheritdoc />
    protected override void OnListLoaded(
        IReadOnlyList<(string Name, IReadOnlyList<MediaListEntry> Entries)> groups)
    {
        // Cache RELEASING media IDs for the background airing notification worker.
        CacheReleasingMediaIds(groups);

        // On first authenticated load, prompt for notification permission if not yet decided.
        // Status Unknown → shows dialog once. Granted/Denied → returns immediately on future loads.
        _ = RequestNotificationPermissionIfNeededAsync();
    }

    /// <inheritdoc />
    protected override void OnSignedOut()
    {
        AiringNotifications.CancelPeriodicCheck();
        AiringNotifications.ClearNotificationState();
    }
}
