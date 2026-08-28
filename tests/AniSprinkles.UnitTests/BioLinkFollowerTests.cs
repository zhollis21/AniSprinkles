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
    [Theory]
    [InlineData("https://anilist.co/character/725", "character-details", "characterId", 725)]
    [InlineData("https://anilist.co/staff/96881", "staff-details", "staffId", 96881)]
    [InlineData("https://anilist.co/anime/21", "media-details", "mediaId", 21)]
    [InlineData("https://anilist.co/studio/18", "studio-details", "studioId", 18)]
    public async Task AnAniListEntity_NavigatesInApp(string url, string route, string key, int id)
    {
        var h = new Harness();

        await h.FollowAsync(url);

        await h.Navigation.Received(1).GoToAsync(
            route,
            false,
            Arg.Is<IDictionary<string, object>>(p => p.ContainsKey(key) && (int)p[key] == id));
        Assert.Empty(h.Browser.Opened);
        Assert.Empty(h.Feedback.Toasts);
    }

    [Fact]
    public async Task AMangaLink_ToastsInsteadOfNavigatingOrOpeningTheBrowser()
    {
        // The details page queries Media(type: ANIME), so a manga id 404s there. The reader gets the
        // same message DetailsPageModelBase already gives for a manga id from a relations carousel,
        // so the answer doesn't depend on where they tapped (#12).
        var h = new Harness();

        await h.FollowAsync("https://anilist.co/manga/30013");

        Assert.Equal([BioLinkFollower.UnsupportedMediaMessage], h.Feedback.Toasts);
        Assert.Empty(h.Browser.Opened);
        await h.Navigation.DidNotReceiveWithAnyArgs().GoToAsync(default!, default, default);
    }

    [Theory]
    [InlineData("https://twitter.com/tsuda_ken")]
    [InlineData("https://en.wikipedia.org/wiki/Monkey_D._Luffy")]
    [InlineData("https://anilist.co/user/someone")]
    public async Task AnythingElse_GoesToTheBrowser(string url)
    {
        // 66 of 235 links sampled across the most-favourited characters and staff are agency,
        // social and personal sites. The browser is the right home for them.
        var h = new Harness();

        await h.FollowAsync(url);

        Assert.Equal(url, h.Browser.LastOpened?.ToString());
        Assert.Empty(h.Feedback.Toasts);
        await h.Navigation.DidNotReceiveWithAnyArgs().GoToAsync(default!, default, default);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    public async Task AnUnusableUrl_DoesNothingAtAll(string? url)
    {
        var h = new Harness();

        await h.FollowAsync(url);

        Assert.Empty(h.Browser.Opened);
        Assert.Empty(h.Feedback.Toasts);
        await h.Navigation.DidNotReceiveWithAnyArgs().GoToAsync(default!, default, default);
    }

    [Fact]
    public async Task WhenNavigationThrows_ItIsSwallowedAndLogged()
    {
        // The caller is a span click that discards the task, so an escaping exception would reach
        // nothing that could handle it.
        var h = new Harness();
        h.Navigation
            .GoToAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<IDictionary<string, object>>())
            .Returns(Task.FromException(new InvalidOperationException("no shell")));

        await h.FollowAsync("https://anilist.co/character/725");

        Assert.NotEmpty(h.Logger.Containing("Failed to follow bio link"));
    }

    private sealed class Harness
    {
        public INavigationService Navigation { get; } = Substitute.For<INavigationService>();

        public RecordingExternalBrowser Browser { get; } = new();

        public RecordingUserFeedback Feedback { get; } = new();

        public RecordingLogger<BioLinkFollowerTests> Logger { get; } = new();

        public Task FollowAsync(string? url)
            => BioLinkFollower.FollowAsync(url, Navigation, Browser, Feedback, Logger);
    }
}
