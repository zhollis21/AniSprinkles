using IconFont.Maui.FluentIcons;

namespace AniSprinkles.Services;

// The icon glyph lives in its own partial so the rest of AniListApiException stays free of the
// MAUI-only IconFont package and can be link-compiled into the (net10.0) unit-test project.
public partial class AniListApiException
{
    /// <summary>
    /// Returns the Fluent icon glyph appropriate for this error kind.
    /// </summary>
    public string IconGlyph => Kind switch
    {
        ApiErrorKind.ServiceOutage => FluentIconsRegular.CloudDismiss24,
        ApiErrorKind.Network => FluentIconsRegular.WifiOff24,
        ApiErrorKind.Authentication => FluentIconsRegular.LockClosed24,
        ApiErrorKind.RateLimited => FluentIconsRegular.Clock24,
        _ => FluentIconsRegular.ErrorCircle24,
    };
}
