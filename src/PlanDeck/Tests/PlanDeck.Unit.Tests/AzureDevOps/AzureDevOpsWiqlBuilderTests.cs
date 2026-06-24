using PlanDeck.Core.Shared.AzureDevOps;

namespace PlanDeck.Unit.Tests.AzureDevOps;

[TestFixture]
public sealed class AzureDevOpsWiqlBuilderTests
{
    [Test]
    public void BuildWhereClause_WithNoFilters_ReturnsNull()
    {
        var result = AzureDevOpsWiqlBuilder.BuildWhereClause([], []);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void BuildWhereClause_WithOnlyWhitespaceValues_ReturnsNull()
    {
        var result = AzureDevOpsWiqlBuilder.BuildWhereClause(["", "  "], [" "]);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void BuildWhereClause_WithTypesOnly_BuildsSingleInClause()
    {
        var result = AzureDevOpsWiqlBuilder.BuildWhereClause(["Bug", "Task"], []);

        Assert.That(result, Is.EqualTo("[System.WorkItemType] IN ('Bug', 'Task')"));
    }

    [Test]
    public void BuildWhereClause_WithStatesOnly_BuildsSingleInClause()
    {
        var result = AzureDevOpsWiqlBuilder.BuildWhereClause([], ["Active"]);

        Assert.That(result, Is.EqualTo("[System.State] IN ('Active')"));
    }

    [Test]
    public void BuildWhereClause_WithBothDimensions_JoinsWithAnd()
    {
        var result = AzureDevOpsWiqlBuilder.BuildWhereClause(["Bug"], ["Active", "New"]);

        Assert.That(result, Is.EqualTo(
            "[System.WorkItemType] IN ('Bug') AND [System.State] IN ('Active', 'New')"));
    }

    [Test]
    public void BuildWhereClause_EscapesSingleQuotesInValues()
    {
        var result = AzureDevOpsWiqlBuilder.BuildWhereClause(["O'Brien"], []);

        Assert.That(result, Is.EqualTo("[System.WorkItemType] IN ('O''Brien')"));
    }

    [Test]
    public void BuildWhereClause_SkipsBlankValuesWithinADimension()
    {
        var result = AzureDevOpsWiqlBuilder.BuildWhereClause(["Bug", "  "], []);

        Assert.That(result, Is.EqualTo("[System.WorkItemType] IN ('Bug')"));
    }
}
