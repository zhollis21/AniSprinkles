using System.Globalization;
using AniSprinkles.Converters;
using Microsoft.Maui.Graphics;

namespace AniSprinkles.UnitTests;

/// <summary>
/// #52 phase 3. These run without a MAUI application, so <c>Application.Current</c> is null and the
/// resource lookups fall through to their transparent default — which is exactly the branch worth
/// pinning, alongside the key mapping, the hash fallback, and the Brush-vs-Color target rule.
/// </summary>
public class RainbowAccentConverterTests
{
    private static readonly RainbowAccentConverter Converter = new();

    [Fact]
    public void ResourceKeyFor_IsStableForTheSameName()
    {
        // The bio-link spans (#137) colour each link by running its own text through this, so a
        // character's name has to land on the same palette entry every time — including across
        // runs, which is why the hash is FNV-1a rather than string.GetHashCode.
        Assert.Equal("RainbowOrange", RainbowAccentConverter.ResourceKeyFor("Shanks"));
        Assert.Equal(
            RainbowAccentConverter.ResourceKeyFor("Buggy the Clown"),
            RainbowAccentConverter.ResourceKeyFor("Buggy the Clown"));
    }

    [Fact]
    public void ResourceKeyFor_StillHonoursTheStatusColours()
        // Sharing the helper with the spans must not cost the status sections their fixed colours.
        => Assert.Equal("RainbowBlue", RainbowAccentConverter.ResourceKeyFor("Current"));

    [Fact]
    public void Convert_ReturnsABrushWhenTheTargetIsABrush()
    {
        // MAUI's Background (Brush) always wins over BackgroundColor, and the app's implicit Border
        // style sets Background — so a Border accent binds Background and the converter has to hand
        // back a Brush or the accent silently renders as the default card colour.
        var result = Converter.Convert("Watching", typeof(Brush), null, CultureInfo.InvariantCulture);

        Assert.IsAssignableFrom<Brush>(result);
    }

    [Fact]
    public void Convert_ReturnsAColourWhenTheTargetIsNotABrush()
    {
        var result = Converter.Convert("Watching", typeof(Color), null, CultureInfo.InvariantCulture);

        Assert.IsType<Color>(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Convert_WithNoKey_IsTransparentRatherThanThrowing(string? value)
    {
        var result = Converter.Convert(value, typeof(Color), null, CultureInfo.InvariantCulture);

        Assert.Equal(Colors.Transparent, result);
    }

    [Fact]
    public void Convert_IsDeterministicForTheSameKey()
    {
        // The hash fallback has to be stable across runs, or a section's colour would change every
        // time the app restarted. FNV-1a is used precisely because string.GetHashCode is randomised.
        var first = Converter.Convert("Some Custom List", typeof(Color), null, CultureInfo.InvariantCulture);
        var second = Converter.Convert("Some Custom List", typeof(Color), null, CultureInfo.InvariantCulture);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Convert_UsesTheParameterAsTheKeyWhenItIsNotABool()
    {
        // A non-bool parameter overrides the bound value as the colour key; a bool means "dim it".
        var byValue = Converter.Convert("Watching", typeof(Color), null, CultureInfo.InvariantCulture);
        var byParameter = Converter.Convert("ignored", typeof(Color), "Watching", CultureInfo.InvariantCulture);

        Assert.Equal(byValue, byParameter);
    }

    [Fact]
    public void ConvertBack_IsNotSupported()
        => Assert.Throws<NotSupportedException>(
            () => Converter.ConvertBack(Colors.Red, typeof(string), null, CultureInfo.InvariantCulture));
}

public class MediaFormatIconsTests
{
    [Theory]
    [InlineData("TV")]
    [InlineData("TV_SHORT")]
    [InlineData("MOVIE")]
    [InlineData("OVA")]
    [InlineData("ONA")]
    [InlineData("SPECIAL")]
    [InlineData("MUSIC")]
    [InlineData("MANGA")]
    [InlineData("NOVEL")]
    [InlineData("ONE_SHOT")]
    public void GlyphFor_EveryAniListFormat_ResolvesToAGlyph(string format)
        => Assert.False(string.IsNullOrEmpty(MediaFormatIcons.GlyphFor(format)));

    [Fact]
    public void GlyphFor_TvAndTvShort_ShareTheSameGlyph()
        => Assert.Equal(MediaFormatIcons.GlyphFor("TV"), MediaFormatIcons.GlyphFor("TV_SHORT"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("tv")]          // AniList sends upper-case enums; a lower-case match would be a bug
    [InlineData("SOMETHING_NEW")]
    public void GlyphFor_AnythingUnknown_IsNullSoTheBadgeHides(string? format)
        => Assert.Null(MediaFormatIcons.GlyphFor(format));
}

public class StringNotNullOrEmptyConverterTests
{
    private static readonly StringNotNullOrEmptyConverter Converter = new();

    [Theory]
    [InlineData("text", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    // Despite the name, this treats whitespace as absent too (IsNullOrWhiteSpace, not
    // IsNullOrEmpty). That is the right behaviour for a visibility binding — a label holding a
    // single space should not reserve layout — but the name does not say so, hence this case.
    [InlineData("   ", false)]
    public void Convert_MapsPresenceToVisibility(string? value, bool expected)
        => Assert.Equal(expected, Converter.Convert(value, typeof(bool), null, CultureInfo.InvariantCulture));

    [Fact]
    public void Convert_ForANonString_IsFalse()
        => Assert.Equal(false, Converter.Convert(42, typeof(bool), null, CultureInfo.InvariantCulture));
}
