using AniSprinkles.Utilities;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.Services;

/// <summary>
/// Offers a just-completed entry's sequels, side stories, spin-offs and alternatives (#103).
/// <see cref="Begin"/> is called by every flow that can end in Completed and returns a scope for
/// that one flow, or <c>null</c> when the offer is switched off — which is also what a test
/// substitute returns, so page-model tests that don't care about the offer don't see it.
/// </summary>
public interface IUpNextOffer
{
    UpNextScope? Begin(MediaListEntry completing);
}

/// <inheritdoc />
/// <remarks>Singleton; holds no per-flow state (that lives in <see cref="UpNextScope"/>).</remarks>
public sealed class UpNextOffer(
    IAniListClient aniListClient,
    IDialogService dialogs,
    IUserFeedback feedback,
    IPreferences preferences,
    ILogger<UpNextOffer> logger) : IUpNextOffer
{
    internal IDialogService Dialogs => dialogs;

    public UpNextScope? Begin(MediaListEntry completing)
    {
        if (completing?.Media is null || completing.MediaId <= 0)
        {
            return null;
        }

        if (!UpNextPreferences.IsEnabled(preferences))
        {
            // Said out loud: otherwise "it didn't offer anything" reads the same in the log whether the
            // switch was off or there was nothing to offer.
            logger.LogInformation("UPNEXT off: no offer for media {MediaId}", completing.MediaId);
            return null;
        }

        var kind = completing.Media.IsManga ? MediaKind.Manga : MediaKind.Anime;
        return new UpNextScope(
            this,
            completing.MediaId,
            completing.Media.DisplayTitle,
            kind,
            UpNextPreferences.DefaultStatus(preferences));
    }

    /// <summary>
    /// Never throws: an offer that cannot load is an offer not made. The completion it follows has
    /// already been saved, and a failure snackbar for something the user did not ask for is noise.
    /// </summary>
    internal async Task<IReadOnlyList<UpNextCandidate>> FetchCandidatesAsync(
        int mediaId, MediaKind kind, MediaListStatus settingsStatus, CancellationToken cancellationToken)
    {
        try
        {
            var edges = await aniListClient.GetMediaRelationsAsync(mediaId, cancellationToken);
            var candidates = UpNextCandidates.Select(kind, edges, AppSettings.DisplayAdultContent, settingsStatus);
            logger.LogInformation(
                "UPNEXT media {MediaId}: {Edges} relations, {Candidates} offered",
                mediaId, edges.Count, candidates.Count);
            return candidates;
        }
        catch (OperationCanceledException)
        {
            return [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "UPNEXT relations fetch failed for media {MediaId}; offering nothing", mediaId);
            return [];
        }
    }

    /// <summary>
    /// Saves each picked title, one request apiece: a new title as a new entry, one already on the list
    /// as its own entry moved to <see cref="UpNextCandidate.Target"/>. Returns the entries that saved.
    /// Not cancellable: by now the user has asked for these.
    /// </summary>
    internal async Task<IReadOnlyList<MediaListEntry>> AddAsync(UpNextChoice choice, MediaKind kind)
    {
        var saved = new List<MediaListEntry>();
        var anyMoved = false;
        UpNextCandidate? last = null;
        foreach (var candidate in choice.Selected)
        {
            var target = candidate.Target;

            // A move saves the viewer's whole entry, read with the relations, so its score, notes and
            // repeat count go back as they were. The status side effects are the same ones a move from
            // the Library applies: a rewatch starts from zero (AniList counts it when completed). This
            // rewrites the candidate's own entry, which is why the candidate keeps its From and Target
            // apart from it (reading the entry once made a toast say "moved to Watching" for a rewatch).
            var entry = candidate.Existing ?? new MediaListEntry { MediaId = candidate.Media.Id };

            // The relations query's entry has no media, so give the shared mutation the totals it reads:
            // without them a title moved to Watching at its total stays there and +1 is dead (PR #224).
            entry.Media ??= new Media
            {
                Id = candidate.Media.Id,
                Type = candidate.Media.Type,
                Episodes = candidate.Media.Episodes,
                Chapters = candidate.Media.Chapters,
                Volumes = candidate.Media.Volumes,
            };
            ListEntryStatusMutations.ApplyStatusChange(entry, target);

            // A finished title queued again is a plan to rewatch, so it starts from zero rather than
            // sitting in Plan to Watch at 28/28 (agreed 2026-11-20). Done here rather than in the shared
            // mutation, which a Library move to Planning also goes through (#222 owns that one). A
            // paused or dropped title keeps its place.
            if (candidate.From == MediaListStatus.Completed && target == MediaListStatus.Planning)
            {
                entry.Progress = 0;
                entry.ProgressVolumes = 0;
            }

            try
            {
                var result = await aniListClient.SaveMediaListEntryAsync(entry);
                if (result is not { Id: > 0 })
                {
                    throw new InvalidOperationException("SaveMediaListEntry returned no entry id for a related title.");
                }

                entry.Id = result.Id;
                saved.Add(entry);
                last = candidate;
                anyMoved |= !candidate.IsNew;
                SentrySdk.AddBreadcrumb(
                    $"Up next {(candidate.IsNew ? "added" : "moved")} (media {entry.MediaId} → {target})", "list", "user");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "UPNEXT failed to save media {MediaId}", candidate.Media.Id);
                await feedback.ShowFailureSnackbarAsync(
                    ex, $"Couldn't {(candidate.IsNew ? "add" : "move")} {candidate.Media.DisplayTitle}. Please try again.", retryAction: null);
            }
        }

        if (saved.Count == 1 && last is { } only)
        {
            var listName = MediaListVocabulary.StatusLabel(only.Target, kind);
            await feedback.ShowToastAsync(only.IsNew
                ? $"{only.Media.DisplayTitle} added to {listName}"
                : $"{only.Media.DisplayTitle} moved to {listName}");
        }
        else if (saved.Count > 1)
        {
            await feedback.ShowToastAsync(anyMoved ? $"{saved.Count} titles updated" : $"{saved.Count} titles added");
        }

        return saved;
    }

    internal void LogFailure(Exception ex, int mediaId)
        => logger.LogError(ex, "UPNEXT offer failed for media {MediaId}", mediaId);

    /// <summary>The sheet's answer; 0 picked is Not now or a dismissal.</summary>
    internal void LogAnswer(int mediaId, int picked, int offered)
        => logger.LogInformation("UPNEXT media {MediaId}: picked {Picked} of {Offered}", mediaId, picked, offered);
}

/// <summary>
/// One completion flow's offer. Created before the flow's popups so the relations fetch can run
/// while the rating popup is open (<see cref="Start"/> is called once the completion is confirmed), and
/// disposed when the flow ends, which cancels a fetch nobody is going to use.
/// </summary>
public sealed class UpNextScope : IDisposable
{
    private readonly UpNextOffer _owner;
    private readonly int _mediaId;
    private readonly string _title;
    private readonly MediaKind _kind;
    private readonly MediaListStatus _settingsStatus;
    private readonly CancellationTokenSource _cts = new();
    private Task<IReadOnlyList<UpNextCandidate>>? _candidates;
    private bool _disposed;

    internal UpNextScope(
        UpNextOffer owner, int mediaId, string title, MediaKind kind, MediaListStatus settingsStatus)
    {
        _owner = owner;
        _mediaId = mediaId;
        _title = title;
        _kind = kind;
        _settingsStatus = settingsStatus;
    }

    /// <summary>Starts the relations fetch if it hasn't started. Safe to call more than once.</summary>
    public void Start()
    {
        if (_disposed)
        {
            return;
        }

        _candidates ??= _owner.FetchCandidatesAsync(_mediaId, _kind, _settingsStatus, _cts.Token);
    }

    /// <summary>
    /// Makes the offer, after the completion has saved, and returns what was added. Never throws, other
    /// than what <paramref name="confirmSaved"/> throws.
    /// <para>
    /// <paramref name="confirmSaved"/> is the caller's "saved" toast. It runs once no sheet is up, and
    /// only when nothing was picked: an Android toast draws over every window, so one shown just
    /// before the sheet sat on its buttons (seen on device, #103). So it runs straight away when there
    /// is nothing to offer, and after the sheet closes on Not now. When titles were picked, their own
    /// toast (or failure snackbar) is the word on how it went.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<MediaListEntry>> OfferAsync(Func<Task>? confirmSaved = null)
    {
        // Never throws: FetchCandidatesAsync turns every failure into nothing to offer.
        var candidates = await CandidatesAsync();
        IReadOnlyList<MediaListEntry> added = [];
        var picked = false;
        if (candidates.Count > 0)
        {
            try
            {
                // A state breadcrumb, not a user one: the sheet is new UI, and a crash inside it should
                // show it was open. Not now gets no breadcrumb (confirmed actions only); each save does.
                SentrySdk.AddBreadcrumb($"Up next sheet shown (media {_mediaId}, {candidates.Count} titles)", "list", "state");
                var choice = await _owner.Dialogs.ShowUpNextAsync(_title, candidates);
                _owner.LogAnswer(_mediaId, choice?.Selected.Count ?? 0, candidates.Count);
                if (choice is { Selected.Count: > 0 })
                {
                    picked = true;
                    added = await _owner.AddAsync(choice, _kind);
                }
            }
            catch (Exception ex)
            {
                _owner.LogFailure(ex, _mediaId);
            }
        }

        if (!picked && confirmSaved is not null)
        {
            await confirmSaved();
        }

        return added;
    }

    /// <summary>
    /// <see cref="OfferAsync"/> for a flow that may have no offer (switched off, or not a completion):
    /// then <paramref name="confirmSaved"/> simply runs.
    /// </summary>
    public static async Task<IReadOnlyList<MediaListEntry>> ConfirmSavedThenOfferAsync(UpNextScope? scope, Func<Task> confirmSaved)
    {
        if (scope is null)
        {
            await confirmSaved();
            return [];
        }

        return await scope.OfferAsync(confirmSaved);
    }

    private Task<IReadOnlyList<UpNextCandidate>> CandidatesAsync()
    {
        Start();
        return _candidates ?? Task.FromResult<IReadOnlyList<UpNextCandidate>>([]);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts.Cancel();
        _cts.Dispose();
    }
}
