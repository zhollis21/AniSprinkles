using AniSprinkles.Services.Abstractions;
using AniSprinkles.UnitTests.Fakes;
using NSubstitute;

namespace AniSprinkles.UnitTests;

/// <summary>
/// #137. What a tap on a bio link does. This was originally inline in the Android span, where none
/// of it could be tested — including the manga branch, which is a deliberate product decision rather
/// than a technical constraint. The span keeps only the parts that need a device: finding the links,
/// painting them, and routing the touch.
/// </summary>
public class BioLinkFollowerTests
{
    [TestCase("https://anilist.co/character/725", "character-details", "characterId", 725)]
    [TestCase("https://anilist.co/staff/96881", "staff-details", "staffId", 96881)]
    [TestCase("https://anilist.co/anime/21", "media-details", "mediaId", 21)]
    [TestCase("https://anilist.co/studio/18", "studio-details", "studioId", 18)]
    public async Task AnAniListEntity_NavigatesInApp(string url, string route, string key, int id)
    {
        var h = new Harness();

        await h.FollowAsync(url);

        await h.Navigation.Received(1).GoToAsync(
            route,
            false,
            Arg.Is<IDictionary<string, object>>(p => p.ContainsKey(key) && (int)p[key] == id));
        Assert.That(h.Browser.Opened, Is.Empty);
        Assert.That(h.Feedback.Toasts, Is.Empty);
    }

    [Test]
    public async Task AMangaLink_NavigatesToTheDetailsPage()
    {
        // Toasted "not supported yet" until #12, because the details page pinned
        // Media(id:, type: ANIME) and a manga id 404'd there. 7 of the 235 bio links sampled across
        // the most-favourited characters and staff point at anilist.co/manga, so this was a real
        // dead end rather than a theoretical one.
        var h = new Harness();

        await h.FollowAsync("https://anilist.co/manga/30013");

        Assert.That(h.Feedback.Toasts, Is.Empty);
        Assert.That(h.Browser.Opened, Is.Empty);
        await h.Navigation.Received(1).GoToAsync(
            "media-details",
            false,
            Arg.Is<IDictionary<string, object>>(p => p.ContainsKey("mediaId") && (int)p["mediaId"] == 30013));
    }

    [TestCase("https://twitter.com/tsuda_ken")]
    [TestCase("https://en.wikipedia.org/wiki/Monkey_D._Luffy")]
    [TestCase("https://anilist.co/user/someone")]
    public async Task AnythingElse_GoesToTheBrowser(string url)
    {
        // 66 of 235 links sampled across the most-favourited characters and staff are agency,
        // social and personal sites. The browser is the right home for them.
        var h = new Harness();

        await h.FollowAsync(url);

        Assert.That(h.Browser.LastOpened?.ToString(), Is.EqualTo(url));
        Assert.That(h.Feedback.Toasts, Is.Empty);
        await h.Navigation.DidNotReceiveWithAnyArgs().GoToAsync(default!, default, default);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("not a url")]
    public async Task AnUnusableUrl_DoesNothingAtAll(string? url)
    {
        var h = new Harness();

        await h.FollowAsync(url);

        Assert.That(h.Browser.Opened, Is.Empty);
        Assert.That(h.Feedback.Toasts, Is.Empty);
        await h.Navigation.DidNotReceiveWithAnyArgs().GoToAsync(default!, default, default);
    }

    [Test]
    public async Task WhenNavigationThrows_ItIsSwallowedAndLogged()
    {
        // The caller is a span click that discards the task, so an escaping exception would reach
        // nothing that could handle it.
        var h = new Harness();
        h.Navigation
            .GoToAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<IDictionary<string, object>>())
            .Returns(Task.FromException(new InvalidOperationException("no shell")));

        await h.FollowAsync("https://anilist.co/character/725");

        Assert.That(h.Logger.Containing("Failed to follow bio link"), Is.Not.Empty);
    }

    private sealed class Harness
    {
        public INavigationService Navigation { get; } = Substitute.For<INavigationService>();

        public RecordingExternalBrowser Browser { get; } = new();

        public RecordingUserFeedback Feedback { get; } = new();

        public RecordingLogger<BioLinkFollowerTests> Logger { get; } = new();

        public Task FollowAsync(string? url)
            => BioLinkFollower.FollowAsync(url, Navigation, Browser, Logger);
    }
}
