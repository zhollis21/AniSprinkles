namespace AniSprinkles.Services.Abstractions;

/// <summary>
/// What the user chose in the move/add-to-list sheet. The sheet offers a status per list plus an
/// optional "remove from list" row, so a choice is either a target status or a removal — never both.
/// </summary>
public readonly record struct MoveToListChoice
{
    private MoveToListChoice(MediaListStatus? status, bool isRemove)
    {
        Status = status;
        IsRemove = isRemove;
    }

    /// <summary>The list the user picked, or <c>null</c> when <see cref="IsRemove"/> is set.</summary>
    public MediaListStatus? Status { get; }

    /// <summary>The user picked "remove from list" rather than a target list.</summary>
    public bool IsRemove { get; }

    public static MoveToListChoice To(MediaListStatus status) => new(status, isRemove: false);

    public static MoveToListChoice Remove { get; } = new(status: null, isRemove: true);
}

/// <summary>
/// What the user chose in the send-diagnostics sheet (#112). Reaching this at all means they pressed
/// Send; <see cref="Description"/> is what they typed, which is optional — requiring text would be
/// friction for someone who only wants the log attached.
/// </summary>
public readonly record struct DiagnosticsReportChoice(string? Description);

/// <summary>
/// Modal user interaction — confirmations, prompts, and the bottom-sheet pickers. Abstracted for the
/// same reason as <see cref="INavigationService"/> and <see cref="IUserFeedback"/>: the concrete
/// popups are XAML-backed <c>CommunityToolkit.Maui</c> types that only exist in the MAUI app project,
/// and page models have to be exercisable from <c>net10.0</c> unit tests without them.
/// <para>
/// Note that <c>Shell.Current</c> is <c>null</c> rather than throwing off-device, so a page model
/// calling a popup directly would silently no-op in a test and read as a pass. Going through this
/// interface is what lets a test assert the dialog was shown and control what it returns.
/// </para>
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Long-press action menu for a list entry. Returns the chosen action, or <c>null</c> if the
    /// user dismissed the sheet.
    /// </summary>
    Task<MediaListEntryAction?> ShowEntryActionsAsync(string mediaTitle, IReadOnlyList<MediaListEntryAction> actions);

    /// <summary>
    /// Status picker used for both "move to list" (<paramref name="allowRemove"/> true, current
    /// status omitted) and "add to list" (<paramref name="currentStatus"/> null, no remove row).
    /// Returns <c>null</c> if the user dismissed the sheet.
    /// </summary>
    Task<MoveToListChoice?> ShowMoveToListAsync(
        string mediaTitle,
        MediaListStatus? currentStatus,
        MediaKind kind = MediaKind.Anime,
        bool allowRemove = true,
        string? subtitle = null);

    /// <summary>
    /// Progress editor. Returns the requested progress, or <c>null</c> if dismissed. The value is
    /// the user's raw request — the caller still owns clamping to the entry's bounds.
    /// <paramref name="unit"/> only labels the control; the numbers mean whatever the caller says.
    /// </summary>
    Task<int?> ShowEditProgressAsync(string mediaTitle, int currentProgress, int? maxProgress, MediaProgressUnit unit);

    /// <summary>
    /// Score picker, pre-populated from <paramref name="initialScore"/>. Returns <c>null</c> when the
    /// user skips or dismisses it, which means "leave the score alone" rather than "clear it".
    /// </summary>
    Task<double?> ShowRatingAsync(string? mediaTitle, double? initialScore);

    /// <summary>Two-button confirmation. Returns <c>false</c> on cancel or dismissal.</summary>
    Task<bool> ConfirmAsync(
        string title,
        string message,
        string confirmText = "OK",
        string cancelText = "Cancel",
        bool isDestructive = false,
        string? iconGlyph = null);

    /// <summary>
    /// The send-diagnostics sheet (#112): states what <paramref name="summary"/> says is about to be
    /// collected, and offers an optional box for what the user was doing. Returns <c>null</c> if they
    /// cancelled or dismissed it.
    /// <para>
    /// A choice rather than a nullable string because "cancelled" and "sent without a note" are
    /// different answers that a <c>string?</c> would flatten into the same <c>null</c> — and one of
    /// them must not send anything.
    /// </para>
    /// </summary>
    Task<DiagnosticsReportChoice?> ShowDiagnosticsReportAsync(string summary);

    /// <summary>
    /// Single-line text prompt. Returns <c>null</c> if the user cancelled.
    /// </summary>
    Task<string?> PromptAsync(
        string title,
        string message,
        string? initialValue = null,
        int maxLength = -1,
        bool numericKeyboard = false);
}
