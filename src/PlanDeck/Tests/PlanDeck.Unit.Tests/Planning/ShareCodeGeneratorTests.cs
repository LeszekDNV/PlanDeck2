using PlanDeck.Application.Planning;

namespace PlanDeck.Unit.Tests.Planning;

[TestFixture]
public sealed class ShareCodeGeneratorTests
{
    private const string AllowedAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    private readonly ShareCodeGenerator _generator = new();

    [Test]
    public void Generate_ProducesTenCharacterCode()
    {
        var code = _generator.Generate();

        Assert.That(code, Has.Length.EqualTo(10));
    }

    [Test]
    public void Generate_UsesOnlyUnambiguousAlphabet()
    {
        for (var i = 0; i < 1000; i++)
        {
            var code = _generator.Generate();
            Assert.That(code.All(AllowedAlphabet.Contains), Is.True,
                $"Code '{code}' contains a character outside the allowed alphabet.");
        }
    }

    [Test]
    public void Generate_DoesNotContainAmbiguousCharacters()
    {
        for (var i = 0; i < 1000; i++)
        {
            var code = _generator.Generate();
            Assert.That(code.IndexOfAny(['I', 'L', 'O', 'U', 'i', 'l', 'o', 'u']), Is.EqualTo(-1),
                $"Code '{code}' contains an ambiguous character.");
        }
    }

    [Test]
    public void Generate_ProducesUniqueCodesAcrossManyDraws()
    {
        var codes = new HashSet<string>();
        for (var i = 0; i < 10_000; i++)
        {
            codes.Add(_generator.Generate());
        }

        Assert.That(codes, Has.Count.EqualTo(10_000));
    }
}
