namespace PlanDeck.Application.Abstractions;

public interface IProjectSecretStore
{
    Task<string> CreateAsync(string value, CancellationToken cancellationToken);

    Task<string> GetLatestAsync(string secretName, CancellationToken cancellationToken);

    Task RotateAsync(
        string secretName,
        string value,
        CancellationToken cancellationToken);

    Task SoftDeleteAsync(string secretName, CancellationToken cancellationToken);

    void Invalidate(string secretName);
}

public abstract class ProjectSecretStoreException(string message) : Exception(message);

public sealed class ProjectSecretMissingException()
    : ProjectSecretStoreException("The project secret does not exist.");

public sealed class ProjectSecretForbiddenException()
    : ProjectSecretStoreException("The project secret cannot be accessed.");

public sealed class ProjectSecretUnavailableException()
    : ProjectSecretStoreException("The project secret store is unavailable.");
