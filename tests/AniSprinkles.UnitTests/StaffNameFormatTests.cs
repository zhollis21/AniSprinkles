namespace AniSprinkles.UnitTests;

/// <summary>
/// Staff Name Language actually changing how names render (#130). It was saved to AniList and read
/// by nothing — both accessors preferred <c>Full</c> unconditionally, so every staff and character
/// name rendered identically whatever the setting said.
/// <para>
/// The answer is <c>userPreferred</c>, which AniList resolves server-side against that setting.
/// Composing the name locally from <c>first</c>/<c>last</c> was tried and abandoned: measured on a
/// real account set to ROMAJI, <c>full</c> flips a Latin-script name ("Bairstow Caitlyn") while
/// <c>userPreferred</c> leaves it alone ("Caitlyn Bairstow"). Reproducing that split locally means
/// guessing at a CJK heuristic; deferring means the app and the website cannot disagree.
/// </para>
/// </summary>
public class StaffNameFormatTests
{
    [Test]
    public void PrefersUserPreferred_WhichCarriesTheViewersSetting()
    {
        var name = new CharacterName
        {
            Full = "Taiki Kawakami",
            UserPreferred = "Kawakami Taiki",
            Native = "川上泰樹",
        };

        Assert.That(new Staff { Name = name }.DisplayName, Is.EqualTo("Kawakami Taiki"));
    }

    [Test]
    public void CharactersFollowTheSameRuleAsStaff()
    {
        var name = new CharacterName { Full = "Monkey D. Luffy", UserPreferred = "Luffy Monkey D." };

        Assert.That(new Character { Name = name }.DisplayName, Is.EqualTo("Luffy Monkey D."));
    }

    [Test]
    public void CardLevelNamesFollowItToo()
    {
        // StaffNode and VoiceActor render on the media-details and appearance carousels. They bound
        // straight to Name.Full, so without their own accessor those surfaces would have kept
        // ignoring the setting even once the entity-level ones honoured it.
        var name = new CharacterName { Full = "Mayumi Tanaka", UserPreferred = "Tanaka Mayumi" };

        Assert.That(new StaffNode { Name = name }.DisplayName, Is.EqualTo("Tanaka Mayumi"));
        Assert.That(new VoiceActor { Name = name }.DisplayName, Is.EqualTo("Tanaka Mayumi"));
    }

    [Test]
    public void LatinNamesAreLeftAloneBecauseAniListLeavesThemAlone()
    {
        // The measured case that decided the design: under ROMAJI, full is flipped but userPreferred
        // is not. Preferring full here would render "Bairstow Caitlyn" where the website shows
        // "Caitlyn Bairstow".
        var name = new CharacterName
        {
            Full = "Bairstow Caitlyn",
            UserPreferred = "Caitlyn Bairstow",
            Native = null,
        };

        Assert.That(new VoiceActor { Name = name }.DisplayName, Is.EqualTo("Caitlyn Bairstow"));
    }

    [Test]
    public void FallsBackToFullWhenUserPreferredIsMissing()
    {
        // Nine of the eleven name selections did not request userPreferred before this change, so a
        // cached or partially-populated name must not render blank.
        var name = new CharacterName { Full = "Hiroshi Kamiya", Native = "神谷浩史" };

        Assert.That(new Staff { Name = name }.DisplayName, Is.EqualTo("Hiroshi Kamiya"));
    }

    [Test]
    public void FallsBackToNativeWhenThatIsAllThereIs()
    {
        var name = new CharacterName { Native = "神谷浩史" };

        Assert.That(new Staff { Name = name }.DisplayName, Is.EqualTo("神谷浩史"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void BlankUserPreferred_DoesNotWinOverAUsableFull(string? userPreferred)
    {
        var name = new CharacterName { Full = "Hans Zimmer", UserPreferred = userPreferred };

        Assert.That(new Staff { Name = name }.DisplayName, Is.EqualTo("Hans Zimmer"));
    }

    [Test]
    public void NullName_FallsBackToUnknown()
    {
        Assert.That(new Staff().DisplayName, Is.EqualTo("Unknown"));
        Assert.That(new Character().DisplayName, Is.EqualTo("Unknown"));
    }

    [Test]
    public void EntirelyBlankName_FallsBackToUnknown()
    {
        Assert.That(new Staff { Name = new CharacterName() }.DisplayName, Is.EqualTo("Unknown"));
    }

    // ── The native subtitle under the hero ────────────────────────────────────────────────────────

    [Test]
    public void NativeSubtitle_IsSuppressedWhenItWouldRepeatTheHero()
    {
        // With the setting on Native, userPreferred IS the native name, so the hero and the subtitle
        // beneath it would print the same thing twice.
        var name = new CharacterName { Full = "Taiki Kawakami", UserPreferred = "川上泰樹", Native = "川上泰樹" };

        Assert.That(new Staff { Name = name }.ShowNativeName, Is.False);
    }

    [Test]
    public void NativeSubtitle_ShowsWhenItAddsSomething()
    {
        var name = new CharacterName { Full = "Taiki Kawakami", UserPreferred = "Kawakami Taiki", Native = "川上泰樹" };

        Assert.That(new Staff { Name = name }.ShowNativeName, Is.True);
    }

    [Test]
    public void NativeSubtitle_HiddenWhenThereIsNoNativeName()
    {
        var name = new CharacterName { Full = "Caitlyn Bairstow", UserPreferred = "Caitlyn Bairstow" };

        Assert.That(new Character { Name = name }.ShowNativeName, Is.False);
    }
}
