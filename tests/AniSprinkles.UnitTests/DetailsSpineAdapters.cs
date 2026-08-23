using AniSprinkles.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AniSprinkles.UnitTests;

// The four adapters that run DetailsSpineTests against each details page model. Each supplies only
// what its page does differently: how its entity is built, which client call fetches it, and how its
// public load entry point is shaped. Everything asserted lives in DetailsSpineTests.
//
// Each adapter routes its fetch through a single mutable responder so Returns / Throws /
// CapturesToken all reconfigure the same stub — NSubstitute's last-configured-wins would otherwise
// make the "fail, then succeed on retry" tests order-sensitive.

public class CharacterDetailsSpineTests : DetailsSpineTests<Character>
{
    private Func<CancellationToken, Task<Character?>> _respond = _ => Task.FromResult<Character?>(null);

    protected override string ErrorContext => "Load character details";

    protected override Harness CreateHarness()
    {
        var harness = new Harness(h => new CharacterDetailsPageModel(
            h.Client, h.Auth, h.Navigation, h.Feedback, h.Browser, h.ErrorReports,
            NullLogger<CharacterDetailsPageModel>.Instance));

        harness.Client
            .GetCharacterAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                harness.Fetches++;
                return _respond(ci.Arg<CancellationToken>());
            });

        return harness;
    }

    protected override Character NewEntity(int id, string? siteUrl = "https://anilist.co/x/1", int? favourites = null)
        => new() { Id = id, SiteUrl = siteUrl, Favourites = favourites };

    protected override void Returns(Harness harness, Character? entity)
        => _respond = _ => Task.FromResult(entity);

    protected override void Throws(Harness harness, Exception exception)
        => _respond = _ => Task.FromException<Character?>(exception);

    protected override void CapturesToken(Harness harness, Action<CancellationToken> capture)
        => _respond = token =>
        {
            capture(token);
            return Task.FromResult<Character?>(new Character { Id = 42 });
        };

    protected override void ReturnsWhenSignalled(Harness harness, Task gate)
        => _respond = async token =>
        {
            await gate.WaitAsync(token);
            return new Character { Id = 42 };
        };

    protected override Task LoadAsync(Harness harness, int id)
        => ((CharacterDetailsPageModel)harness.Model).LoadAsync(id);

    protected override int FetchCount(Harness harness) => harness.Fetches;

    protected override bool HasEntity(Harness harness) => ((CharacterDetailsPageModel)harness.Model).HasCharacter;
}

public class StaffDetailsSpineTests : DetailsSpineTests<Staff>
{
    private Func<CancellationToken, Task<Staff?>> _respond = _ => Task.FromResult<Staff?>(null);

    protected override string ErrorContext => "Load staff details";

    protected override Harness CreateHarness()
    {
        var harness = new Harness(h => new StaffDetailsPageModel(
            h.Client, h.Auth, h.Navigation, h.Feedback, h.Browser, h.ErrorReports,
            NullLogger<StaffDetailsPageModel>.Instance));

        harness.Client
            .GetStaffAsync(
                Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                harness.Fetches++;
                return _respond(ci.Arg<CancellationToken>());
            });

        return harness;
    }

    protected override Staff NewEntity(int id, string? siteUrl = "https://anilist.co/x/1", int? favourites = null)
        => new() { Id = id, SiteUrl = siteUrl, Favourites = favourites };

    protected override void Returns(Harness harness, Staff? entity)
        => _respond = _ => Task.FromResult(entity);

    protected override void Throws(Harness harness, Exception exception)
        => _respond = _ => Task.FromException<Staff?>(exception);

    protected override void CapturesToken(Harness harness, Action<CancellationToken> capture)
        => _respond = token =>
        {
            capture(token);
            return Task.FromResult<Staff?>(new Staff { Id = 42 });
        };

    protected override void ReturnsWhenSignalled(Harness harness, Task gate)
        => _respond = async token =>
        {
            await gate.WaitAsync(token);
            return new Staff { Id = 42 };
        };

    protected override Task LoadAsync(Harness harness, int id)
        => ((StaffDetailsPageModel)harness.Model).LoadAsync(id);

    protected override int FetchCount(Harness harness) => harness.Fetches;

    protected override bool HasEntity(Harness harness) => ((StaffDetailsPageModel)harness.Model).HasStaff;
}

public class StudioDetailsSpineTests : DetailsSpineTests<Studio>
{
    private Func<CancellationToken, Task<Studio?>> _respond = _ => Task.FromResult<Studio?>(null);

    protected override string ErrorContext => "Load studio details";

    protected override Harness CreateHarness()
    {
        var harness = new Harness(h => new StudioDetailsPageModel(
            h.Client, h.Auth, h.Navigation, h.Feedback, h.Browser, h.ErrorReports,
            NullLogger<StudioDetailsPageModel>.Instance));

        harness.Client
            .GetStudioAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                harness.Fetches++;
                return _respond(ci.Arg<CancellationToken>());
            });

        return harness;
    }

    protected override Studio NewEntity(int id, string? siteUrl = "https://anilist.co/x/1", int? favourites = null)
        => new() { Id = id, SiteUrl = siteUrl, Favourites = favourites };

    protected override void Returns(Harness harness, Studio? entity)
        => _respond = _ => Task.FromResult(entity);

    protected override void Throws(Harness harness, Exception exception)
        => _respond = _ => Task.FromException<Studio?>(exception);

    protected override void CapturesToken(Harness harness, Action<CancellationToken> capture)
        => _respond = token =>
        {
            capture(token);
            return Task.FromResult<Studio?>(new Studio { Id = 42 });
        };

    protected override void ReturnsWhenSignalled(Harness harness, Task gate)
        => _respond = async token =>
        {
            await gate.WaitAsync(token);
            return new Studio { Id = 42 };
        };

    protected override Task LoadAsync(Harness harness, int id)
        => ((StudioDetailsPageModel)harness.Model).LoadAsync(id);

    protected override int FetchCount(Harness harness) => harness.Fetches;

    protected override bool HasEntity(Harness harness) => ((StudioDetailsPageModel)harness.Model).HasStudio;
}

public class MediaDetailsSpineTests : DetailsSpineTests<Media>
{
    private Func<CancellationToken, Task<(Media?, MediaListEntry?)>> _respond =
        _ => Task.FromResult<(Media?, MediaListEntry?)>((null, null));

    protected override string ErrorContext => "Load media details";

    // An empty result here means the query came back without a title rather than 404'd, which a retry
    // can fix — unlike the other three, where a missing entity is final.
    protected override bool NullResultIsRetryable => true;

    // This page's load is the heavy one and its list-entry merge is order-sensitive, so a second load
    // is dropped at the in-flight guard rather than superseding the first.
    protected override bool SupersedesConcurrentLoads => false;

    protected override Harness CreateHarness()
    {
        var harness = new Harness(h =>
        {
            var dialogs = new ScriptedDialogService();
            return new MediaDetailsPageModel(
                h.Client, h.Auth, h.ErrorReports, h.Navigation, h.Feedback, h.Browser,
                dialogs, new ListEntryStatusFlow(dialogs),
                NullLogger<MediaDetailsPageModel>.Instance);
        });

        harness.Client
            .GetMediaAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                harness.Fetches++;
                return _respond(ci.Arg<CancellationToken>());
            });

        return harness;
    }

    protected override Media NewEntity(int id, string? siteUrl = "https://anilist.co/x/1", int? favourites = null)
        => new() { Id = id, SiteUrl = siteUrl, Favourites = favourites };

    protected override void Returns(Harness harness, Media? entity)
        => _respond = _ => Task.FromResult<(Media?, MediaListEntry?)>((entity, null));

    protected override void Throws(Harness harness, Exception exception)
        => _respond = _ => Task.FromException<(Media?, MediaListEntry?)>(exception);

    protected override void CapturesToken(Harness harness, Action<CancellationToken> capture)
        => _respond = token =>
        {
            capture(token);
            return Task.FromResult<(Media?, MediaListEntry?)>((new Media { Id = 42 }, null));
        };

    protected override void ReturnsWhenSignalled(Harness harness, Task gate)
        => _respond = async token =>
        {
            await gate.WaitAsync(token);
            return ((Media?)new Media { Id = 42 }, (MediaListEntry?)null);
        };

    protected override Task LoadAsync(Harness harness, int id)
        => ((MediaDetailsPageModel)harness.Model).LoadAsync(id, listEntry: null);

    protected override int FetchCount(Harness harness) => harness.Fetches;

    protected override bool HasEntity(Harness harness) => ((MediaDetailsPageModel)harness.Model).HasMedia;
}
