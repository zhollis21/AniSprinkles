using AniSprinkles.Services.Abstractions;
using AniSprinkles.UnitTests.Fakes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AniSprinkles.UnitTests;

/// <summary>
/// #103's settings are device-local: they must reach <see cref="IPreferences"/> on the tap, and must
/// not look like unsaved AniList changes, because the AniList save has no field to carry them.
/// </summary>
[NonParallelizable]
public class SettingsUpNextTests
{
    public SettingsUpNextTests() => TestDataBuilder.ResetAppSettings();

    [Test]
    public void LoadsWhatIsStored()
    {
        var preferences = new FakePreferences();
        UpNextPreferences.SetEnabled(preferences, false);
        UpNextPreferences.SetDefaultStatus(preferences, MediaListStatus.Planning);

        var model = Create(preferences);

        Assert.That(model.UpNextEnabled, Is.False);
        Assert.That(model.UpNextStatus, Is.EqualTo("Planning"));
    }

    [Test]
    public void Section_StartsOpen_AndCollapses()
    {
        var model = Create(new FakePreferences());

        Assert.That(model.IsUpNextExpanded, Is.True);
        model.ToggleUpNextCommand.Execute(null);
        Assert.That(model.IsUpNextExpanded, Is.False);
    }

    [Test]
    public void Changes_AreWrittenImmediately_AndAreNotUnsavedAniListChanges()
    {
        var preferences = new FakePreferences();
        var model = Create(preferences);

        model.UpNextEnabled = false;
        model.SetUpNextStatusCommand.Execute("Planning");

        Assert.That(UpNextPreferences.IsEnabled(preferences), Is.False);
        Assert.That(UpNextPreferences.DefaultStatus(preferences), Is.EqualTo(MediaListStatus.Planning));
        Assert.That(model.HasUnsavedChanges, Is.False);
    }

    [TestCase("Dropped")]
    [TestCase("3")]
    [TestCase("Current, Planning")]
    public void OnlyWatchingOrPlanningIsAccepted(string value)
    {
        var preferences = new FakePreferences();
        var model = Create(preferences);

        model.SetUpNextStatusCommand.Execute(value);

        Assert.That(model.UpNextStatus, Is.EqualTo("Current"));
        Assert.That(preferences.ContainsKey(UpNextPreferences.StatusKey), Is.False);
    }

    [Test]
    public void Changes_AreLogged_NotOnlyBreadcrumbed()
    {
        // Breadcrumbs only reach Sentry with a crash; the device log is what a "why didn't it offer
        // anything" report is read from (2026-11-20).
        var logger = new RecordingLogger<SettingsPageModel>();
        var model = Create(new FakePreferences(), logger);

        model.UpNextEnabled = false;
        model.SetUpNextStatusCommand.Execute("Planning");

        Assert.That(logger.Containing("UPNEXT suggestions off"), Has.Count.EqualTo(1));
        Assert.That(logger.Containing("UPNEXT suggestions go to Planning"), Has.Count.EqualTo(1));
    }

    [Test]
    public void LoadingTheStoredValues_LogsNothing()
    {
        var logger = new RecordingLogger<SettingsPageModel>();
        var preferences = new FakePreferences();
        UpNextPreferences.SetEnabled(preferences, true);

        Create(preferences, logger);

        Assert.That(logger.Containing("UPNEXT"), Is.Empty);
    }

    private static SettingsPageModel Create(IPreferences preferences, ILogger<SettingsPageModel>? logger = null) => new(
        Substitute.For<IAuthService>(),
        Substitute.For<IAniListClient>(),
        Substitute.For<IAiringNotificationService>(),
        new ErrorReportService(NullLogger<ErrorReportService>.Instance),
        preferences,
        new ImmediateDispatcher(),
        Substitute.For<IAppInfo>(),
        new StubAppEnvironment(),
        new ScriptedDialogService(),
        new RecordingUserFeedback(),
        Substitute.For<IExternalBrowser>(),
        logger ?? NullLogger<SettingsPageModel>.Instance);
}
