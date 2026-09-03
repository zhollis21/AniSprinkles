using AniSprinkles.Services.Abstractions;

namespace AniSprinkles.UnitTests.Fakes;

/// <summary>
/// An <see cref="IDialogService"/> whose answers are set per test and whose calls are recorded.
/// Every answer defaults to "the user dismissed it", which is the safe default for tests that only
/// need a page model to construct.
/// <para>
/// This is the double the seam in #62 exists for: before it, a page model reaching a popup went
/// through <c>Shell.Current</c>, which is <c>null</c> rather than throwing off-device — so the flow
/// silently no-opped and the test passed without exercising anything.
/// </para>
/// </summary>
public sealed class ScriptedDialogService : IDialogService
{
    private readonly List<string> _calls = [];

    /// <summary>Names of the dialogs shown, in order.</summary>
    public IReadOnlyList<string> Calls => _calls;

    /// <summary>Invoked as each dialog is shown, for tests that assert on ordering.</summary>
    public Action<string>? OnCall { get; set; }

    /// <summary>
    /// Runs before <see cref="ConfirmAsync"/> returns. Lets a test hold a confirmation open and act
    /// while the flow is genuinely mid-dialog.
    /// </summary>
    public Func<Task>? BeforeConfirmAsync { get; set; }

    public MediaListEntryAction? EntryActionAnswer { get; set; }

    public MoveToListChoice? MoveToListAnswer { get; set; }

    public int? EditProgressAnswer { get; set; }

    public double? RatingAnswer { get; set; }

    public bool ConfirmAnswer { get; set; }

    public string? PromptAnswer { get; set; }

    public Task<MediaListEntryAction?> ShowEntryActionsAsync(
        string mediaTitle,
        IReadOnlyList<MediaListEntryAction> actions)
    {
        Record(nameof(ShowEntryActionsAsync));
        return Task.FromResult(EntryActionAnswer);
    }

    /// <summary>The kind the last ShowMoveToListAsync call was made with, so tests can assert the
    /// sheet is labelled for the right media type (#12).</summary>
    public MediaKind? LastMoveToListKind { get; private set; }

    public Task<MoveToListChoice?> ShowMoveToListAsync(
        string mediaTitle,
        MediaListStatus? currentStatus,
        MediaKind kind = MediaKind.Anime,
        bool allowRemove = true,
        string? subtitle = null)
    {
        LastMoveToListKind = kind;
        Record(nameof(ShowMoveToListAsync));
        return Task.FromResult(MoveToListAnswer);
    }

    /// <summary>What the last ShowEditProgressAsync call was told to edit — unit and cap.</summary>
    public MediaProgressUnit? LastEditProgressUnit { get; private set; }

    public int? LastEditProgressMax { get; private set; }

    public int? LastEditProgressCurrent { get; private set; }

    public Task<int?> ShowEditProgressAsync(string mediaTitle, int currentProgress, int? maxProgress, MediaProgressUnit unit)
    {
        LastEditProgressUnit = unit;
        LastEditProgressMax = maxProgress;
        LastEditProgressCurrent = currentProgress;
        Record(nameof(ShowEditProgressAsync));
        return Task.FromResult(EditProgressAnswer);
    }

    public Task<double?> ShowRatingAsync(string? mediaTitle, double? initialScore)
    {
        Record(nameof(ShowRatingAsync));
        return Task.FromResult(RatingAnswer);
    }

    /// <summary>Copy from the last ConfirmAsync call, so tests can assert user-visible wording.</summary>
    public string? LastConfirmTitle { get; private set; }

    public string? LastConfirmMessage { get; private set; }

    public Task<bool> ConfirmAsync(
        string title,
        string message,
        string confirmText = "OK",
        string cancelText = "Cancel",
        bool isDestructive = false,
        string? iconGlyph = null)
    {
        LastConfirmTitle = title;
        LastConfirmMessage = message;
        Record(nameof(ConfirmAsync));
        return BeforeConfirmAsync is null ? Task.FromResult(ConfirmAnswer) : AwaitThenAnswerAsync();

        async Task<bool> AwaitThenAnswerAsync()
        {
            await BeforeConfirmAsync();
            return ConfirmAnswer;
        }
    }

    public Task<string?> PromptAsync(
        string title,
        string message,
        string? initialValue = null,
        int maxLength = -1,
        bool numericKeyboard = false)
    {
        Record(nameof(PromptAsync));
        return Task.FromResult(PromptAnswer);
    }

    /// <summary>
    /// What the send-diagnostics sheet returns. Left null — "the user cancelled" — so a test has to
    /// opt in to sending, and no test accidentally ships a report it never meant to.
    /// </summary>
    public DiagnosticsReportChoice? DiagnosticsReportAnswer { get; set; }

    /// <summary>The disclosure text the sheet was last shown with, so a test can assert the user was
    /// actually told what would be collected.</summary>
    public string? LastDiagnosticsSummary { get; private set; }

    /// <summary>Runs before the sheet returns, letting a test observe state while the flow is
    /// genuinely mid-disclosure — used to prove nothing is collected until the user consents.</summary>
    public Func<Task>? BeforeDiagnosticsReportAsync { get; set; }

    public Task<DiagnosticsReportChoice?> ShowDiagnosticsReportAsync(string summary)
    {
        LastDiagnosticsSummary = summary;
        Record(nameof(ShowDiagnosticsReportAsync));
        return BeforeDiagnosticsReportAsync is null
            ? Task.FromResult(DiagnosticsReportAnswer)
            : AwaitThenAnswerAsync();

        async Task<DiagnosticsReportChoice?> AwaitThenAnswerAsync()
        {
            await BeforeDiagnosticsReportAsync();
            return DiagnosticsReportAnswer;
        }
    }

    private void Record(string call)
    {
        _calls.Add(call);
        OnCall?.Invoke(call);
    }
}
