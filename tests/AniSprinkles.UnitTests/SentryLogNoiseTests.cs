using AniSprinkles.Services;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.UnitTests;

public class SentryLogNoiseTests
{
    // The frames MAUI 11.0.10 logged on device for #230, trimmed to what the filter reads.
    private const string ImageSourceStack =
        "   at Microsoft.Maui.Handlers.ViewHandler`2.get_PlatformView()\n"
        + "   at Microsoft.Maui.Handlers.ButtonHandler.ButtonImageSourcePartSetter.SetImageSource(Drawable platformImage)\n"
        + "   at Microsoft.Maui.Platform.ImageSourcePartLoader.UpdateImageSourceAsync()\n"
        + "   at Microsoft.Maui.TaskExtensions.FireAndForget(Task task, Action`1 errorCallback)";

    private const string PlatformViewNull = "PlatformView cannot be null here";

    [Test]
    public void ImageSourceLoadAfterDisconnect_IsFiltered()
    {
        Assert.That(
            SentryLogNoise.IsImageSourceLoadAfterDisconnect(
                "Microsoft.Maui.Handlers.IButtonHandler", typeof(InvalidOperationException), PlatformViewNull, ImageSourceStack),
            Is.True);
    }

    [TestCase("AniSprinkles.PageModels.MediaDetailsPageModel", typeof(InvalidOperationException), PlatformViewNull, ImageSourceStack, TestName = "Keeps our own categories")]
    [TestCase("Microsoft.Maui.Handlers.IButtonHandler", typeof(ArgumentException), PlatformViewNull, ImageSourceStack, TestName = "Keeps another exception type")]
    [TestCase("Microsoft.Maui.Handlers.IButtonHandler", typeof(InvalidOperationException), "Something else", ImageSourceStack, TestName = "Keeps another message")]
    [TestCase("Microsoft.Maui.Handlers.ILabelHandler", typeof(InvalidOperationException), PlatformViewNull, "   at Microsoft.Maui.Handlers.LabelHandler.MapText()", TestName = "Keeps a disconnected handler outside the image loader")]
    [TestCase("Microsoft.Maui.Handlers.IButtonHandler", typeof(InvalidOperationException), PlatformViewNull, null, TestName = "Keeps an exception with no stack")]
    [TestCase("Microsoft.Maui.Handlers.IButtonHandler", null, null, null, TestName = "Keeps a log with no exception")]
    public void OtherLogs_AreKept(string category, Type? exceptionType, string? message, string? stackTrace)
    {
        Assert.That(SentryLogNoise.IsImageSourceLoadAfterDisconnect(category, exceptionType, message, stackTrace), Is.False);
    }

    [Test]
    public void Filter_KeepsAnErrorWithNoException()
    {
        Assert.That(SentryLogNoise.Filter("Microsoft.Maui.Handlers.IButtonHandler", LogLevel.Error, default, null), Is.False);
    }
}
