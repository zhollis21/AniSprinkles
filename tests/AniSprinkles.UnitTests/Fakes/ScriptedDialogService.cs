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
        _calls.Add(nameof(ShowEntryActionsAsync));
        return Task.FromResult(EntryActionAnswer);
    }

    public Task<MoveToListChoice?> ShowMoveToListAsync(
        string animeTitle,
        MediaListStatus? currentStatus,
        bool allowRemove = true,
        string? subtitle = null)
    {
        _calls.Add(nameof(ShowMoveToListAsync));
        return Task.FromResult(MoveToListAnswer);
    }

    public Task<int?> ShowEditProgressAsync(string animeTitle, int currentProgress, int? maxEpisodes)
    {
        _calls.Add(nameof(ShowEditProgressAsync));
        return Task.FromResult(EditProgressAnswer);
    }

    public Task<double?> ShowRatingAsync(string? animeTitle, double? initialScore)
    {
        _calls.Add(nameof(ShowRatingAsync));
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
        _calls.Add(nameof(ConfirmAsync));
        return Task.FromResult(ConfirmAnswer);
    }

    public Task<string?> PromptAsync(
        string title,
        string message,
        string? initialValue = null,
        int maxLength = -1,
        bool numericKeyboard = false)
    {
        _calls.Add(nameof(PromptAsync));
        return Task.FromResult(PromptAnswer);
    }
}
