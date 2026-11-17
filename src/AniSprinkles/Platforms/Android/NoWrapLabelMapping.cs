using Microsoft.Maui.Handlers;

namespace AniSprinkles.Platforms.Android;

/// <summary>
/// Makes <see cref="LineBreakMode.NoWrap"/> actually stop a label wrapping on Android (#193).
/// </summary>
/// <remarks>
/// MAUI maps <c>NoWrap</c> to a one-line limit only, and Android still word-wraps under that limit:
/// it lays the text out over several lines and shows the first. That is harmless while a label is
/// measured wide enough, but the chip rows are measured a pixel or two short for some strings.
/// "Blood type F" on Luffy's character page got 199 px when it needs about 201, so "F" wrapped
/// onto the hidden second line and the chip read "Blood type". Turning on horizontal scrolling
/// takes the width limit off the text layout, so a short measure clips at most the edge of the
/// last glyph instead of dropping the whole last word.
///
/// Registered under its own key, which runs when a label's handler connects. Appended to the
/// <c>LineBreakMode</c> key instead, it made no difference on device (checked on Luffy's page,
/// MAUI 11.0.10). A label's <c>LineBreakMode</c> is therefore read once, at connect. That covers
/// every chip, which gets it from <c>ChipLabelStyle</c>, but a later change to <c>LineBreakMode</c>
/// would not reach this.
///
/// Only <c>NoWrap</c> labels are touched. Every other label keeps whatever MAUI set, so wrapping
/// and the truncation modes behave exactly as before.
/// </remarks>
internal static class NoWrapLabelMapping
{
    private const string Key = "AniSprinklesNoWrap";

    public static void Register()
        => LabelHandler.Mapper.AppendToMapping(Key, (handler, view) =>
        {
            if (view is Label { LineBreakMode: LineBreakMode.NoWrap })
            {
                handler.PlatformView.SetHorizontallyScrolling(true);
            }
        });
}
