using AniSprinkles.Views;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Maui.Views;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.Services.Maui;

/// <summary>
/// <see cref="IDialogService"/> adapter over the XAML popups in <c>AniSprinkles.Views</c> and
/// <c>Shell.Current.DisplayPromptAsync</c>. Lives here rather than in Core because every one of those
/// types needs <c>CommunityToolkit.Maui</c> and a live shell; keeping them behind the interface is
/// what lets the page models run on the plain <c>net10.0</c> test TFM. Mirrors
/// <see cref="MauiShellNavigationService"/> and <see cref="MauiUserFeedback"/>.
/// <para>
/// Every method returns the "user dismissed" value when there is no current page, matching the
/// behaviour the call sites already guarded for when they reached <c>Shell.Current</c> themselves.
/// </para>
/// </summary>
public sealed class MauiDialogService(ILogger<MauiDialogService> logger) : IDialogService
{
    // The toolkit's PopupBorder is the element that reaches the screen edge, so it must BE the visible
    // sheet: give it the rounded-top bottom-sheet shape (the popup's BackgroundColor fills it). A nested
    // rounded Border would leave the PopupBorder's own bottom strip transparent, showing the dim scrim.
    private static PopupOptions BottomSheetPopupOptions => new()
    {
        Shape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
        {
            CornerRadius = new CornerRadius(20, 20, 0, 0),
            StrokeThickness = 0,
        },
        Shadow = null,
        CanBeDismissedByTappingOutsideOfPopup = true,
    };

    private static PopupOptions TransparentPopupOptions => new()
    {
        Shape = null,
        Shadow = null,
        CanBeDismissedByTappingOutsideOfPopup = true,
    };

    public async Task<MyAnimeEntryAction?> ShowEntryActionsAsync(
        string animeTitle,
        IReadOnlyList<MyAnimeEntryAction> actions)
    {
        var result = await ShowAsync(new ActionMenuPopup(animeTitle, actions), BottomSheetPopupOptions);
        return result as MyAnimeEntryAction?;
    }

    public async Task<MoveToListChoice?> ShowMoveToListAsync(
        string mediaTitle,
        MediaListStatus? currentStatus,
        MediaKind kind = MediaKind.Anime,
        bool allowRemove = true,
        string? subtitle = null)
    {
        var popup = new MoveToListPopup(mediaTitle, currentStatus, kind, allowRemove, subtitle);
        var result = await ShowAsync(popup, BottomSheetPopupOptions);

        return result switch
        {
            MediaListStatus status => MoveToListChoice.To(status),
            // The sheet signals its remove row with a sentinel string rather than a status.
            string action when action == "delete" => MoveToListChoice.Remove,
            _ => null,
        };
    }

    public async Task<int?> ShowEditProgressAsync(string mediaTitle, int currentProgress, int? maxProgress, MediaProgressUnit unit)
    {
        var popup = new EditProgressPopup(mediaTitle, currentProgress, maxProgress, unit);
        var result = await ShowAsync(popup, TransparentPopupOptions);
        return result as int?;
    }

    public async Task<double?> ShowRatingAsync(string? animeTitle, double? initialScore)
    {
        var result = await ShowAsync(new RatingPopup(animeTitle, initialScore), TransparentPopupOptions);
        return result as double?;
    }

    public Task<bool> ConfirmAsync(
        string title,
        string message,
        string confirmText = "OK",
        string cancelText = "Cancel",
        bool isDestructive = false,
        string? iconGlyph = null)
        => ConfirmPopup.ShowAsync(title, message, confirmText, cancelText, isDestructive, iconGlyph);

    public async Task<string?> PromptAsync(
        string title,
        string message,
        string? initialValue = null,
        int maxLength = -1,
        bool numericKeyboard = false)
    {
        if (Shell.Current is not { } shell)
        {
            return null;
        }

        return await shell.DisplayPromptAsync(
            title,
            message,
            initialValue: initialValue ?? string.Empty,
            maxLength: maxLength,
            keyboard: numericKeyboard ? Keyboard.Numeric : Keyboard.Default);
    }

    private async Task<object?> ShowAsync(Popup<object> popup, PopupOptions options)
    {
        if (Shell.Current?.CurrentPage is not { } page)
        {
            logger.LogWarning("Popup {Popup} skipped: no current page.", popup.GetType().Name);
            return null;
        }

        var result = await page.ShowPopupAsync<object>(popup, options, CancellationToken.None);
        return result.WasDismissedByTappingOutsideOfPopup ? null : result.Result;
    }
}
