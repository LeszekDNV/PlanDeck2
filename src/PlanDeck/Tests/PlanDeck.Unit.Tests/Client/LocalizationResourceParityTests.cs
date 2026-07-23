using System.Xml.Linq;

namespace PlanDeck.Unit.Tests.Client;

[TestFixture]
public class LocalizationResourceParityTests
{
    [Test]
    public void SharedResource_EnglishAndPolish_HaveSameKeys()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", ".."));
        var enPath = Path.Combine(repoRoot, "Web", "PlanDeck.Client", "Resources", "SharedResource.resx");
        var plPath = Path.Combine(repoRoot, "Web", "PlanDeck.Client", "Resources", "SharedResource.pl.resx");

        var enKeys = ReadResourceKeys(enPath);
        var plKeys = ReadResourceKeys(plPath);

        Assert.Multiple(() =>
        {
            Assert.That(enKeys.Except(plKeys), Is.Empty, "Keys present in EN but missing in PL.");
            Assert.That(plKeys.Except(enKeys), Is.Empty, "Keys present in PL but missing in EN.");
        });
    }

    private static HashSet<string> ReadResourceKeys(string path)
    {
        var doc = XDocument.Load(path);
        return doc.Root!
            .Elements("data")
            .Select(element => element.Attribute("name")?.Value)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToHashSet(StringComparer.Ordinal);
    }
}
