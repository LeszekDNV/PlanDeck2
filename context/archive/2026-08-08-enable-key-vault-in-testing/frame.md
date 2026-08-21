# Frame Brief: Enable Key Vault in Testing

> Framing step before /vdf-plan. This document captures what is *actually*
> at issue, separated from what was initially assumed.

## Reported Observation

In Azure Container Apps revision `plandeck-server--0000044`, Azure DevOps
connection validation succeeds with HTTP 200, but saving the PAT fails with:

`Unavailable: The project secret store is temporarily unavailable.`

The failure occurred four times between 18:55:22 and 18:56:48 CEST. The Key
Vault itself is operational, and the application identity has the
`Key Vault Secrets Officer` role.

## Initial Framing (preserved)

- **User's stated cause or approach**: Testing does not receive the Key Vault
  configuration because `AppHost.cs` omits `WithReference(keyVault)` for that
  publish target.
- **User's proposed direction**: Enable Key Vault for the Testing environment.
- **Pre-dispatch narrowing**: The leading concern is the missing Key Vault
  configuration in revision `plandeck-server--0000044`, observed specifically
  when persisting a validated Azure DevOps PAT.
- **Runtime configuration check**: The presence of
  `ConnectionStrings__key-vault` and
  `Aspire__Azure__Security__KeyVault__VaultUri` has not yet been checked on the
  active revision.

## Dimension Map

The observation could originate at any of these dimensions:

1. **Testing publish topology** - target classification may exclude the Key
   Vault resource and its server reference from the Aspire graph.
   This is the initial framing.
2. **Configuration propagation and dependency selection** - the server may not
   receive or recognize a vault URI and may therefore select the intentionally
   unavailable secret-store implementation.
3. **Runtime access to Key Vault** - a configured client could fail because of
   identity, RBAC, networking, or service availability.

## Hypothesis Investigation

| Hypothesis | Evidence | Verdict |
| --- | --- | --- |
| Testing publish topology excludes Key Vault | `isTestingPublishTarget` is true for the Testing target, while `AddAzureKeyVault`, `WithRoleAssignments`, `WithReference`, and `WaitFor` all run only under `if (!isTestingPublishTarget)` (`src/PlanDeck/Aspire/PlanDeck.AppHost/AppHost.cs:23-50`). Both deployment paths select Testing (`.github/workflows/azure-dev.yml:58`, `.azuredevops/pipelines/azure-dev.yml:104`). | **STRONG** |
| Missing configuration selects the unavailable store | The server recognizes only the `key-vault` connection string or Aspire vault URI (`src/PlanDeck/Web/PlanDeck.Server/Program.cs:14-22`). When neither exists, DI registers `UnavailableProjectSecretStore` (`src/PlanDeck/Web/PlanDeck.Server/Extensions/ServiceCollectionExtensions.cs:216-223`). Its `CreateAsync` always throws `ProjectSecretUnavailableException` (`src/PlanDeck/Core/PlanDeck.Infrastructure/AzureDevOps/UnavailableProjectSecretStore.cs:7-10`), which maps to the exact observed gRPC message (`src/PlanDeck/Core/PlanDeck.Application/Services/ProjectGrpcService.cs:738-749`). | **STRONG** |
| Configured Key Vault client fails at runtime | `KeyVaultProjectSecretStore` maps authentication failures and non-401/403 Azure SDK failures to the same unavailable exception (`src/PlanDeck/Core/PlanDeck.Infrastructure/AzureDevOps/KeyVaultProjectSecretStore.cs:17-34,168-180`). However, this path requires a recognized vault URI, which the Testing graph does not generate. The actual revision configuration was not inspected, so an inherited or manually supplied URI cannot be ruled out. | **WEAK** |

## Narrowing Signals

- Azure DevOps validation succeeds before secret persistence, isolating the
  observation from Azure DevOps connectivity and PAT validity.
- The exact user-visible message is the default mapping for
  `ProjectSecretUnavailableException`.
- The Testing graph contains no alternate Key Vault reference or environment
  variable.
- Four occurrences describe four save failures; they do not by themselves
  establish an intermittent runtime fault.
- The active revision's two recognized Key Vault settings remain unchecked, so
  repository evidence cannot prove which secret-store implementation was
  instantiated in that revision.

## Cross-System Convention

The original secure-endpoint contract required `AddAzureKeyVault("key-vault")`
outside the publish-only branch and a server reference in local and publish
modes (`context/archive/2026-07-21-secure-ado-grpc-endpoints/plan.md:442-462`).
The current Testing-only exclusion conflicts with that contract. A later Entra
deployment plan explicitly left moving Testing secrets to Key Vault out of
that change, but did not establish that Testing should permanently operate
without project secret persistence
(`context/archive/2026-07-29-testing-entra-deployment-readiness/plan.md:78-88`).

## Confirmed Problem Statement

> **The actual problem to plan around is**: The Testing publish topology breaks
> PlanDeck's project-secret-store contract by excluding the Key Vault resource
> and reference, causing the server to select an intentionally unavailable
> store and reject PAT persistence.

The initial framing was correct, with one important precision: the evidence
points to an environment orchestration and configuration-contract failure, not
to Azure DevOps connectivity or a proven Key Vault/RBAC outage. Restoring the
contract would allow Testing to instantiate the real secret store; the active
revision configuration must still be checked to rule out inherited settings
before implementation planning.

## Confidence

**MEDIUM** - source code, deployment target selection, the exact exception
mapping, and the prior architecture contract all support the same causal
chain. Confidence is not HIGH because the active revision has not been checked
for manually supplied or inherited Key Vault settings.

Before `/vdf-plan`, inspect revision `plandeck-server--0000044` for
`ConnectionStrings__key-vault` and
`Aspire__Azure__Security__KeyVault__VaultUri`. Their absence confirms the
leading chain; their presence requires investigating the configured client's
runtime access instead.

## What Changes for /vdf-plan

Plan around restoring and verifying the Testing environment's project-secret
store configuration contract. Do not broaden the plan into Azure DevOps,
secret-store business logic, or RBAC changes unless revision inspection
contradicts the leading hypothesis.

## References

- `src/PlanDeck/Aspire/PlanDeck.AppHost/AppHost.cs:23-50`
- `src/PlanDeck/Web/PlanDeck.Server/Program.cs:14-22`
- `src/PlanDeck/Web/PlanDeck.Server/Extensions/ServiceCollectionExtensions.cs:216-223`
- `src/PlanDeck/Core/PlanDeck.Infrastructure/AzureDevOps/UnavailableProjectSecretStore.cs:5-36`
- `src/PlanDeck/Core/PlanDeck.Infrastructure/AzureDevOps/KeyVaultProjectSecretStore.cs:17-34,168-180`
- `src/PlanDeck/Core/PlanDeck.Application/Services/ProjectGrpcService.cs:554-566,738-749`
- `context/archive/2026-07-21-secure-ado-grpc-endpoints/plan.md:442-462`
- `context/archive/2026-07-29-testing-entra-deployment-readiness/plan.md:78-88`
- Investigation tasks: `frame-testing-kv-topology`,
  `frame-testing-kv-config`, `frame-testing-kv-runtime`
