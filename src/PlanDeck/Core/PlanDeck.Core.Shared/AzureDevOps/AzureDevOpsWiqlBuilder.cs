namespace PlanDeck.Core.Shared.AzureDevOps;

public static class AzureDevOpsWiqlBuilder
{
    public static string? BuildWhereClause(
        IReadOnlyCollection<string> workItemTypes,
        IReadOnlyCollection<string> states)
    {
        var clauses = new List<string>();

        var typeClause = BuildInClause("System.WorkItemType", workItemTypes);
        if (typeClause is not null)
        {
            clauses.Add(typeClause);
        }

        var stateClause = BuildInClause("System.State", states);
        if (stateClause is not null)
        {
            clauses.Add(stateClause);
        }

        return clauses.Count == 0 ? null : string.Join(" AND ", clauses);
    }

    private static string? BuildInClause(string field, IReadOnlyCollection<string> values)
    {
        var quoted = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => $"'{value.Replace("'", "''")}'")
            .ToArray();

        return quoted.Length == 0
            ? null
            : $"[{field}] IN ({string.Join(", ", quoted)})";
    }
}
