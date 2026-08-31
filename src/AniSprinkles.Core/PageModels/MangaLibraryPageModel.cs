using Microsoft.Extensions.Logging;

namespace AniSprinkles.PageModels;

/// <summary>
/// The manga half of the Library tab (#12). Everything is inherited: it overrides neither
/// post-load hook because manga does not air, so there is no schedule to poll, nothing to cache
/// for a background worker, and no notification permission to ask for.
/// <para>
/// It still takes <see cref="IAiringNotificationService"/> because the base constructor does. That
/// is a little untidy, but the alternative — two constructors on the base, or a nullable service —
/// buys nothing: the instance is simply never touched from this side.
/// </para>
/// </summary>
public partial class MangaLibraryPageModel(
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
        MediaKind.Manga,
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
        logger);
