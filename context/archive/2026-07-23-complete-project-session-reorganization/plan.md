# Complete Project and Session Reorganization Follow-ups Implementation Plan

## Overview

Close the remaining acceptance and implementation gaps from
`reorganize-project-and-sessions`. The work makes the remote Testing deployment
private and verifiably isolated, repairs the mobile Project-first experience,
runs the complete E2E suite against that protected target, and records
repeatable evidence for the outstanding deletion, localization, role, routing,
and pipeline criteria.

The selected security model intentionally keeps the current deterministic test
identity cookies and the implicit Owner identity when `e2e-user` is absent.
Therefore, private ingress is the mandatory security boundary for Testing and
must be proven from both allowed and denied network locations.

## Current State Analysis

The original change has landed and its local automated suites pass. Its
implementation review left eight criteria pending and two findings intentionally
unresolved:

- Project deletion is covered by browser automation for an active Session, its
  children, room invalidation, and a reusable shared Team, but manual criterion
  2.5 did not exercise active and inactive Sessions together
  (`context/changes/reorganize-project-and-sessions/reviews/impl-review.md:181`).
- The top app bar renders all navigation, identity, localization, and logout
  actions in one row, while Session headers and list rows do not constrain long
  labels (`src/PlanDeck/Web/PlanDeck.Client/Layout/MainLayout.razor:13`,
  `src/PlanDeck/Web/PlanDeck.Client/Pages/Sessions.razor:32`).
- The existing mobile E2E checks only that the create button is visible at
  `390x844`; it does not test the failing `375x812` viewport or horizontal
  overflow (`src/PlanDeck/Tests/PlanDeck.E2e.Tests/SessionsTests.cs:71`).
- Test authentication accepts unsigned role/guest cookies and defaults to Owner
  when no role cookie is present
  (`src/PlanDeck/Web/PlanDeck.Server/Identity/TestAuthenticationHandler.cs:44`,
  `src/PlanDeck/Web/PlanDeck.Server/Identity/TestAuthenticationHandler.cs:102`).
- Scenario endpoints require a secret token and are mapped only in Development
  or Testing, while Production endpoint absence already has an integration test
  (`src/PlanDeck/Web/PlanDeck.Server/Testing/E2eScenarioEndpoints.cs:10`,
  `src/PlanDeck/Tests/PlanDeck.Integration.Tests/Identity/E2eScenarioEndpointTests.cs:105`).
- The AppHost always publishes the server with an external HTTP endpoint and
  does not make Testing ingress private
  (`src/PlanDeck/Aspire/PlanDeck.AppHost/AppHost.cs:12`,
  `src/PlanDeck/Aspire/PlanDeck.AppHost/AppHost.cs:84`).
- The pipeline can run remote E2E, but it uses a Microsoft-hosted public agent,
  executes the whole remote suite in one opaque command, and runs before the
  Deploy stage rather than proving a freshly deployed protected Testing target
  (`.azuredevops/pipelines/azure-dev.yml:25`,
  `.azuredevops/pipelines/azure-dev.yml:62`,
  `.azuredevops/pipelines/azure-dev.yml:85`).

## Desired End State

A dedicated `Testing` deployment is reachable only from its approved private
network. A private self-hosted Azure DevOps agent proves successful health,
scenario authorization, setup, E2E execution, and cleanup; a Microsoft-hosted
agent proves the same Testing URL is not reachable publicly. Production remains
externally usable but returns `404` for the test scenario surface and cannot
start with test authentication enabled.

At `375x812`, authenticated navigation moves into a mobile drawer, all primary
actions remain reachable, and long Project and Session names occupy at most two
lines with a full-name tooltip. The local and remote browser suites verify this
without horizontal page overflow.

The final evidence covers active and inactive Session deletion, shared Team
preservation, room invalidation, Owner/Admin/Member behavior in English and
Polish, Project-first navigation, direct links, guest voting, Production
isolation, and layer-identifying pipeline logs.

### Key Discoveries:

- `MainLayout.razor.cs` already contains `_drawerOpen` and `DrawerToggle`, so the
  responsive navigation can reuse existing state instead of adding a new state
  abstraction (`src/PlanDeck/Web/PlanDeck.Client/Layout/MainLayout.razor.cs:6`).
- `E2eIdentityContextFactory` already creates isolated Owner, Admin, Member, and
  guest browser contexts, and is the canonical place to attach deterministic
  identity selection (`src/PlanDeck/Tests/PlanDeck.E2e.Tests/E2eIdentityContextFactory.cs:5`).
- `E2eScenarioClient` and the scenario endpoint already enforce a secret token
  for seed/cleanup; this plan does not replace that control with a new auth
  protocol (`src/PlanDeck/Web/PlanDeck.Server/Testing/E2eScenarioEndpoints.cs:25`).
- Aspire emits Azure Container Apps infrastructure through
  `PublishAsAzureContainerApp`, making `AppHost.cs` the source of truth for the
  Testing ingress choice (`src/PlanDeck/Aspire/PlanDeck.AppHost/AppHost.cs:84`).
- Remote fixture validation already requires `RemoteEnvironment=Test` and an
  E2E scenario token, so pipeline changes should preserve this fail-closed
  contract (`src/PlanDeck/Tests/PlanDeck.E2e.Tests/AspireAppFixture.cs:25`).
- Production endpoint absence is already testable through
  `WebApplicationFactory`; the remaining gap is a probe against the deployed
  Production URL (`src/PlanDeck/Tests/PlanDeck.Integration.Tests/Identity/E2eScenarioEndpointTests.cs:105`).

## What We're NOT Doing

- Replacing deterministic Testing identities with real Microsoft Entra accounts.
- Adding a token bootstrap, signing the `e2e-user` cookie, or removing the
  implicit Owner fallback.
- Treating the scenario token as sufficient protection for the complete Testing
  application.
- Adding rate limiting, token rotation infrastructure, or a new secrets store.
- Changing Project, Session, voting, guest, or role domain behavior.
- Changing the existing single-dialog Project deletion contract.
- Adding new localization languages.
- Introducing visual snapshot testing or pixel-perfect responsive assertions.
- Replacing Aspire/azd provisioning with a separate hand-written Bicep stack.
- Redesigning the pipeline into one job per E2E layer; the E2E execution remains
  one step with structured phase logs.

## Implementation Approach

First make private ingress a deploy-time invariant of the Testing environment
and establish independent allowed/denied network probes. Then repair the mobile
UI using MudBlazor responsive components and explicit bounded text styles.
After those foundations are stable, restructure the deployment/E2E pipeline so
the protected target is deployed before it is tested and the single E2E step
emits phase-specific diagnostics. Finally run the complete deterministic role,
localization, deletion, routing, and guest regression checklist and record
reproducible evidence.

## Critical Implementation Details

### Timing & lifecycle

The remote gate must deploy or select the Testing environment before any browser
test runs. Scenario cleanup runs in `finally` and must execute even after a test
failure. The public denial probe and private success probe must target the same
deployment revision.

### State sequencing

Testing ingress is private only when publish mode is explicitly configured for
the Testing/test-auth deployment. Normal Production publishing remains external.
The pipeline must never infer this distinction from the URL alone; it uses
separate environment configuration and agent pools.

### Debug & observability

The single remote E2E step writes explicit `preflight`, `scenario-auth`,
`playwright-setup`, `e2e`, and `cleanup` log boundaries and preserves TRX plus
Playwright artifacts. Secrets and scenario tokens must never be printed.

## Phase 1: Private Testing Boundary and Production Isolation

### Overview

Make private ingress the explicit security boundary for deterministic test
authentication and prove the deployed Testing and Production exposure contracts.

### Changes Required:

#### 1. Environment-specific Container Apps ingress

**Files**:
- `src/PlanDeck/Aspire/PlanDeck.AppHost/AppHost.cs`
- `src/PlanDeck/Aspire/PlanDeck.AppHost/appsettings.json`
- Azure environment configuration consumed by `azd`

**Intent**: Publish the test-auth deployment with internal/private ingress while
keeping normal Production publishing externally reachable.

**Contract**: Publish mode distinguishes the dedicated Testing environment from
Production through an explicit configuration input. When test auth is enabled
for Testing, the generated Container App ingress is internal and the scenario
token remains a secret parameter. Production keeps external ingress and cannot
enable `Authentication:UseTestScheme`.

#### 2. Allowed and denied network probes

**Files**:
- `.azuredevops/pipelines/azure-dev.yml`
- pipeline variable groups and agent-pool configuration

**Intent**: Prove that the Testing deployment is usable from its approved
private network and unreachable from the public Internet.

**Contract**:
- A private self-hosted agent with network access resolves the Testing host and
  receives an application response.
- A Microsoft-hosted public agent targets the same URL and must not establish a
  successful HTTP connection.
- Both probes identify the tested deployment/revision without logging secrets.
- A missing private pool, Testing URL, or environment identifier fails the gate
  before E2E execution.

#### 3. Production runtime probe and configuration tests

**Files**:
- `src/PlanDeck/Tests/PlanDeck.Integration.Tests/Identity/ProductionAuthenticationConfigurationTests.cs`
- `src/PlanDeck/Tests/PlanDeck.Integration.Tests/Identity/E2eScenarioEndpointTests.cs`
- `.azuredevops/pipelines/azure-dev.yml`

**Intent**: Verify both the application configuration and the deployed
Production endpoint reject test capabilities.

**Contract**:
- Integration tests prove Production startup rejects test auth and does not map
  `/testing/e2e-scenarios`.
- A pipeline probe against the configured Production URL receives `404` from the
  scenario route without sending a scenario token.
- The probe never mutates Production data and does not treat an unrelated
  connectivity failure as proof of endpoint absence.

### Success Criteria:

#### Automated Verification:

- Testing publish output configures private ingress while Production publish
  output remains external.
- Production authentication and scenario endpoint integration tests pass:
  `dotnet test Tests/PlanDeck.Integration.Tests/PlanDeck.Integration.Tests.csproj --filter "FullyQualifiedName~ProductionAuthenticationConfigurationTests|FullyQualifiedName~E2eScenarioEndpointTests"`.
- The private agent succeeds and the public agent is denied against the same
  Testing deployment revision.
- The deployed Production scenario route returns `404`.

#### Manual Verification:

- Azure Container Apps portal/network configuration shows no public ingress for
  Testing and external ingress for Production.
- The private agent identity and network path are documented as prerequisites
  without exposing credentials.

**Implementation Note**: Pause after the automated gates pass so a human can
confirm the deployed ingress settings before enabling remote browser tests.

---

## Phase 2: Responsive Project-First Navigation and Session Layout

### Overview

Repair the mobile regression at `375x812` while preserving desktop navigation,
authorization-aware controls, localization, and Project-first routes.

### Changes Required:

#### 1. Responsive app navigation

**Files**:
- `src/PlanDeck/Web/PlanDeck.Client/Layout/MainLayout.razor`
- `src/PlanDeck/Web/PlanDeck.Client/Layout/MainLayout.razor.cs`
- `src/PlanDeck/Web/PlanDeck.Client/wwwroot/css/app.css`
- `src/PlanDeck/Web/PlanDeck.Client/Resources/SharedResource.resx`
- `src/PlanDeck/Web/PlanDeck.Client/Resources/SharedResource.pl.resx`

**Intent**: Move Projects, Teams, language, identity, and logout actions into an
accessible mobile drawer while retaining the current desktop app bar.

**Contract**:
- The drawer uses the existing `_drawerOpen` state and `DrawerToggle`.
- A labelled hamburger button is visible only below the selected MudBlazor
  breakpoint; desktop navigation remains visible above it.
- Guest and unauthenticated states retain their existing permitted actions.
- Navigation or culture selection closes the mobile drawer.
- Every new label is localized with EN/PL key parity.

#### 2. Bounded Project and Session labels

**Files**:
- `src/PlanDeck/Web/PlanDeck.Client/Pages/ProjectDetails.razor`
- `src/PlanDeck/Web/PlanDeck.Client/Pages/Sessions.razor`
- `src/PlanDeck/Web/PlanDeck.Client/wwwroot/css/app.css`

**Intent**: Prevent long context names and Session names from pushing status
chips or actions outside the viewport.

**Contract**:
- Project and Session names wrap to at most two lines.
- The full name is available through an accessible tooltip.
- Header action groups wrap or stack at the mobile breakpoint.
- Status chips and primary actions remain visible and do not overlap text.
- The page has no horizontal document overflow at `375x812`.

#### 3. Mobile regression coverage

**Files**:
- `src/PlanDeck/Tests/PlanDeck.E2e.Tests/SessionsTests.cs`
- `src/PlanDeck/Tests/PlanDeck.E2e.Tests/Pages/SessionsPage.cs`
- relevant Project page object/tests when the shared layout is exercised

**Intent**: Replace the shallow mobile smoke check with assertions for the
actual regression.

**Contract**: The test uses `375x812`, opens the drawer, navigates through its
primary actions, exercises long Project and Session names, verifies the create
or read-only controls appropriate to the role, and asserts document width does
not exceed viewport width. Locators remain role/label/text-first.

### Success Criteria:

#### Automated Verification:

- Client and server build with the responsive layout:
  `dotnet build Web/PlanDeck.Server/PlanDeck.Server.csproj`.
- EN/PL resource parity tests pass:
  `dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj --filter "FullyQualifiedName~LocalizationResourceParityTests"`.
- Targeted mobile E2E passes locally through Aspire at `375x812`:
  `dotnet test Tests/PlanDeck.E2e.Tests/PlanDeck.E2e.Tests.csproj --filter "FullyQualifiedName~Sessions_RendersOnMobileViewport"`.

#### Manual Verification:

- At `375x812`, Projects, Teams, language, identity, and logout are reachable
  without horizontal scrolling.
- Long Project and Session names show at most two lines, expose the complete
  value in a tooltip, and do not hide status or action controls.
- Desktop navigation remains visually and functionally unchanged.

**Implementation Note**: Pause for human verification in English and Polish at
mobile and desktop widths before changing the remote pipeline gate.

---

## Phase 3: Protected Remote E2E and Layered Diagnostics

### Overview

Deploy or select the protected Testing target before testing it, run the full
remote suite from the private agent, and make failures attributable without
splitting the E2E execution into multiple steps.

### Changes Required:

#### 1. Deployment and test-stage ordering

**File**: `.azuredevops/pipelines/azure-dev.yml`

**Intent**: Ensure the tested revision is deployed to Testing before the remote
gate starts and keep Production deployment independent of the test-auth target.

**Contract**:
- Unit/integration/build gates remain before deployment.
- The dedicated Testing environment is deployed with test auth, private ingress,
  and the protected scenario token.
- Public denial and Production endpoint probes run against explicit URLs.
- Remote E2E runs only after Testing deployment and private reachability pass.
- The normal Production/development deployment never reuses Testing auth
  parameters.

#### 2. Structured remote E2E execution

**Files**:
- `.azuredevops/pipelines/azure-dev.yml`
- `src/PlanDeck/Tests/PlanDeck.E2e.Tests/AspireAppFixture.cs`
- `src/PlanDeck/Tests/PlanDeck.E2e.Tests/E2eScenarioClient.cs`

**Intent**: Keep one E2E command step while making setup, authorization, browser,
test, and cleanup failures distinguishable.

**Contract**:
- The step logs non-secret boundaries for `preflight`, `scenario-auth`,
  `playwright-setup`, `e2e`, and `cleanup`.
- Preflight validates `BaseUrl`, `RemoteEnvironment=Test`, token presence,
  HTTPS, and private reachability.
- Scenario authorization performs a bounded seed/cleanup check before the suite.
- Chromium installation and the E2E command have distinct exit handling.
- TRX, Playwright traces/screenshots on failure, and cleanup diagnostics are
  published even when the E2E command fails.
- Cleanup is idempotent and does not turn an earlier application/test failure
  into an apparent success.

#### 3. Remote role, localization, and regression matrix

**Files**:
- `src/PlanDeck/Tests/PlanDeck.E2e.Tests/SessionRoleSmokeTests.cs`
- `src/PlanDeck/Tests/PlanDeck.E2e.Tests/ProjectsTests.cs`
- `src/PlanDeck/Tests/PlanDeck.E2e.Tests/SessionsTests.cs`
- `src/PlanDeck/Tests/PlanDeck.E2e.Tests/VotingRoomTests.cs`
- `src/PlanDeck/Tests/PlanDeck.E2e.Tests/GuestVotingTests.cs`
- `src/PlanDeck/Tests/PlanDeck.E2e.Tests/E2eIdentityContextFactory.cs`
- corresponding page objects

**Intent**: Make the outstanding acceptance matrix repeatable through
deterministic Testing identities instead of real Entra accounts.

**Contract**:
- Owner, Admin, and Member use separate browser contexts and unique `runId`
  scenarios.
- Role smoke paths run in both English and Polish.
- Project deletion seeds active and inactive Sessions, verifies active room
  invalidation and all child removal, and proves the shared Team remains usable.
- Regression paths cover `/`, Project and Session direct URLs, legacy
  `/sessions`, `/join/{code}`, `/voting/{sessionId}`, authenticated return
  navigation, and guest voting.
- Every test owns cleanup in `finally` and can run independently.

### Success Criteria:

#### Automated Verification:

- Full remote E2E passes against the private Testing deployment with
  `BaseUrl`, `RemoteEnvironment=Test`, and the protected scenario token.
- Owner/Admin/Member role smoke paths pass in English and Polish.
- Project deletion with active and inactive Sessions removes owned data,
  invalidates the active room, and preserves a usable shared Team.
- Routing, direct-link, mobile, and guest-voting regressions pass remotely.
- Pipeline publishes TRX and failure artifacts and identifies the failed
  execution boundary without printing secrets.

#### Manual Verification:

- A failed dry-run in each supported diagnostic boundary is recognizable from
  the job log and published artifacts.
- Scenario data is absent after both successful and intentionally failed runs.

**Implementation Note**: Pause after one successful and one controlled failing
pipeline run so a human can confirm diagnostics and cleanup.

---

## Phase 4: Final Acceptance and Change Closure

### Overview

Re-run local and remote quality gates, execute the deterministic acceptance
checklist, and capture evidence that closes the inherited pending criteria.

### Changes Required:

#### 1. Acceptance evidence

**Files**:
- `context/changes/complete-project-session-reorganization/reviews/impl-review.md`
- pipeline run artifacts and links referenced by the review
- `context/changes/reorganize-project-and-sessions/plan.md`

**Intent**: Record reproducible evidence rather than marking inherited criteria
complete from assumption or partial coverage.

**Contract**: The review records commands, pipeline run identifiers, tested
deployment revisions, viewport/culture/role matrix, network probe results,
Production `404`, deletion graph outcome, cleanup result, and any accepted
residual risk. The original plan's pending criteria are updated only when their
evidence exists.

#### 2. Final quality gate

**Files**:
- all projects and tests touched by preceding phases

**Intent**: Ensure the follow-up does not regress the already completed
Project-first implementation.

**Contract**: Unit, integration, local E2E, protected remote E2E, and full
solution build run from clean scenarios without execution-order dependence.

### Success Criteria:

#### Automated Verification:

- Unit tests pass:
  `dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj`.
- Integration tests pass:
  `dotnet test Tests/PlanDeck.Integration.Tests/PlanDeck.Integration.Tests.csproj`.
- Full local Aspire E2E passes:
  `dotnet test Tests/PlanDeck.E2e.Tests/PlanDeck.E2e.Tests.csproj`.
- Full protected remote Testing E2E and both network probes pass in Azure
  DevOps.
- Entire solution builds:
  `dotnet build PlanDeck.slnx`.

#### Manual Verification:

- Deterministic Owner, Admin, and Member workflows are accepted in English and
  Polish.
- Project-first navigation, cascade deletion, direct links, mobile layout, and
  guest voting match the approved behavior.
- Pipeline failures are diagnosable by boundary and scenario cleanup is
  confirmed.
- Original criteria 2.5, 3.7, 4.5, 4.6, 5.4, 5.6, 5.7, and 5.8 have linked
  evidence and no unsupported completion marks remain.

**Implementation Note**: Human approval of this phase is required before the
follow-up change or the original change is archived.

---

## Testing Strategy

### Unit Tests:

- Preserve localization resource-key parity after adding mobile navigation
  labels.
- Exercise any extracted responsive state/helper behavior only where it contains
  logic; do not add component-test infrastructure solely for CSS.

### Integration Tests:

- Verify Production rejects test-auth startup configuration.
- Verify Production does not map scenario endpoints.
- Preserve missing, invalid, and valid scenario-token behavior in Testing.
- Verify scenario cleanup remains isolated and idempotent.

### E2E Tests:

- Use Page Object Pattern, accessible locators, state-based waits, unique
  `runId` values, and `finally` cleanup.
- Test `375x812` and desktop widths without fixed waits or CSS/DOM-structure
  locators.
- Run Owner/Admin/Member contexts independently in both EN and PL.
- Seed active and inactive Sessions in the deletion scenario.
- Publish traces/screenshots only as artifacts; assertions remain DOM/state
  based.

### Manual Testing Steps:

1. From a public agent, confirm the Testing URL cannot establish a successful
   HTTP connection.
2. From the private agent, confirm Testing health and authorized scenario
   seed/cleanup.
3. Probe Production and confirm the normal application responds while
   `/testing/e2e-scenarios/` returns `404`.
4. At `375x812`, use the drawer and inspect long Project/Session names in EN and
   PL; repeat desktop navigation.
5. In isolated Owner/Admin/Member contexts, execute the approved Project and
   Session role smoke paths in both cultures.
6. Delete a Project containing active and inactive Sessions and verify the room,
   child graph, and shared Team outcomes.
7. Exercise Project-first/direct/legacy/join/voting routes and guest voting.
8. Inspect a successful and controlled failing pipeline run for diagnostics,
   artifacts, secret masking, and scenario cleanup.

## Performance Considerations

- The responsive changes are presentational and must not add repeated network
  calls or duplicate authorization queries.
- The mobile drawer should remain unmounted or hidden according to MudBlazor's
  responsive pattern rather than running independent navigation state.
- Remote preflight and scenario-auth checks use bounded HTTP timeouts so a
  blocked network path fails promptly.
- Full remote E2E remains one suite invocation to avoid duplicate browser startup
  and scenario setup overhead.

## Migration Notes

- No database migration or data backfill is required.
- Testing ingress changes require a private DNS/network path and a self-hosted
  Azure DevOps agent in the approved network before the remote gate can pass.
- The Microsoft-hosted public agent is intentionally retained only for the
  denied-access probe and ordinary build/test work.
- Existing Testing identity cookies remain unsigned and missing `e2e-user`
  continues to authenticate as Owner. This is an accepted residual risk and is
  safe only while private ingress is continuously enforced.
- Production remains externally reachable and continues to use Microsoft Entra
  authentication.

## References

- Original plan:
  `context/changes/reorganize-project-and-sessions/plan.md`
- Original implementation review:
  `context/changes/reorganize-project-and-sessions/reviews/impl-review.md`
- Test authentication:
  `src/PlanDeck/Web/PlanDeck.Server/Identity/TestAuthenticationHandler.cs`
- Scenario endpoint guard:
  `src/PlanDeck/Web/PlanDeck.Server/Testing/E2eScenarioEndpoints.cs`
- Azure Container Apps publish configuration:
  `src/PlanDeck/Aspire/PlanDeck.AppHost/AppHost.cs`
- Remote fixture guard:
  `src/PlanDeck/Tests/PlanDeck.E2e.Tests/AspireAppFixture.cs`
- Pipeline:
  `.azuredevops/pipelines/azure-dev.yml`
- Main layout:
  `src/PlanDeck/Web/PlanDeck.Client/Layout/MainLayout.razor`
- Session UI:
  `src/PlanDeck/Web/PlanDeck.Client/Pages/Sessions.razor`
- Mobile E2E:
  `src/PlanDeck/Tests/PlanDeck.E2e.Tests/SessionsTests.cs`
- Project deletion E2E:
  `src/PlanDeck/Tests/PlanDeck.E2e.Tests/ProjectsTests.cs`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Private Testing Boundary and Production Isolation

#### Automated

- [x] 1.1 Testing publish output uses private ingress and Production remains external
- [x] 1.2 Production authentication and scenario endpoint integration tests pass
- [x] 1.3 Private Testing access succeeds and public Testing access is denied for the same revision
- [ ] 1.4 Deployed Production scenario endpoint returns not found

#### Manual

- [ ] 1.5 Azure ingress settings and private agent network path are confirmed

### Phase 2: Responsive Project-First Navigation and Session Layout

#### Automated

- [x] 2.1 Client and server build with the responsive layout — 1e04cf4
- [x] 2.2 English and Polish resource key parity is preserved — 1e04cf4
- [x] 2.3 Mobile Project-first E2E passes at 375x812 without horizontal overflow — 1e04cf4

#### Manual

- [x] 2.4 Mobile navigation and long labels remain usable in English and Polish — 1e04cf4
- [x] 2.5 Desktop navigation remains unchanged — 1e04cf4

### Phase 3: Protected Remote E2E and Layered Diagnostics

#### Automated

- [x] 3.1 Full protected remote Testing E2E suite passes — f946010
- [x] 3.2 Owner Admin and Member role smoke paths pass in English and Polish — f946010
- [x] 3.3 Project deletion covers active and inactive Sessions room invalidation and shared Team preservation — f946010
- [x] 3.4 Routing direct-link mobile and guest-voting regressions pass remotely — f946010
- [x] 3.5 Pipeline publishes secret-safe layer-identifying test diagnostics and artifacts — f946010

#### Manual

- [ ] 3.6 Successful and controlled failing pipeline runs are diagnosable
- [ ] 3.7 Scenario cleanup succeeds after successful and failed runs

### Phase 4: Final Acceptance and Change Closure

#### Automated

- [x] 4.1 Unit test suite passes — 32c5f75
- [x] 4.2 Integration test suite passes — 32c5f75
- [x] 4.3 Full local Aspire E2E suite passes — 32c5f75
- [x] 4.4 Full protected remote gate and network probes pass — 32c5f75
- [x] 4.5 Entire solution builds — 32c5f75

#### Manual

- [ ] 4.6 Deterministic Owner Admin and Member workflows are accepted in English and Polish
- [ ] 4.7 Navigation deletion direct links mobile layout and guest voting match approved behavior
- [ ] 4.8 Pipeline diagnostics and scenario cleanup are accepted
- [ ] 4.9 All eight inherited pending criteria have linked evidence
