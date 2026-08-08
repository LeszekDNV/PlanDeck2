# Enable Key Vault in Testing Implementation Plan

## Overview

Restore the project secret-store contract in the Azure Testing publish topology. The
AppHost will include the existing Aspire Key Vault resource and server reference for
Testing, and both active GitHub deployment workflows will reject a final Container App
revision that does not contain the expected Key Vault binding.

The change is complete only when the repository contract is verified, the existing
`rg-test` infrastructure is updated without replacing the vault, and a project Owner can
save a valid sandbox Azure DevOps PAT through the public Testing application.

## Current State Analysis

The active Testing revision `plandeck-server--0000044` is provisioned and healthy but
contains neither `ConnectionStrings__key-vault` nor
`Aspire__Azure__Security__KeyVault__VaultUri`. This confirms the frame brief's causal
chain: Testing omits the Aspire resource reference, the server detects no vault
configuration, and DI selects the intentionally unavailable secret store.

The infrastructure required to restore the contract already exists in `rg-test`:

- Key Vault `keyvault-ade7omipejs3a` exists in `polandcentral`.
- Azure RBAC, soft delete, and purge protection are enabled.
- The `plandeck-server` user-assigned identity has `Key Vault Secrets Officer` at the
  vault scope.
- The active GitHub workflows for `main` and `develop` already share one readiness
  action and deploy to the same serialized Testing environment.

The missing protection is deployment validation. The current readiness action verifies
revision state and public `/health`, but `/health` does not exercise the project secret
store, so a revision without the Key Vault binding can pass.

## Desired End State

Testing, Production, local development, and local test hosts all use the same logical
Aspire resource name, `key-vault`. A Testing publish manifest contains the Key Vault
resource, the least-privilege role-assignment module, and the
`ConnectionStrings__key-vault` server environment binding.

After a Testing deployment, the shared readiness action captures the final immutable
revision, verifies it is healthy and running, confirms that revision contains the
required binding name without reading or logging its value, and then performs the
existing public `/health` check. Both active GitHub workflows use this contract.

A project Owner can validate and save a sandbox Azure DevOps PAT in Testing. The save
creates the project connection successfully rather than returning
`Unavailable: The project secret store is temporarily unavailable.`

### Key Discoveries:

- Testing alone excludes the entire Key Vault resource/reference block
  (`src/PlanDeck/Aspire/PlanDeck.AppHost/AppHost.cs:23-50`).
- The server already registers the Aspire Key Vault client when
  `ConnectionStrings__key-vault` is present
  (`src/PlanDeck/Web/PlanDeck.Server/Program.cs:14-22`).
- DI already selects `KeyVaultProjectSecretStore` when that configuration is available
  (`src/PlanDeck/Web/PlanDeck.Server/Extensions/ServiceCollectionExtensions.cs:185-223`).
- A current Production manifest generated from AppHost emits
  `ConnectionStrings__key-vault`, `KEY_VAULT_URI`, the `key-vault` resource, and the
  server-to-vault role-assignment module. `ConnectionStrings__key-vault` is therefore
  the exact final-revision contract to guard.
- Both active Testing workflows invoke the same readiness action after `azd deploy`
  (`.github/workflows/azure-dev.yml:189-200`,
  `.github/workflows/azure-develop.yml:185-196`).
- Azure Container Apps creates a new revision when environment variables change, and
  the current azd image-based flow may create one revision during provision and another
  during deploy. Validation must inspect the revision captured after the final deploy.

## What We're NOT Doing

- Changing `ProjectGrpcService`, `KeyVaultProjectSecretStore`, exception mapping, or
  server DI selection.
- Adding a Key Vault startup health check or making general application readiness
  depend on the project secret store.
- Creating a second Testing vault, renaming the existing logical `key-vault` resource,
  or changing the existing managed identity.
- Broadening or reducing the existing `Key Vault Secrets Officer` role in this change.
- Reading, printing, or persisting Key Vault binding values in workflow logs.
- Running automated E2E tests against deployed `rg-test`; browser E2E remains local.
- Updating or reactivating the legacy Azure Pipelines deployment.
- Adding automatic traffic rollback, deleting old revisions, or changing database
  migrations.
- Modifying archived plans to rewrite historical scope decisions.

## Implementation Approach

Make the AppHost resource graph environment-consistent by declaring and referencing the
same `key-vault` resource regardless of publish target. Preserve the current protection
settings, cleared default assignments, explicit Secrets Officer role, reference, and
startup dependency.

Extend the existing shared Container App readiness action rather than introducing a
second deployment action. Give it a required environment-variable-name input, pass
`ConnectionStrings__key-vault` from both active workflows, and validate the captured
final revision's environment-variable names only. Keep the current revision lifecycle
and public health checks unchanged.

Apply the repository change first, then preview the Testing infrastructure update.
Provision and deploy only after the preview shows reuse of the existing vault and
identity. Finish with Azure configuration checks and one manual sandbox PAT
create/remove journey through the public Testing UI.

## Critical Implementation Details

### Timing & lifecycle

`azd provision` applies the resource and environment-variable configuration, while
`azd deploy` applies the image and can create a second revision. The binding check must
run against the single `latestRevisionName` captured after `azd deploy`, as the existing
readiness action already does, not against a provision-time revision.

### Debug & observability

The deployment gate may report the Container App name, revision name, required binding
name, and pass/fail result. It must not print the binding value, the full environment
array, the full revision JSON, PAT values, or secret names.

## Phase 1: Restore and Guard the Testing Key Vault Contract

### Overview

Restore the Aspire topology contract and make its absence a deployment failure for both
active Testing workflows.

### Changes Required:

#### 1. Environment-consistent Aspire Key Vault topology

**File**: `src/PlanDeck/Aspire/PlanDeck.AppHost/AppHost.cs`

**Intent**: Include the existing Key Vault resource and server relationship in Testing
instead of excluding the complete block for that publish target.

**Contract**: Declare logical resource `key-vault` for every AppHost mode. Preserve
`ClearDefaultRoleAssignments`, soft delete, purge protection,
`KeyVaultSecretsOfficer`, `WithReference(keyVault)`, and `WaitFor(keyVault)`. The
Testing-only branch remains responsible only for Testing-specific server environment
configuration.

#### 2. Final-revision binding validation

**Files**:

- `.github/actions/wait-for-container-app-readiness/action.yml`
- `.github/actions/wait-for-container-app-readiness/wait-for-container-app-readiness.ps1`

**Intent**: Fail a deployment whose final healthy revision does not contain the Key
Vault binding, even when the generic public health endpoint succeeds.

**Contract**: Add a required input and PowerShell parameter for one required environment
variable name. After the captured revision reaches the accepted ready states and before
public health succeeds, inspect
`properties.template.containers[0].env[*].name` from that exact revision. Require an
ordinal exact match. Report only the expected name and revision in diagnostics; do not
serialize values or the full environment collection.

#### 3. Active Testing deployment workflows

**Files**:

- `.github/workflows/azure-dev.yml`
- `.github/workflows/azure-develop.yml`

**Intent**: Apply one identical Key Vault binding success definition to both active
routes into the shared Testing environment.

**Contract**: Pass `ConnectionStrings__key-vault` to the new readiness-action input in
both workflows. Preserve triggers, GitHub Environment, concurrency, provisioning,
migrations, deployment order, timeouts, and health path.

#### 4. Testing deployment runbook

**File**: `src/PlanDeck/AZURE_DEPLOYMENT.md`

**Intent**: Document the restored Testing dependency and safe operator checks instead
of stating that Testing does not provision Key Vault.

**Contract**: Add the existing Testing vault and application role to the environment
inventory, describe the final-revision binding gate, and update the Key Vault incident
check. Commands and examples may display resource names, binding names, and RBAC roles
only; they must not display environment values, PATs, or secret values.

### Success Criteria:

#### Automated Verification:

- PowerShell parses the readiness script without syntax errors.
- Both active workflow definitions pass `ConnectionStrings__key-vault` to the same
  shared readiness action.
- A Testing publish manifest contains the `key-vault` resource, server role-assignment
  dependency, and `ConnectionStrings__key-vault` environment binding.
- Whole solution builds from `src/PlanDeck`: `dotnet build PlanDeck.slnx`.
- Repository diff passes whitespace validation: `git diff --check`.

#### Manual Verification:

- Manifest review confirms Testing reuses logical resource `key-vault`, retains purge
  protection and explicit least-privilege assignment, and contains no secret values.
- Runbook review confirms operators can diagnose a missing binding without dumping
  revision environment values.

**Implementation Note**: After repository verification passes, pause for review of the
Testing manifest and sanitized diagnostics before any Azure provisioning.

---

## Phase 2: Preview, Deploy, and Prove PAT Persistence

### Overview

Apply the guarded topology to `rg-test`, prove the final runtime contract, and exercise
the original failing user journey with a sandbox credential.

### Changes Required:

#### 1. Testing infrastructure preview

**External systems**: Azure Developer CLI environment `test`, subscription
`Visual Studio Professional Subscription`, resource group `rg-test`

**Intent**: Prove the AppHost change reconciles the existing protected vault and role
assignment instead of replacing or deleting infrastructure.

**Contract**: Select the existing `test` azd environment and run
`azd provision --preview --no-prompt`. Review the generated change set for the existing
Key Vault, application identity, role assignment, and Container App configuration.
Stop if the preview deletes/replaces the vault, disables protection, broadens RBAC, or
targets a different subscription/resource group.

#### 2. Guarded Testing deployment

**External systems**: GitHub Actions Testing environment, Azure Container Apps
`plandeck-server`

**Intent**: Produce a final healthy revision that is rejected automatically unless it
contains the Key Vault binding.

**Contract**: Run one of the active Testing workflows after both workflow definitions
land. The shared action captures the post-deploy final revision, verifies the exact
binding name, then completes the existing revision-state and public `/health` gates.
The other workflow receives the same behavior through the shared action and identical
input.

#### 3. Azure resource and RBAC verification

**External resources**:

- Key Vault `keyvault-ade7omipejs3a`
- Managed identity `plandeck_server_identity-ade7omipejs3a`
- Final `plandeck-server` revision

**Intent**: Confirm the deployed configuration still satisfies the security and
availability assumptions used by the application.

**Contract**: Verify the final revision contains the binding name, the managed identity
is assigned to the app, the identity retains `Key Vault Secrets Officer` at the vault
scope, Azure RBAC remains enabled, and soft delete plus purge protection remain enabled.
Do not query secret values.

#### 4. Sandbox PAT persistence smoke test and cleanup

**External systems**: Public Testing application, sandbox Azure DevOps project, Testing
Key Vault

**Intent**: Prove the original validation-then-save journey reaches the real secret
store and completes successfully.

**Contract**: As a project Owner, configure an Azure DevOps connection using a valid,
least-privilege sandbox PAT. Require validation and save to succeed and the project
connection status to become valid. Remove the connection through the application after
verification so its generated test secret enters the existing soft-delete flow. Never
record the PAT or generated secret name in plan artifacts or logs.

### Success Criteria:

#### Automated Verification:

- `azd provision --preview --no-prompt` succeeds for the existing `test` environment
  without vault replacement, deletion, protection downgrade, or RBAC broadening.
- The selected Testing workflow passes provisioning, deployment, final-revision binding,
  revision readiness, and public `/health` gates.
- The final revision exposes the name `ConnectionStrings__key-vault` and does not expose
  its value in workflow output.

#### Manual Verification:

- Azure confirms the existing vault, managed identity, Secrets Officer assignment,
  Azure RBAC, soft delete, and purge protection remain intact.
- A valid sandbox PAT validates and saves successfully in the public Testing UI without
  the project-secret-store unavailable error.
- The saved connection can be read back as valid without displaying the PAT.
- Removing the smoke-test connection completes through the application and no
  credential value appears in repository, workflow, or captured diagnostic output.

**Implementation Note**: A failed preview or deployment gate stops the phase. Do not
patch the Container App environment manually or bypass the binding check to complete the
smoke test.

---

## Testing Strategy

### Unit Tests:

- No application unit-test changes are required because secret-store behavior and
  selection logic are unchanged.
- Parse the modified PowerShell script through the PowerShell AST parser.
- Statically verify both workflow call sites provide the exact required binding name.

### Integration Tests:

- Generate the Testing publish manifest and assert the Key Vault resource, server
  reference, role-assignment dependency, and exact binding name are present.
- Keep existing local Aspire integration coverage that waits for `key-vault` and
  resolves its connection string
  (`src/PlanDeck/Tests/PlanDeck.Integration.Tests/AspireAppFixture.cs:27-49`).
- Keep the opt-in real-vault lifecycle test unchanged; it validates the secret-store
  implementation but does not replace the deployed Testing smoke test
  (`src/PlanDeck/Tests/PlanDeck.Integration.Tests/AzureDevOps/RealKeyVaultProjectSecretStoreTests.cs:11-110`).
- Use the real shared GitHub action against the final Testing revision. Do not add
  automated browser E2E against `rg-test`.

### Manual Testing Steps:

1. Review the Testing publish manifest for `key-vault`,
   `plandeck-server-roles-key-vault`, and `ConnectionStrings__key-vault`.
2. Preview provisioning in the existing `test` environment and confirm the current
   vault and identity are reconciled in place.
3. Run an active Testing workflow and record its workflow URL plus verified revision
   name, but no environment values.
4. Confirm the final revision contains the expected binding name and the shared action
   completed `/health`.
5. Confirm the identity, vault-scoped role, RBAC mode, soft delete, and purge protection.
6. As a project Owner, validate and save a sandbox Azure DevOps PAT.
7. Reload the project and confirm the connection remains valid without exposing the
   PAT.
8. Remove the connection through the application and verify no unavailable-store error
   occurs.

## Performance Considerations

The AppHost adds no new runtime service: Testing reuses its existing Key Vault and
managed identity. The deployment gate scans a small in-memory environment-variable list
once on the already captured final revision, adding negligible time and no extra
application traffic. The plan intentionally avoids a Key Vault call on every `/health`
request.

## Migration Notes

There is no database or secret migration. The existing Testing vault remains in place;
the change restores its generated reference to new Container App revisions.

Rollback means reverting the AppHost and workflow changes and redeploying a reviewed
prior application configuration. Do not delete the purge-protected vault. If the manual
smoke test created a project connection, remove it through the application before
rollback so the test secret follows normal soft deletion. Container App traffic rollback
does not alter Key Vault contents or database state.

## References

- Frame brief: `context/changes/enable-key-vault-in-testing/frame.md`
- Original all-environments Key Vault contract:
  `context/archive/2026-07-21-secure-ado-grpc-endpoints/plan.md:442-462`
- Prior Testing scope boundary:
  `context/archive/2026-07-29-testing-entra-deployment-readiness/plan.md:78-93`
- AppHost topology:
  `src/PlanDeck/Aspire/PlanDeck.AppHost/AppHost.cs:23-50`
- Server Key Vault configuration detection:
  `src/PlanDeck/Web/PlanDeck.Server/Program.cs:14-22`
- Secret-store DI selection:
  `src/PlanDeck/Web/PlanDeck.Server/Extensions/ServiceCollectionExtensions.cs:185-223`
- Shared revision gate:
  `.github/actions/wait-for-container-app-readiness/wait-for-container-app-readiness.ps1:92-283`
- Azure Container Apps environment variables:
  `https://learn.microsoft.com/azure/container-apps/environment-variables`
- Azure Developer CLI Container Apps workflow:
  `https://learn.microsoft.com/azure/developer/azure-developer-cli/container-apps-workflows`
- Azure Container Apps managed identity:
  `https://learn.microsoft.com/azure/container-apps/managed-identity`
- Azure Key Vault RBAC:
  `https://learn.microsoft.com/azure/key-vault/general/rbac-guide`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Restore and Guard the Testing Key Vault Contract

#### Automated

- [x] 1.1 PowerShell parses the readiness script without syntax errors
- [x] 1.2 Both active workflows pass the exact Key Vault binding name
- [x] 1.3 Testing publish manifest contains the Key Vault resource, role dependency, and binding
- [x] 1.4 Whole solution builds
- [x] 1.5 Repository diff passes whitespace validation

#### Manual

- [x] 1.6 Testing manifest preserves protection, least privilege, and secret-free output
- [x] 1.7 Runbook diagnostics expose binding names but no values

### Phase 2: Preview, Deploy, and Prove PAT Persistence

#### Automated

- [ ] 2.1 Testing infrastructure preview succeeds without destructive or broader RBAC changes
- [ ] 2.2 Testing workflow passes binding, revision, and public health gates
- [ ] 2.3 Final revision contains the Key Vault binding name without logging its value

#### Manual

- [ ] 2.4 Existing vault, identity, RBAC, soft delete, and purge protection remain intact
- [ ] 2.5 Sandbox PAT validates and saves without the unavailable-store error
- [ ] 2.6 Saved connection remains valid without exposing the PAT
- [ ] 2.7 Smoke-test connection cleanup completes without credential exposure
