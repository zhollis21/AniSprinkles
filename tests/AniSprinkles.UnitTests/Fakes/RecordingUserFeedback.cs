using AniSprinkles.Services.Abstractions;

namespace AniSprinkles.UnitTests.Fakes;

/// <summary>
/// Records what a page model tried to tell the user instead of showing it. The real
/// <c>MauiUserFeedback</c> swallows its own display failures, so tests that assert on user-visible
/// failure signalling need a recording double rather than a throwing one.
/// </summary>
public sealed class RecordingUserFeedback : IUserFeedback
{
    private readonly List<string> _snackbars = [];
    private readonly List<string> _toasts = [];

    public IReadOnlyList<string> Snackbars => _snackbars;

    public IReadOnlyList<string> Toasts => _toasts;

    /// <summary>The action attached to the most recent snackbar that offered one.</summary>
    public Action? LastSnackbarAction { get; private set; }

    public Task ShowSnackbarAsync(string message)
    {
        _snackbars.Add(message);
        return Task.CompletedTask;
    }

    public Task ShowSnackbarAsync(string message, string actionText, Action action, TimeSpan? duration = null)
    {
        _snackbars.Add(message);
        LastSnackbarAction = action;
        return Task.CompletedTask;
    }

    public Task ShowToastAsync(string message)
    {
        _toasts.Add(message);
        return Task.CompletedTask;
    }
}
