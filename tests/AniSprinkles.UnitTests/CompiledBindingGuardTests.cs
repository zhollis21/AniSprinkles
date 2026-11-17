using System.Xml;
using System.Xml.Linq;

namespace AniSprinkles.UnitTests;

// Guards against bindings that quietly fall back to reflection (#173). A reflection binding is a
// silent blank at runtime when the property it names is renamed, rather than a build error, and it
// is not trim-safe. Two shapes produce one in this app:
//
//  - x:DataType="{x:Null}", which switches compiled bindings off for an element and everything
//    under it. XC0023 flags this too, now that it is no longer suppressed, but only as a warning.
//  - A path through BindingContext — {Binding BindingContext.X, Source={x:Reference RootView}} and
//    the like. BindingContext is typed object, so the source generator cannot follow the path and
//    emits a reflection Binding with no diagnostic at all. Bind to the page model directly instead:
//    {Binding X, Source={RelativeSource AncestorType={x:Type pageModels:SomePageModel}}}.
//
// Parsed as XML rather than grepped as text, so a comment that mentions either shape does not trip it.
public class CompiledBindingGuardTests
{
    private const string XamlNamespace = "http://schemas.microsoft.com/winfx/2009/xaml";

    [TestCaseSource(typeof(XamlFileValidationTests), nameof(XamlFileValidationTests.AllXamlFiles))]
    public void XamlFile_HasNoReflectionBindings(string relativePath)
    {
        var fullPath = Path.Combine(XamlFileValidationTests.RepoRoot.Value, relativePath);
        var offenders = FindReflectionBindings(XDocument.Load(fullPath, LoadOptions.SetLineInfo), relativePath);

        Assert.That(offenders, Is.Empty,
            "These bindings fall back to reflection. Bind to the page model through "
            + "RelativeSource AncestorType, or declare an x:DataType:" + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    // The detector itself, against each shape it must catch and each it must leave alone. The
    // per-file test above only ever sees the shapes the app happens to contain, so without these the
    // element-form branch would never have been seen to fire.
    [TestCase("""<Label x:DataType="{x:Null}" />""", 1, TestName = "Flags x:Null data type")]
    [TestCase("""<Label x:DataType="{x:Null }" />""", 1, TestName = "Flags x:Null data type with inner space")]
    [TestCase("""<Label Text="{Binding BindingContext.Title, Source={x:Reference Root}}" />""", 1, TestName = "Flags BindingContext path, positional")]
    [TestCase("""<Label Text="{Binding Source={x:Reference Root}, Path=BindingContext.Title}" />""", 1, TestName = "Flags BindingContext path, named")]
    [TestCase("""<Label><Label.Text><Binding Path="BindingContext.Title" /></Label.Text></Label>""", 1, TestName = "Flags BindingContext path, element syntax")]
    [TestCase("""<Label x:DataType="{x:Null}" Text="{Binding BindingContext.Title}" />""", 2, TestName = "Flags both shapes on one element")]
    [TestCase("""<Label Text="{Binding Title, Source={RelativeSource AncestorType={x:Type x:String}}}" />""", 0, TestName = "Allows AncestorType binding")]
    [TestCase("""<Label Text="{Binding Source='Feed', x:DataType=x:String}" />""", 0, TestName = "Allows constant source with data type")]
    [TestCase("""<Label x:DataType="x:String" Text="{Binding .}" />""", 0, TestName = "Allows a declared data type")]
    [TestCase("""<!-- {Binding BindingContext.Title} x:DataType="{x:Null}" --><Label />""", 0, TestName = "Ignores comments")]
    [TestCase("""<Label Text="BindingContext.Title" />""", 0, TestName = "Ignores plain text that is not a binding")]
    public void FindReflectionBindings_DetectsEachShape(string fragment, int expected)
    {
        var xaml = $"""<Root xmlns="http://schemas.microsoft.com/dotnet/2021/maui" xmlns:x="{XamlNamespace}">{fragment}</Root>""";
        var document = XDocument.Parse(xaml, LoadOptions.SetLineInfo);

        Assert.That(FindReflectionBindings(document, "fragment.xaml"), Has.Count.EqualTo(expected));
    }

    internal static List<string> FindReflectionBindings(XDocument document, string relativePath)
    {
        var offenders = new List<string>();
        foreach (var element in document.Descendants())
        {
            foreach (var attribute in element.Attributes())
            {
                if (IsNullDataType(attribute) || IsBindingContextPath(element, attribute))
                {
                    var line = ((IXmlLineInfo)attribute).LineNumber;
                    offenders.Add($"{relativePath}:{line}: {attribute.Name.LocalName}=\"{attribute.Value}\"");
                }
            }
        }

        return offenders;
    }

    private static bool IsNullDataType(XAttribute attribute)
        => attribute.Name == XName.Get("DataType", XamlNamespace)
            && attribute.Value.Replace(" ", "", StringComparison.Ordinal) == "{x:Null}";

    private static bool IsBindingContextPath(XElement element, XAttribute attribute)
    {
        // Markup-extension form: Command="{Binding BindingContext.X, ...}" or "{Binding Path=BindingContext.X}".
        if (attribute.Value.Contains("{Binding", StringComparison.Ordinal)
            && attribute.Value.Contains("BindingContext.", StringComparison.Ordinal))
        {
            return true;
        }

        // Element form: <Binding Path="BindingContext.X" />.
        return element.Name.LocalName == "Binding"
            && attribute.Name.LocalName == "Path"
            && attribute.Value.StartsWith("BindingContext.", StringComparison.Ordinal);
    }
}
