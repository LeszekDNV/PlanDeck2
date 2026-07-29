# Testing Entra Deployment Readiness Implementation Plan

## Overview

Complete the Testing deployment contract for Microsoft account sign-in. A dedicated
Entra web application and GitHub Environment will supply the three settings already
required by AppHost, while shared GitHub composite actions will reject incomplete
inputs before provisioning and reject an unhealthy final Azure Container Apps
revision after deployment.

## Current State Analysis

Both active GitHub deployment workflows target the same Testing environment and use
the same concurrency group, but they expose only the OIDC identity used by the
pipeline itself. They do not expose the separate tenant ID, client ID, and client
secret required by the user-facing Microsoft authentication handler
(`.github/workflows/azure-dev.yml:34-50`,
`.github/workflows/azure-develop.yml:38-54`).

AppHost already reads `AZURE_ENTRA_TENANT_ID`, `AZURE_ENTRA_CLIENT_ID`, and
`AZURE_ENTRA_CLIENT_SECRET`, maps them to the server configuration, and marks
Microsoft authentication as required for every published target
(`src/PlanDeck/Aspire/PlanDeck.AppHost/AppHost.cs:53-103`). The server then validates
that contract during startup (`src/PlanDeck/Web/PlanDeck.Server/Identity/MicrosoftAuthenticationOptions.cs:19-34`).
No application logic change is needed.

Each workflow provisions infrastructure, deploys the image, and stops after
`azd deploy`. A successful command therefore does not prove that the final revision
started or that public ingress serves a ready application. PlanDeck already maps
`/health` after registering a SQL DbContext check, so the required public readiness
surface exists (`src/PlanDeck/Web/PlanDeck.Server/Extensions/ServiceCollectionExtensions.cs:56-57`,
`src/PlanDeck/Aspire/PlanDeck.ServiceDefaults/Extensions.cs:100-121`,
`src/PlanDeck/Web/PlanDeck.Server/Program.cs:148-150`).

The repository already uses a composite GitHub action backed by a bounded PowerShell
retry script for Azure SQL readiness
(`.github/actions/wait-for-azure-sql/action.yml:1-30`,
`.github/actions/wait-for-azure-sql/wait-for-azure-sql.ps1:1-116`). This is the
established pattern for sharing deployment gates between the two workflows.

## Desired End State

The GitHub Environment named `Testing` contains the dedicated Entra application's
tenant ID and client ID as variables and its credential as a secret. Both Testing
workflows bind to that environment, validate all three inputs before any Azure
provisioning, and forward them under the existing `AZURE_ENTRA_*` names.

After `azd deploy`, each workflow identifies the final `plandeck-server` revision,
waits until Azure reports it provisioned, healthy, and running, then obtains the
public ingress FQDN and requires `GET /health` to return HTTP 200. A failed or timed
out gate fails the workflow with useful non-sensitive revision diagnostics and does
not automatically change traffic.

A user can then initiate Microsoft sign-in from the Testing site, reach the
dedicated `PlanDeck Testing` registration, and complete the callback to the public
Testing host.

### Key Discoveries:

- Pipeline OIDC identity settings and application sign-in settings are distinct;
  only the former are currently present in the workflows
  (`.github/workflows/azure-dev.yml:41-50`,
  `.github/workflows/azure-develop.yml:45-54`).
- AppHost and server startup validation already implement the required fail-closed
  application behavior (`src/PlanDeck/Aspire/PlanDeck.AppHost/AppHost.cs:89-103`,
  `src/PlanDeck/Web/PlanDeck.Server/Identity/MicrosoftAuthenticationOptions.cs:26-34`).
- `azd provision` and `azd deploy` can each create a revision; the gate must inspect
  `latestRevisionName` only after the final deploy command.
- Microsoft recommends inspecting `latestRevisionName`, then revision
  `provisioningState`, `healthState`, and `runningState` when diagnosing ACA
  deployments.
- `/health` is the readiness endpoint and includes SQL; `/alive` checks only process
  liveness (`src/PlanDeck/Aspire/PlanDeck.ServiceDefaults/Extensions.cs:100-121`).
- `src/PlanDeck/AZURE_DEPLOYMENT.md:46-67` still describes Azure Pipelines as the
  deployment source and uses obsolete authentication setting names, so the handoff
  documentation currently conflicts with the active GitHub workflows.

## What We're NOT Doing

- Reopening or rewriting the completed `microsoft-login-error` implementation plan.
- Changing Microsoft login buttons, account routes, OIDC callbacks, claims,
  provisioning, or the authentication capability contract.
- Adding another application health check for Entra configuration; required settings
  are already validated before the server can expose `/health`.
- Moving Testing secrets to Azure Key Vault in this change.
- Reusing the GitHub deployment service principal as the user-facing Entra
  application.
- Changing the legacy `.azuredevops/pipelines/azure-dev.yml` pipeline.
- Converting the `main` workflow from Testing to Production.
- Adding canary, blue-green, or automatic rollback behavior.
- Automatically shifting traffic or deactivating a failed revision.
- Running automated E2E tests against deployed `rg-test`.
- Adding WAF, ingress restrictions, or database migration rollback automation.

## Implementation Approach

Keep the application and Aspire resource model unchanged. Establish the external
Entra and GitHub Environment prerequisites first, then make both GitHub workflows
consume the same environment-scoped contract.

Follow the existing composite-action pattern twice: a small fail-fast Entra input
validator before `azd provision`, and a bounded ACA readiness probe after
`azd deploy`. Each action owns its PowerShell implementation so both workflows stay
semantically identical without hiding the whole deployment pipeline inside one large
action.

The ACA gate reads the current app after deployment, captures
`properties.latestRevisionName`, and polls that exact immutable revision. It accepts
only a provisioned, healthy revision in `Running` or `Running (at max)`, fails early
on terminal failure states, and times out on intermediate states. It then reads the
app ingress FQDN from Azure and retries public `GET /health` until HTTP 200 or the
bounded deadline. It reports resource, revision, and state details only; it never
prints credentials or container environment values.

## Critical Implementation Details

### Timing & lifecycle

Capture `latestRevisionName` after `azd deploy`, not after `azd provision`, because
both operations can create revisions. Poll the captured revision name rather than
re-reading "latest" on every attempt, so a concurrent or manual deployment cannot
silently change the object being verified.

### Debug & observability

On failure, include the Container App name, revision name, provisioning state,
health state, running state, bounded state details, and the public health status.
Do not dump the Container App definition, environment variables, or secret-bearing
command arguments.

## Phase 1: Establish the Testing Entra and GitHub Environment Contract

### Overview

Create the dedicated external identities and document the exact non-secret and secret
inputs that every Testing deployment requires.

### Changes Required:

#### 1. Dedicated Microsoft Entra web application

**External resources**: Microsoft Entra admin center, application registration
`PlanDeck Testing`

**Intent**: Separate user sign-in for the public Testing site from local Development
and from the GitHub Actions deployment identity.

**Contract**: Create a web application registration with the public callback
`https://plandeck-server.wittymeadow-96369440.polandcentral.azurecontainerapps.io/signin-oidc`,
an organizational-account audience compatible with the existing `/organizations`
authority, and a time-bounded client secret. Record only tenant ID, application
client ID, credential expiry, and ownership; never store the secret value in the
repository.

#### 2. GitHub Environment

**External resource**: GitHub repository environment `Testing`

**Intent**: Scope application sign-in configuration to the shared Testing deployment
boundary rather than repository-wide credentials.

**Contract**: Allow deployments from `main` and `develop`. Define environment
variables `AZURE_ENTRA_TENANT_ID` and `AZURE_ENTRA_CLIENT_ID`, and environment secret
`AZURE_ENTRA_CLIENT_SECRET`. Keep existing pipeline identity variables
`AZURE_CLIENT_ID` and `AZURE_TENANT_ID` separate.

#### 3. Deployment handoff prerequisites

**File**: `src/PlanDeck/AZURE_DEPLOYMENT.md`

**Intent**: Make the source-controlled handoff accurately describe the active
Testing authentication prerequisites before workflow code depends on them.

**Contract**: Document the dedicated registration, callback URI, environment variable
versus secret split, credential rotation ownership, and the distinction between
pipeline OIDC and application OIDC. Do not include actual IDs or secret values.

### Success Criteria:

#### Automated Verification:

- Documentation diff passes repository whitespace validation:
  `git diff --check`

#### Manual Verification:

- `PlanDeck Testing` exists with the exact public `/signin-oidc` redirect URI and
  organizational-account audience.
- GitHub Environment `Testing` permits both deployment branches and contains the two
  required variables plus the client-secret secret.
- The client-secret expiry and rotation owner are recorded outside the repository
  secret value.
- The repository diff contains secret names and setup instructions only, not actual
  Entra IDs or credential values.

**Implementation Note**: Do not proceed until both external systems are configured;
later workflow runs intentionally fail when these prerequisites are absent.

---

## Phase 2: Add a Shared Fail-Fast Entra Preflight

### Overview

Validate the environment-scoped Entra contract before provisioning can create an
invalid ACA revision.

### Changes Required:

#### 1. Entra configuration validation action

**Files**:

- `.github/actions/validate-azure-entra-config/action.yml` (new)
- `.github/actions/validate-azure-entra-config/validate-azure-entra-config.ps1` (new)

**Intent**: Give both workflows one safe, deterministic preflight for required
application-authentication inputs.

**Contract**: Accept tenant ID, client ID, client secret, and publish-target inputs.
Reject null, empty, or whitespace values before any Azure command runs. Log only
field names and validation outcome; never echo input values. Keep validation aligned
with `MicrosoftAuthenticationOptions.IsAvailable` rather than imposing a narrower
identifier format that the server does not require.

#### 2. Main Testing workflow

**File**: `.github/workflows/azure-dev.yml`

**Intent**: Bind the `main` deployment to the protected Testing configuration and
fail before provisioning when the application-authentication contract is incomplete.

**Contract**: Set job `environment: Testing`, map
`vars.AZURE_ENTRA_TENANT_ID`, `vars.AZURE_ENTRA_CLIENT_ID`, and
`secrets.AZURE_ENTRA_CLIENT_SECRET` to same-named job environment values, then invoke
the shared validator after Azure login and before `azd provision`. Preserve pipeline
identity, SQL, branch trigger, concurrency, and reset-database behavior.

#### 3. Develop Testing workflow

**File**: `.github/workflows/azure-develop.yml`

**Intent**: Apply the identical protected contract to the `develop` deployment.

**Contract**: Mirror the main workflow's environment binding and preflight ordering
without changing its branch trigger or display name.

### Success Criteria:

#### Automated Verification:

- PowerShell parses the validation script without syntax errors.
- The validation script succeeds with three non-empty placeholder values.
- The validation script fails with a sanitized error when each required value is
  omitted in turn.
- Whole solution builds: `dotnet build PlanDeck.slnx`

#### Manual Verification:

- A GitHub Actions run shows the `Testing` environment and passes preflight without
  exposing IDs or the secret in logs.
- Removing a required value in a controlled configuration check stops the workflow
  before `azd provision`; restore the value before continuing.

**Implementation Note**: The negative configuration check must not reach Azure
provisioning and must be performed without committing or printing a secret.

---

## Phase 3: Gate Deployment on the Final ACA Revision and Public Readiness

### Overview

Turn `azd deploy` command success into a verified application deployment result
without automating traffic changes.

### Changes Required:

#### 1. Container App readiness action

**Files**:

- `.github/actions/wait-for-container-app-readiness/action.yml` (new)
- `.github/actions/wait-for-container-app-readiness/wait-for-container-app-readiness.ps1` (new)

**Intent**: Centralize bounded revision polling and public readiness verification for
both Testing workflows.

**Contract**: Accept resource group, Container App name, revision timeout, public
health timeout, and health path. Resolve non-empty `latestRevisionName` once after
deployment. Poll that exact revision via Azure CLI and require
`provisioningState=Provisioned`, `healthState=Healthy`, and `runningState` equal to
`Running` or `Running (at max)`. Fail early for provisioning failure, unhealthy,
degraded, failed, or activation-failed terminal states; otherwise retry with bounded
backoff until timeout.

After revision success, resolve `properties.configuration.ingress.fqdn` through Azure
CLI and require HTTPS `GET /health` to return HTTP 200 within its own bounded retry
window. Treat redirects, authentication responses, SPA content, and every non-200
status as not ready. On failure, throw a sanitized diagnostic and leave traffic and
revision activation unchanged.

#### 2. Main Testing post-deploy gate

**File**: `.github/workflows/azure-dev.yml`

**Intent**: Make the `main` workflow fail unless its final application revision and
public endpoint are ready.

**Contract**: Invoke the shared readiness action immediately after
`azd deploy --no-prompt`, using existing `AZURE_RESOURCE_GROUP`,
Container App name `plandeck-server`, and health path `/health`. Do not add
`if: always()` or continue-on-error behavior.

#### 3. Develop Testing post-deploy gate

**File**: `.github/workflows/azure-develop.yml`

**Intent**: Enforce the identical final deployment gate for `develop`.

**Contract**: Mirror the main workflow invocation and timeout values so both routes
to `rg-test` have the same success definition.

### Success Criteria:

#### Automated Verification:

- PowerShell parses the readiness script without syntax errors.
- Both workflow definitions call the same readiness action immediately after their
  final `azd deploy` step.
- A healthy Testing deployment records one immutable revision name, reaches healthy
  running state, and receives HTTP 200 from public `/health`.
- Whole solution builds: `dotnet build PlanDeck.slnx`

#### Manual Verification:

- Workflow logs identify the verified revision and public health URL without exposing
  environment variables or credentials.
- The active ACA revision shown in Azure matches the revision verified by the
  workflow.
- The failure contract is reviewed: a failed gate leaves revision and traffic state
  untouched and points operators to revision/system/application logs.

**Implementation Note**: Do not add automatic rollback. A failed gate is an explicit
operator handoff because database migrations may already have run.

---

## Phase 4: Verify Microsoft Sign-In and Complete the Operational Handoff

### Overview

Exercise both deployment paths and the real user-facing authentication flow, then
make the deployment documentation match the implemented operating model.

### Changes Required:

#### 1. Active CI/CD documentation

**File**: `src/PlanDeck/AZURE_DEPLOYMENT.md`

**Intent**: Remove stale guidance that identifies Azure Pipelines as the active
deployment source and provide one accurate Testing runbook.

**Contract**: Identify `.github/workflows/azure-dev.yml` and
`.github/workflows/azure-develop.yml` as the active Testing deployments, with the
shared concurrency group and GitHub Environment. Mark
`.azuredevops/pipelines/azure-dev.yml` as legacy/out of scope. Document the two-stage
revision-plus-HTTPS gate, safe diagnostic commands, manual traffic rollback command,
and the rule that rollback requires human review because migrations are not reverted.

#### 2. Testing deployment verification

**External systems**: GitHub Actions, Azure Container Apps, Microsoft Entra,
public Testing application

**Intent**: Prove that the repository configuration, external registration, runtime
revision, and browser callback work as one end-to-end contract.

**Contract**: Dispatch or trigger both workflows separately under the shared
concurrency group. For each run, retain the workflow URL, verified revision name, and
health result without recording credentials. On the final healthy deployment,
initiate Microsoft sign-in, verify the authorization request uses the dedicated
Testing client ID and public callback, and complete sign-in with an organizational
account.

### Success Criteria:

#### Automated Verification:

- Main Testing workflow completes preflight, deployment, revision readiness, and
  public `/health` gates.
- Develop Testing workflow completes the same four gates.
- Whole solution builds: `dotnet build PlanDeck.slnx`

#### Manual Verification:

- "Sign in with a Microsoft account" redirects to Microsoft using the dedicated
  `PlanDeck Testing` client and exact public callback URI.
- A valid organizational account completes sign-in and returns to the Testing app.
- The runbook can be followed to identify a failed revision and perform a
  human-approved traffic rollback without exposing secrets.

**Implementation Note**: This change is complete only after both branch workflows and
the real Microsoft callback are confirmed.

---

## Testing Strategy

### Unit Tests:

- No application unit tests are required because application behavior is unchanged.
- Exercise the standalone Entra validation script with complete input and each
  missing-input case.
- Parse both new PowerShell scripts through the PowerShell AST parser to catch syntax
  errors without contacting Azure.

### Integration Tests:

- Use the real GitHub Actions environment and federated Azure login to verify
  environment resolution and preflight ordering.
- Run the ACA readiness action against the revision created by each workflow.
- Require the existing public `/health` endpoint to include the SQL readiness check.
- Preserve the existing integration coverage for required/optional Microsoft
  configuration and route availability.

### Manual Testing Steps:

1. Verify the `PlanDeck Testing` registration's audience, owner, credential expiry,
   and exact public redirect URI.
2. Verify the GitHub Environment branch restrictions, variables, and secret names.
3. Run the main Testing workflow and record its final verified revision.
4. Run the develop Testing workflow and record its final verified revision.
5. Confirm the latest active ACA revision matches the second workflow's verified
   revision and public `/health` returns HTTP 200.
6. Open the Testing login page, start Microsoft sign-in, inspect the client ID and
   redirect URI, and complete the callback.
7. Review the documented failure and manual rollback procedure without intentionally
   directing traffic to a broken revision.

## Performance Considerations

The new work runs only during deployment. Use bounded polling with increasing delays
and separate revision and HTTP deadlines so a failed deployment cannot hang a runner
indefinitely. Query only the named Container App and captured revision. The public
health probe must not download the Blazor application or follow redirects.

## Migration Notes

No database or application migration is required. Configure the Entra registration
and GitHub Environment before merging workflow enforcement. Existing published
revisions remain compatible.

If a new deployment fails after database migrations, the workflow stops without
changing traffic. Operators must inspect the revision and application logs, then
decide whether to redeploy or manually restore traffic to a known-good revision.
Application rollback does not reverse EF Core migrations.

## References

- Frame brief:
  `context/changes/testing-entra-deployment-readiness/frame.md`
- Completed application-side authentication plan:
  `context/changes/microsoft-login-error/plan.md`
- Existing shared readiness action:
  `.github/actions/wait-for-azure-sql/action.yml`
- Azure Container Apps revision lifecycle:
  `https://learn.microsoft.com/azure/container-apps/revisions#lifecycle`
- Azure Container Apps deployment diagnostics:
  `https://learn.microsoft.com/azure/container-apps/deployment-errors#diagnostic-workflow-summary`
- Azure Developer CLI Container Apps workflow:
  `https://learn.microsoft.com/azure/developer/azure-developer-cli/container-apps-workflows#image-based-deployment-strategy`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Establish the Testing Entra and GitHub Environment Contract

#### Automated

- [x] 1.1 Documentation diff passes repository whitespace validation

#### Manual

- [x] 1.2 Dedicated PlanDeck Testing registration has the exact public callback and audience
- [x] 1.3 GitHub Environment Testing contains the required branch policy, variables, and secret
- [x] 1.4 Client-secret expiry and rotation ownership are recorded securely
- [x] 1.5 Repository diff contains no actual Entra IDs or credential values

### Phase 2: Add a Shared Fail-Fast Entra Preflight

#### Automated

- [ ] 2.1 PowerShell parses the validation script without syntax errors
- [ ] 2.2 Validation succeeds with complete placeholder inputs
- [ ] 2.3 Validation rejects every missing required input without exposing values
- [ ] 2.4 Whole solution builds after preflight integration

#### Manual

- [ ] 2.5 Testing workflow preflight passes without exposing credentials
- [ ] 2.6 Controlled missing-input check stops before Azure provisioning

### Phase 3: Gate Deployment on the Final ACA Revision and Public Readiness

#### Automated

- [ ] 3.1 PowerShell parses the readiness script without syntax errors
- [ ] 3.2 Both workflows invoke the shared readiness action after final deployment
- [ ] 3.3 Healthy Testing deployment verifies one revision and public HTTP 200
- [ ] 3.4 Whole solution builds after readiness integration

#### Manual

- [ ] 3.5 Workflow logs expose safe revision and health diagnostics only
- [ ] 3.6 Azure active revision matches the workflow-verified revision
- [ ] 3.7 Failure behavior preserves traffic and revision state for operator review

### Phase 4: Verify Microsoft Sign-In and Complete the Operational Handoff

#### Automated

- [ ] 4.1 Main Testing workflow completes all deployment gates
- [ ] 4.2 Develop Testing workflow completes all deployment gates
- [ ] 4.3 Whole solution builds after documentation and handoff changes

#### Manual

- [ ] 4.4 Microsoft authorization uses the dedicated Testing client and callback
- [ ] 4.5 Organizational Microsoft sign-in completes and returns to Testing
- [ ] 4.6 Documented diagnostics and human-approved rollback procedure are usable
