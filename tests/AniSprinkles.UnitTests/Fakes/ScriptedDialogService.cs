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

    public MyAnimeEntryAction? EntryActionAnswer { get; set; }

    public MoveToListChoice? MoveToListAnswer { get; set; }

    public int? EditProgressAnswer { get; set; }

    public double? RatingAnswer { get; set; }

    public bool ConfirmAnswer { get; set; }

    public string? PromptAnswer { get; set; }

    public Task<MyAnimeEntryAction?> ShowEntryActionsAsync(
        string animeTitle,
        IReadOnlyList<MyAnimeEntryAction> actions)
    {
        Record(nameof(ShowEntryActionsAsync));
        return Task.FromResult(EntryActionAnswer);
    }

    public Task<MoveToListChoice?> ShowMoveToListAsync(
        string animeTitle,
        MediaListStatus? currentStatus,
        bool allowRemove = true,
        string? subtitle = null)
    {
        Record(nameof(ShowMoveToListAsync));
        return Task.FromResult(MoveToListAnswer);
    }

    public Task<int?> ShowEditProgressAsync(string animeTitle, int currentProgress, int? maxEpisodes)
    {
        Record(nameof(ShowEditProgressAsync));
        return Task.FromResult(EditProgressAnswer);
    }

    public Task<double?> ShowRatingAsync(string? animeTitle, double? initialScore)
    {
        Record(nameof(ShowRatingAsync));
        return Task.FromResult(RatingAnswer);
    }

    public Task<bool> ConfirmAsync(
        string title,
        string message,
        string confirmText = "OK",
        string cancelText = "Cancel",
        bool isDestructive = false,
        string? iconGlyph = null)
    {
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

    private void Record(string call)
    {
        _calls.Add(call);
        OnCall?.Invoke(call);
    }
}
