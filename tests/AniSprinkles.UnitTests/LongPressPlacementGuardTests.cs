using System.Xml;
using System.Xml.Linq;

namespace AniSprinkles.UnitTests;

// Guards where a LongPressGestureRecognizer may sit (#171). On Android, MAUI 11.0.10 gives every view
// with recognizers one GestureDetector, and only turns its long-press detection on when a long press
// is present. That one detector is what keeps a long press from also being a tap:
//
//  - Long press and tap on the SAME element share the detector, and Android's detector never reports
//    a tap at finger-up after a long press. That is the whole reason the app no longer needs a
//    tap-suppression timer.
//  - A long press on a different element from the tap gets its own detector. The tap's detector then
//    has long press off and reports a tap on release after any hold, so the card would open its
//    action sheet AND navigate (or, for Library's +1, increment progress).
//  - A long press that is the only recognizer on a view also fires after a plain tap on 11.0.10
//    (dotnet/maui#40180).
//
// So every GestureRecognizers collection holding a long press must hold a tap too. Parsed as XML
// rather than grepped, so a comment that mentions a recognizer does not count.
public class LongPressPlacementGuardTests
{
    [TestCaseSource(typeof(XamlFileValidationTests), nameof(XamlFileValidationTests.AllXamlFiles))]
    public void XamlFile_PairsEveryLongPressWithATapOnTheSameElement(string relativePath)
    {
        var fullPath = Path.Combine(XamlFileValidationTests.RepoRoot.Value, relativePath);
        var offenders = FindUnpairedLongPresses(XDocument.Load(fullPath, LoadOptions.SetLineInfo), relativePath);

        Assert.That(offenders, Is.Empty,
            "A LongPressGestureRecognizer must share its GestureRecognizers with a TapGestureRecognizer, "
            + "or Android reports a tap after the long press:" + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    [Test]
    public void TheAppHasLongPressesToGuard()
    {
        // Without this, deleting every long press would leave the per-file test green and guarding nothing.
        var count = XamlFileValidationTests.AllXamlFiles()
            .Select(args => XDocument.Load(Path.Combine(XamlFileValidationTests.RepoRoot.Value, (string)args[0])))
            .Sum(document => document.Descendants().Count(e => e.Name.LocalName == "LongPressGestureRecognizer"));

        Assert.That(count, Is.GreaterThan(0));
    }

    // The detector itself, against each shape it must catch and each it must leave alone.
    [TestCase("""<Border><Border.GestureRecognizers><TapGestureRecognizer /><LongPressGestureRecognizer /></Border.GestureRecognizers></Border>""", 0, TestName = "Allows long press beside a tap")]
    [TestCase("""<Border><Border.GestureRecognizers><LongPressGestureRecognizer /><TapGestureRecognizer /></Border.GestureRecognizers></Border>""", 0, TestName = "Allows either order")]
    [TestCase("""<Border><Border.GestureRecognizers><LongPressGestureRecognizer /></Border.GestureRecognizers></Border>""", 1, TestName = "Flags a lone long press")]
    [TestCase("""<Border><Border.GestureRecognizers><TapGestureRecognizer /></Border.GestureRecognizers><Grid><Grid.GestureRecognizers><LongPressGestureRecognizer /></Grid.GestureRecognizers></Grid></Border>""", 1, TestName = "Flags a long press on a child of the tapped element")]
    [TestCase("""<Border><Border.GestureRecognizers><SwipeGestureRecognizer /><LongPressGestureRecognizer /></Border.GestureRecognizers></Border>""", 1, TestName = "Flags a long press paired with a non-tap recognizer")]
    [TestCase("""<Border><Border.GestureRecognizers><TapGestureRecognizer /></Border.GestureRecognizers></Border>""", 0, TestName = "Ignores a tap on its own")]
    [TestCase("""<!-- <LongPressGestureRecognizer /> --><Border />""", 0, TestName = "Ignores comments")]
    public void FindUnpairedLongPresses_DetectsEachShape(string fragment, int expected)
    {
        var xaml = $"""<Root xmlns="http://schemas.microsoft.com/dotnet/2021/maui">{fragment}</Root>""";
        var document = XDocument.Parse(xaml, LoadOptions.SetLineInfo);

        Assert.That(FindUnpairedLongPresses(document, "fragment.xaml"), Has.Count.EqualTo(expected));
    }

    internal static List<string> FindUnpairedLongPresses(XDocument document, string relativePath)
    {
        var offenders = new List<string>();
        foreach (var longPress in document.Descendants().Where(e => e.Name.LocalName == "LongPressGestureRecognizer"))
        {
            var hasSiblingTap = longPress.Parent?.Elements().Any(e => e.Name.LocalName == "TapGestureRecognizer") == true;
            if (!hasSiblingTap)
            {
                offenders.Add($"{relativePath}:{((IXmlLineInfo)longPress).LineNumber}");
            }
        }

        return offenders;
    }
}
