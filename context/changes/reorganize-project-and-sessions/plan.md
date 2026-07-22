# Reorganize Projects and Project-Owned Sessions Implementation Plan

## Overview

Reorganize PlanDeck so a Project is the required parent context for session management, Azure DevOps configuration, and delegated access. Close the remaining backend authorization gaps, replace the global Sessions workflow with project-scoped routes, support confirmed cascade deletion of a Project and its Sessions, and make the Owner/Admin/Member behavior enforceable through deterministic automated tests.

## Current State Analysis

The persistence model is already mostly project-owned: every `PlanningSession` has a required `ProjectId`, project creation assigns the creator as `Owner`, and Azure DevOps operations resolve configuration from the Session's Project. The remaining behavior is inconsistent with that model:

- `ListSessionsRequest` has no Project context, the repository lists all tenant Sessions, and the service performs per-Session authorization checks.
- Session mutations accept any project role instead of distinguishing `Admin` from read-only `Member`.
- `SessionMemberGrpcService` rejects guests but does not authorize the caller against the target Project, leaving an intra-tenant IDOR path.
- `/sessions` is still a top-level route and the create form asks the user to choose a Project.
- Project deletion is blocked when Sessions exist, despite the confirmed requirement to delete the entire Project-owned graph.
- E2E test auth has only two identities and no deterministic scenario lifecycle, so it cannot enforce the confirmed Owner/Admin/Member matrix in CI.

## Desired End State

Authenticated users enter through `/projects`, select a Project, and manage its Sessions only through `/projects/{projectId}/sessions`. The backend requires a Project for Session listing, authorizes reads at `Member` and mutations at `Admin`, and conceals inaccessible resources with `NotFound`. `Owner` and `Admin` can perform Session administration, while `Member` can read and vote.

Deleting a Project through one explicit confirmation removes its Sessions, Session tasks, Session members, project memberships, project-team links, and ADO connection record while preserving shared Teams and AppUsers. The Key Vault secret cleanup remains ordered before SQL deletion, and active in-memory rooms for deleted Sessions are invalidated.

The complete role matrix is covered by unit/integration tests and browser tests that run deterministically both through local Aspire and a protected `Testing` deployment in CI.

### Key Discoveries:

- `PlanningSession.ProjectId` and the supporting `(TenantId, ProjectId, CreatedAtUtc)` index already exist; no Session backfill is required (`src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/Configurations/PlanningSessionConfiguration.cs:32`).
- Global Session listing and its N+1 access checks originate in `ListSessionsRequest`, `ISessionRepository`, and `SessionGrpcService.ListSessionsAsync` (`src/PlanDeck/Core/PlanDeck.Core.Shared/Contracts/ISessionService.cs:203`, `src/PlanDeck/Core/PlanDeck.Application/Services/SessionGrpcService.cs:117`).
- `SessionMemberGrpcService` currently has no resource-level authorization (`src/PlanDeck/Core/PlanDeck.Application/Services/SessionMemberGrpcService.cs:10`).
- The existing role hierarchy already models the selected policy `Member < Admin < Owner`; no new role or role migration is needed (`src/PlanDeck/Core/PlanDeck.Application/Domain/ProjectRole.cs:1`).
- Changing only the Project-to-Session FK from `Restrict` to `Cascade` creates no multiple-cascade path in the current SQL Server graph (`src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/Configurations/PlanningSessionConfiguration.cs:42`).
- Test auth supports identity selection by cookie, but currently exposes only two deterministic identities (`src/PlanDeck/Web/PlanDeck.Server/Testing/TestMemberIdentities.cs:1`, `src/PlanDeck/Web/PlanDeck.Server/Identity/TestAuthenticationHandler.cs:22`).
- Remote E2E already rejects unsafe environments, but the pipeline does not pass the required `RemoteEnvironment=Test` parameter (`src/PlanDeck/Tests/PlanDeck.E2e.Tests/AspireAppFixture.cs:19`, `.azuredevops/pipelines/azure-dev.yml:59`).

## What We're NOT Doing

- Adding an `Editor` role or replacing the existing `Member < Admin < Owner` hierarchy.
- Moving globally reusable Teams under a Project or changing Team ownership semantics.
- Adding `ProjectId` to Session-addressed voting, guest, ADO write-back, or SignalR contracts when the server can resolve it from `SessionId`.
- Changing `/voting/{sessionId}` or `/join/{code}` route identity.
- Preserving or redirecting the legacy `/sessions` route; it will no longer match and will follow the application's normal not-found behavior.
- Adding Project archival, soft deletion, restore, or recycle-bin workflows.
- Providing cross-resource transactions between SQL Server and Key Vault; a failed secret cleanup continues to block SQL deletion.
- Replacing the in-process SignalR room store or introducing a distributed backplane.

## Implementation Approach

First make the backend Project boundary explicit and secure so every client surface depends on a correct contract. Then change Project deletion semantics and migration independently, including external-secret and realtime cleanup. Build the project-first UI on those stable contracts. Finally introduce deterministic multi-identity E2E infrastructure and use it to enforce the full role matrix and regression behavior locally and in CI.

## Critical Implementation Details

### Timing & lifecycle

Project deletion must capture the owned Session identifiers, successfully soft-delete the Project's Key Vault secret, and then commit the SQL delete. A Key Vault failure must leave the SQL graph intact; the database cascade handles the relational graph atomically after external cleanup succeeds. After a successful SQL delete, active rooms for the captured Sessions are invalidated as a best-effort, idempotent step — so a failed SQL delete never destroys rooms for still-existing Sessions.

### State sequencing

For Session-addressed operations, the server resolves `sessionId -> projectId -> effective role`; clients must not submit an additional Project identifier that could disagree with persisted ownership. Session listing is the exception because its resource boundary is the requested Project itself.

### Debug & observability

Test authentication and scenario-management endpoints are permitted only in `Development` or `Testing`. A remote E2E run must also identify the target as `Test` and use a deployment-level protection mechanism; Production must continue to fail startup if test auth is requested.

## Phase 1: Project-Scoped Backend and Authorization

### Overview

Make Project ownership explicit in the Session list contract, replace tenant-wide/N+1 listing with one project-filtered query, and enforce the selected role policy across Session and Session-member operations.

### Changes Required:

#### 1. Session gRPC contract and client wrapper

**Files**:
- `src/PlanDeck/Core/PlanDeck.Core.Shared/Contracts/ISessionService.cs`
- `src/PlanDeck/Web/PlanDeck.Client/Services/ISessionClientService.cs`
- `src/PlanDeck/Web/PlanDeck.Client/Services/SessionClientService.cs`

**Intent**: Require callers to identify the Project whose Sessions are being listed, preventing a global cross-project list from reappearing at the API or client layer.

**Contract**: `ListSessionsRequest` gains `[DataMember(Order = 1)] Guid ProjectId`; the client list method accepts a non-empty `projectId` and sends it in the request. Existing Session-addressed methods remain unchanged.

#### 2. Project-filtered Session repository

**Files**:
- `src/PlanDeck/Core/PlanDeck.Application/Abstractions/ISessionRepository.cs`
- `src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/SessionRepository.cs`

**Intent**: Execute one tenant-filtered, Project-filtered query supported by the existing composite index.

**Contract**: Replace the unscoped `GetSessionsAsync(CancellationToken)` contract with a `projectId`-scoped query. The returned set must contain only Sessions whose persisted `ProjectId` matches the requested Project.

#### 3. Session role enforcement

**File**: `src/PlanDeck/Core/PlanDeck.Application/Services/SessionGrpcService.cs`

**Intent**: Apply one Project access check before list retrieval and consistently distinguish read access from administrative mutations.

**Contract**:
- Empty `ListSessionsRequest.ProjectId` returns `InvalidArgument`.
- Listing requires effective `Member` access to the requested Project, then invokes the filtered repository once.
- Session reads require `Member`.
- Session creation, configuration changes, task add/edit/remove/bulk import, activation, ADO write-back, and Session deletion require `Admin`.
- Missing Session or absent Project membership returns `NotFound`; an existing lower role returns `PermissionDenied`.
- Guest-specific voting/join behavior remains unchanged.

#### 4. Session-member authorization

**File**: `src/PlanDeck/Core/PlanDeck.Application/Services/SessionMemberGrpcService.cs`

**Intent**: Close the intra-tenant IDOR path by applying the same Session-to-Project role resolution used by other Session operations.

**Contract**: Listing participants requires `Member`; assigning and removing participants require `Admin`; inaccessible Sessions return `NotFound`, lower roles return `PermissionDenied`, and guests remain rejected.

#### 5. Backend authorization tests

**Files**:
- `src/PlanDeck/Tests/PlanDeck.Unit.Tests/Sessions/SessionGrpcServiceTests.cs`
- `src/PlanDeck/Tests/PlanDeck.Unit.Tests/Sessions/SessionMemberGrpcServiceTests.cs`
- `src/PlanDeck/Tests/PlanDeck.Integration.Tests/Persistence/SessionPersistenceTests.cs`

**Intent**: Lock down request validation, project isolation, query scope, and the Owner/Admin/Member matrix before UI changes depend on it.

**Contract**: Test fakes become role-configurable. Tests cover empty Project IDs, Project A/B isolation, Member read/vote access, Member mutation denial, Owner/Admin mutation success, inaccessible-resource concealment, and every Session-member operation.

### Success Criteria:

#### Automated Verification:

- Session service and Session-member unit tests pass: `dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj --filter "FullyQualifiedName~SessionGrpcServiceTests|FullyQualifiedName~SessionMemberGrpcServiceTests"`
- Session persistence integration tests pass: `dotnet test Tests/PlanDeck.Integration.Tests/PlanDeck.Integration.Tests.csproj --filter "FullyQualifiedName~SessionPersistenceTests"`
- Server project builds with the updated shared contract: `dotnet build Web/PlanDeck.Server/PlanDeck.Server.csproj`

#### Manual Verification:

- A Member can open a Project's Session list but cannot invoke any administrative Session or participant mutation.
- A user without Project membership receives the same not-found experience for an unknown and an inaccessible Project/Session.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 2: Cascading Project Lifecycle

### Overview

Implement the confirmed destructive lifecycle: one Project deletion removes the full owned relational graph while preserving shared entities and respecting external-secret and realtime ordering.

### Changes Required:

#### 1. Project-to-Session cascade migration

**Files**:
- `src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/Configurations/PlanningSessionConfiguration.cs`
- `src/PlanDeck/Core/PlanDeck.Infrastructure/Migrations/<timestamp>_CascadeProjectSessions.cs`
- `src/PlanDeck/Core/PlanDeck.Infrastructure/Migrations/PlanDeckDbContextModelSnapshot.cs`

**Intent**: Let SQL Server atomically delete Project-owned Sessions and their existing cascading children.

**Contract**: The composite FK `FK_Sessions_Projects_TenantId_ProjectId` changes from `NO ACTION/Restrict` to `CASCADE`; `Down` restores `Restrict`. Existing Project-to-member/team-link/ADO-connection and Session-to-task/member cascades remain unchanged.

#### 2. Project repository and service deletion orchestration

**Files**:
- `src/PlanDeck/Core/PlanDeck.Application/Abstractions/IProjectRepository.cs`
- `src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/ProjectRepository.cs`
- `src/PlanDeck/Core/PlanDeck.Application/Services/ProjectGrpcService.cs`

**Intent**: Remove the obsolete “Project has Sessions” precondition while preserving owner-only deletion and external cleanup guarantees.

**Contract**: Delete no longer calls or exposes `EnsureCanDeleteAsync`. The orchestration identifies owned Sessions via the project-filtered `ISessionRepository` query introduced in Phase 1 (injected into `ProjectGrpcService` — no new lookup method), requires `Owner`, performs required external/realtime cleanup, and then removes the Project once; shared `Team` and `AppUser` records are not deleted.

#### 3. Realtime room invalidation

**Files**:
- `src/PlanDeck/Core/PlanDeck.Application/Planning/IPlanningRoomService.cs`
- `src/PlanDeck/Core/PlanDeck.Application/Planning/PlanningRoomService.cs`
- `src/PlanDeck/Web/PlanDeck.Server/Realtime/SignalRPlanningRoomNotifier.cs`
- `src/PlanDeck/Tests/PlanDeck.Unit.Tests/Planning/PlanningRoomServiceTests.cs`

**Intent**: Prevent deleted Sessions from retaining usable in-memory voting rooms until the normal inactivity TTL.

**Contract**: Add a Session-addressed room removal/invalidation operation that is idempotent, clears room state, and causes connected/new callers to observe that the Session is unavailable. Project deletion applies it to every owned Session after the SQL delete succeeds (best-effort — rooms are never destroyed for Sessions that still exist because the delete failed).

#### 4. Delete confirmation and localization

**Files**:
- `src/PlanDeck/Web/PlanDeck.Client/Pages/Projects.razor`
- `src/PlanDeck/Web/PlanDeck.Client/Pages/Projects.razor.cs`
- `src/PlanDeck/Web/PlanDeck.Client/Resources/SharedResource.resx`
- `src/PlanDeck/Web/PlanDeck.Client/Resources/SharedResource.pl.resx`

**Intent**: Retain the selected single-dialog workflow while making its destructive scope explicit.

**Contract**: The existing confirmation states that the Project, all Sessions, tasks, participants, memberships, links, and ADO configuration will be deleted permanently. One affirmative action invokes the existing delete client contract.

#### 5. Cascade and ordering tests

**Files**:
- `src/PlanDeck/Tests/PlanDeck.Unit.Tests/Projects/ProjectGrpcServiceTests.cs`
- `src/PlanDeck/Tests/PlanDeck.Unit.Tests/Projects/ProjectConnectionGrpcServiceTests.cs`
- `src/PlanDeck/Tests/PlanDeck.Integration.Tests/Persistence/ProjectPersistenceTests.cs`
- `src/PlanDeck/Tests/PlanDeck.Integration.Tests/Persistence/SessionPersistenceTests.cs`

**Intent**: Verify the complete graph, tenant boundary, preserved shared records, and failure ordering.

**Contract**: Tests prove cascade deletion of every Project/Session child, survival of shared Teams/AppUsers, secret cleanup before SQL deletion, no SQL deletion on secret failure, room invalidation after successful SQL deletion (and no invalidation when the delete fails), and migration FK behavior.

### Success Criteria:

#### Automated Verification:

- Project and room unit tests pass: `dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj --filter "FullyQualifiedName~ProjectGrpcServiceTests|FullyQualifiedName~ProjectConnectionGrpcServiceTests|FullyQualifiedName~PlanningRoomServiceTests"`
- Project and Session persistence integration tests pass: `dotnet test Tests/PlanDeck.Integration.Tests/PlanDeck.Integration.Tests.csproj --filter "FullyQualifiedName~ProjectPersistenceTests|FullyQualifiedName~SessionPersistenceTests"`
- Migration applies from the previous schema and its `Down` path restores the restrictive FK in a disposable database.

#### Manual Verification:

- The single confirmation clearly communicates permanent deletion of the Project-owned graph.
- Deleting a Project with active and inactive Sessions removes it from the UI, preserves shared Teams, and makes prior voting-room URLs unavailable.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 3: Project-First UI

### Overview

Replace the flat Projects/Sessions experience with a Project list, Project dashboard, and project-scoped Session management route.

### Changes Required:

#### 1. Entry route and top-level navigation

**Files**:
- `src/PlanDeck/Web/PlanDeck.Client/Pages/Home.razor`
- `src/PlanDeck/Web/PlanDeck.Client/Pages/Home.razor.cs`
- `src/PlanDeck/Web/PlanDeck.Client/Layout/MainLayout.razor`
- `src/PlanDeck/Web/PlanDeck.Client/Resources/SharedResource.resx`
- `src/PlanDeck/Web/PlanDeck.Client/Resources/SharedResource.pl.resx`

**Intent**: Make Projects the authenticated entry point and stop presenting Sessions as an independent module.

**Contract**: `/` routes authenticated non-guests to `/projects`; the top-level Sessions navigation item is removed; Projects and global Teams remain. The exact legacy `/sessions` route is removed without a compatibility redirect.

#### 2. Project list and dashboard

**Files**:
- `src/PlanDeck/Web/PlanDeck.Client/Pages/Projects.razor`
- `src/PlanDeck/Web/PlanDeck.Client/Pages/Projects.razor.cs`
- `src/PlanDeck/Web/PlanDeck.Client/Pages/ProjectDetails.razor`
- `src/PlanDeck/Web/PlanDeck.Client/Pages/ProjectDetails.razor.cs`

**Intent**: Split the current large Projects page into a focused list/create surface and a stable Project dashboard.

**Contract**:
- `/projects` lists and creates Projects.
- `/projects/{ProjectId:guid}` loads one accessible Project and presents Sessions, ADO connection, members, and assigned Teams as Project-owned sections/actions.
- The Sessions action deep-links to `/projects/{ProjectId}/sessions`.
- Missing or inaccessible Projects share one localized not-found/no-access state.
- All C# behavior remains in `.razor.cs` partial classes.

#### 3. Project-scoped Session management

**Files**:
- `src/PlanDeck/Web/PlanDeck.Client/Pages/Sessions.razor`
- `src/PlanDeck/Web/PlanDeck.Client/Pages/Sessions.razor.cs`
- `src/PlanDeck/Web/PlanDeck.Client/Components/AdoImportPanel.razor.cs`

**Intent**: Reuse the existing Session management capabilities inside a required Project route and remove all cross-project selection behavior.

**Contract**:
- The only management route is `/projects/{ProjectId:guid}/sessions`.
- `ProjectId` comes from the route and is passed to list, create, and ADO import calls.
- The create form no longer displays a Project selector or loads all Projects.
- The page verifies the Project, exposes loading/empty/not-found states, and displays its Project context.
- Administrative controls are visible only for effective `Admin`/`Owner`; `Member` receives read/vote UI and no mutation controls.
- A forced lower-role mutation still maps `PermissionDenied` to a localized error without changing client state.

#### 4. Voting and guest navigation

**Files**:
- `src/PlanDeck/Web/PlanDeck.Client/Pages/VotingRoom.razor`
- `src/PlanDeck/Web/PlanDeck.Client/Pages/VotingRoom.razor.cs`
- `src/PlanDeck/Web/PlanDeck.Client/Pages/JoinSession.razor.cs`

**Intent**: Preserve Session-addressed voting and guest links while removing navigation back to the obsolete global route.

**Contract**: The authenticated Voting Room back action uses the loaded Session's `ProjectId` to navigate to `/projects/{projectId}/sessions`. Guest join continues to resolve `/join/{code}` to `/voting/{sessionId}`. Login fallback no longer embeds `/sessions`.

#### 5. UI localization and component tests

**Files**:
- `src/PlanDeck/Web/PlanDeck.Client/Resources/SharedResource.resx`
- `src/PlanDeck/Web/PlanDeck.Client/Resources/SharedResource.pl.resx`
- `src/PlanDeck/Tests/PlanDeck.Unit.Tests/Client/LocalizationResourceParityTests.cs`
- `src/PlanDeck/Tests/PlanDeck.Unit.Tests/Client/SessionRoleUiTests.cs`

**Intent**: Keep every new dashboard, role, destructive action, loading, empty, and error message equivalent in English and Polish, and make route/role/error-mapping behavior testable in the existing NUnit unit-test project (no client component test project exists; component rendering is covered by Phase 5 E2E).

**Contract**: EN/PL resource sets contain matching keys, enforced by a resx key-parity NUnit test reading both resource files. Route-driven state, role-aware control visibility, and `PermissionDenied`-to-localized-error mapping live in testable code-behind/helper methods exercised by NUnit tests in `PlanDeck.Unit.Tests`. No new user-facing string is hard-coded in Razor or code-behind.

### Success Criteria:

#### Automated Verification:

- Client and server build with the new routes and client contracts: `dotnet build Web/PlanDeck.Server/PlanDeck.Server.csproj`
- Route-driven state, role-aware control, and localized-error-mapping unit tests pass: `dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj --filter "FullyQualifiedName~SessionRoleUiTests"`
- Resource key parity between `SharedResource.resx` and `SharedResource.pl.resx` passes: `dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj --filter "FullyQualifiedName~LocalizationResourceParityTests"`

#### Manual Verification:

- Starting at `/` leads an authenticated user to Projects, then to a Project dashboard and its Session list without any Project selector.
- Navigating directly to `/sessions` shows the normal not-found experience rather than a redirect or global list.
- Owner/Admin see Session administration controls, Member sees read/vote controls only, and guest join/voting deep links still work.
- The layout remains usable at the existing mobile viewport breakpoint.

**Known break**: removing the `/sessions` route intentionally breaks the existing E2E tests (`SessionsPage.cs` navigates to `/sessions`); they are rebuilt in Phase 5. Mark affected E2E tests `[Ignore("Superseded by project-first routes; rebuilt in Phase 5")]` in this phase, and exclude the E2E project from Phase 3/4 verification gates.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 4: Deterministic Multi-Role E2E Harness

### Overview

Extend test authentication and deployment controls so three role-specific browser contexts and isolated test scenarios can run safely and repeatably locally and in CI.

### Changes Required:

#### 1. Three deterministic test identities

**Files**:
- `src/PlanDeck/Web/PlanDeck.Server/Testing/TestMemberIdentities.cs`
- `src/PlanDeck/Web/PlanDeck.Server/Testing/TestAppUserSeeder.cs`
- `src/PlanDeck/Web/PlanDeck.Server/Identity/TestAuthenticationHandler.cs`
- `src/PlanDeck/Tests/PlanDeck.Integration.Tests/Identity/TestAppUserSeederTests.cs`

**Intent**: Provide stable Owner, Admin, and Member identities without real Entra authentication.

**Contract**: The test-only identity selector recognizes three explicit cookie values and seeds all three AppUsers. Unknown values fail safely. The scheme remains unavailable outside `Development` and `Testing`.

#### 2. E2E identity-context factory

**Files**:
- `src/PlanDeck/Tests/PlanDeck.E2e.Tests/E2eIdentityContextFactory.cs`
- `src/PlanDeck/Tests/PlanDeck.E2e.Tests/VotingRoomTests.cs`
- other E2E fixtures that currently create browser contexts directly

**Intent**: Centralize isolated Playwright context creation and remove duplicated cookie setup.

**Contract**: The factory creates a fresh context for Owner/Admin/Member, applies the identity cookie for the current base URL, and guarantees disposal. Tests do not mutate one browser context between roles.

#### 3. Test-only scenario seed and cleanup surface

**Files**:
- `src/PlanDeck/Web/PlanDeck.Server/Testing/E2eScenarioEndpoints.cs`
- `src/PlanDeck/Web/PlanDeck.Server/Testing/E2eScenarioService.cs`
- `src/PlanDeck/Web/PlanDeck.Server/Program.cs`
- `src/PlanDeck/Web/PlanDeck.Server/Extensions/ServiceCollectionExtensions.cs`
- `src/PlanDeck/Tests/PlanDeck.E2e.Tests/E2eScenarioClient.cs`

**Intent**: Create and remove complete, unique role scenarios without expensive or order-dependent UI setup.

**Contract**:
- Test-only endpoints seed a Project with accepted Owner/Admin/Member memberships plus configurable Session/tasks/status and return identifiers keyed by a unique `runId`.
- Cleanup removes only data owned by that `runId` and is idempotent.
- Endpoints are mapped only in `Development`/`Testing` when test auth is enabled and require a deployment-level test authorization token in remote runs.
- Production never maps or registers the scenario surface.

#### 4. Local and remote E2E fixture configuration

**Files**:
- `src/PlanDeck/Tests/PlanDeck.E2e.Tests/AspireAppFixture.cs`
- `src/PlanDeck/Tests/PlanDeck.E2e.Tests/.runsettings`
- `src/PlanDeck/Aspire/PlanDeck.AppHost/AppHost.cs`

**Intent**: Make local Aspire and remote Test targets expose the same deterministic harness while preserving the Production guard.

**Contract**: Local runs set test auth and scenario credentials through Aspire. Remote runs require `BaseUrl`, `RemoteEnvironment=Test`, and the test authorization token; Test/Staging ambiguity is removed for the multi-role suite.

#### 5. CI Test deployment and invocation

**Files**:
- `.azuredevops/pipelines/azure-dev.yml`
- deployment configuration files referenced by that pipeline

**Intent**: Run the multi-role E2E suite against a protected `Testing` deployment rather than Production or an unprovisioned local Aspire instance.

**Contract**: The pipeline provisions/targets the dedicated Test environment, passes `BaseUrl`, `RemoteEnvironment=Test`, and the secret test token, installs Chromium, executes the E2E project, publishes results, and tears down or cleans scenario data according to the existing deployment lifecycle.

#### 6. Harness security and lifecycle tests

**Files**:
- `src/PlanDeck/Tests/PlanDeck.Integration.Tests/Identity/ProductionAuthenticationConfigurationTests.cs`
- `src/PlanDeck/Tests/PlanDeck.Integration.Tests/Identity/TestAppUserSeederTests.cs`
- new integration tests for scenario endpoint registration and authorization

**Intent**: Prove that deterministic test capabilities cannot leak into Production and that cleanup is isolated.

**Contract**: Tests cover environment guards, token rejection, three identities, unique `runId` isolation, idempotent cleanup, and absence of scenario routes in Production.

### Success Criteria:

#### Automated Verification:

- Test-auth and scenario integration tests pass: `dotnet test Tests/PlanDeck.Integration.Tests/PlanDeck.Integration.Tests.csproj --filter "FullyQualifiedName~TestAppUserSeederTests|FullyQualifiedName~ProductionAuthenticationConfigurationTests|FullyQualifiedName~E2eScenario"`
- E2E project builds and discovers the three-role fixture without contacting real Entra: `dotnet test Tests/PlanDeck.E2e.Tests/PlanDeck.E2e.Tests.csproj --list-tests`
- Pipeline configuration supplies `RemoteEnvironment=Test` and does not expose the test token in logs.

#### Manual Verification:

- A local Aspire run can open three simultaneous browser contexts that are recognized as Owner, Admin, and Member.
- The protected remote Test deployment accepts authorized scenario setup/cleanup and rejects missing or invalid test credentials.
- Production does not expose test authentication or scenario endpoints.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 5: Full Role Matrix E2E and Regression

### Overview

Rebuild the page objects around Project-first navigation and enforce the selected behavior through browser-level role, deletion, deep-link, and regression scenarios.

### Changes Required:

#### 1. Project-first page objects

**Files**:
- `src/PlanDeck/Tests/PlanDeck.E2e.Tests/Pages/ProjectsPage.cs`
- `src/PlanDeck/Tests/PlanDeck.E2e.Tests/Pages/ProjectDetailsPage.cs`
- `src/PlanDeck/Tests/PlanDeck.E2e.Tests/Pages/SessionsPage.cs`
- `src/PlanDeck/Tests/PlanDeck.E2e.Tests/Pages/SessionMembersPage.cs`
- `src/PlanDeck/Tests/PlanDeck.E2e.Tests/Pages/VotingRoomPage.cs`

**Intent**: Model the new route hierarchy and role-visible controls without CSS, XPath, structural locators, fixed waits, or network-idle assumptions.

**Contract**: Page objects navigate through Project IDs, use accessible role/label/text locators, wait for observable UI state, and expose actions/assertions without embedding test orchestration.

#### 2. Owner/Admin/Member mutation matrix

**Files**:
- `src/PlanDeck/Tests/PlanDeck.E2e.Tests/SessionAuthorizationMatrixTests.cs`
- `src/PlanDeck/Tests/PlanDeck.E2e.Tests/SessionsTests.cs`
- `src/PlanDeck/Tests/PlanDeck.E2e.Tests/SessionMembersTests.cs`

**Intent**: Cover every confirmed important Session mutation for all three roles in the browser.

**Contract**: For Session creation, configuration, single-task add, bulk add, ADO import, task edit, task removal, activation, ADO write-back, participant assignment/removal, and Session deletion:
- Owner succeeds and the result persists.
- Admin succeeds and the result persists.
- Member has no administrative control; a direct/forced request is denied and persisted state remains unchanged.

Every scenario owns a unique `runId`, starts from seeded state, reloads before persistence assertions, and cleans up in `finally`/teardown.

#### 3. Project deletion E2E

**Files**:
- `src/PlanDeck/Tests/PlanDeck.E2e.Tests/ProjectsTests.cs`
- `src/PlanDeck/Tests/PlanDeck.E2e.Tests/Pages/ProjectDetailsPage.cs`

**Intent**: Verify the selected single-confirmation cascade behavior from the user-facing workflow.

**Contract**: The test confirms the warning text, deletes a Project containing Sessions and child data, verifies the Project and Session routes are unavailable, and verifies a shared Team remains usable.

#### 4. Routing and voting regressions

**Files**:
- `src/PlanDeck/Tests/PlanDeck.E2e.Tests/SessionsTests.cs`
- `src/PlanDeck/Tests/PlanDeck.E2e.Tests/VotingRoomTests.cs`
- `src/PlanDeck/Tests/PlanDeck.E2e.Tests/GuestVotingTests.cs`

**Intent**: Protect Project-first deep links without regressing the intentionally Session-scoped voting and guest flows.

**Contract**: Tests cover `/` to Projects, direct Project/Session URLs, `/sessions` not-found, Voting Room back to the owning Project's Session list, unchanged `/join/{code}` and `/voting/{sessionId}`, and Member voting participation.

#### 5. Full solution validation

**Files**:
- test projects and pipeline artifacts affected by the preceding phases

**Intent**: Establish one final quality gate for the atomically deployed client/server contract and all data/auth/UI behavior.

**Contract**: The solution build, non-E2E suites, local E2E smoke path, and remote Test E2E matrix all complete without relying on execution order or residual data.

### Success Criteria:

#### Automated Verification:

- Unit tests pass: `dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj`
- Integration tests pass: `dotnet test Tests/PlanDeck.Integration.Tests/PlanDeck.Integration.Tests.csproj`
- Full E2E suite passes locally through Aspire: `dotnet test Tests/PlanDeck.E2e.Tests/PlanDeck.E2e.Tests.csproj`
- Full E2E suite passes against the protected Test deployment with `BaseUrl` and `RemoteEnvironment=Test`.
- Entire solution builds: `dotnet build PlanDeck.slnx`

#### Manual Verification:

- A real Owner, Admin, and Member can complete their intended Project/Session workflows in English and Polish.
- The Project-first navigation, single-dialog cascade deletion, direct links, mobile layout, and guest voting behavior match the approved product decisions.
- Pipeline results clearly distinguish setup failures, authorization failures, browser failures, and application assertions.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before considering the change complete.

---

## Testing Strategy

### Unit Tests:

- Validate `ProjectId` request requirements and repository call shape.
- Exercise configurable Owner/Admin/Member/absent-role paths for every Session and Session-member operation.
- Verify `NotFound` concealment versus `PermissionDenied` for insufficient roles.
- Verify Project deletion ordering across Key Vault, room invalidation, and repository deletion.
- Verify room invalidation is idempotent and removes active state.

### Integration Tests:

- Prove Project A cannot list or address Project B Sessions inside one tenant.
- Prove database cascade removes every Project/Session child while preserving shared Team/AppUser rows.
- Verify migration `Up`/`Down`, tenant isolation, and failure rollback.
- Verify test-only endpoints and authentication are unavailable in Production and protected in Testing.
- Verify deterministic scenario setup/cleanup is isolated by `runId`.

### E2E Tests:

- Use the Page Object Pattern and accessible locators only.
- Run every important Session mutation for Owner, Admin, and Member using independent browser contexts.
- Assert both UI control visibility and server-enforced denial/no persisted change.
- Cover Project cascade deletion, Project-first routing, Voting Room return navigation, mobile rendering, and unchanged guest voting.
- Use unique scenario IDs and state-based waits; never use fixed timeouts or shared test order.

### Manual Testing Steps:

1. Create a Project as Owner, invite one Admin and one Member, and verify the dashboard presents Sessions, ADO, members, and assigned Teams.
2. As Owner and Admin, create and administer Sessions under the Project route.
3. As Member, list Sessions and vote while confirming all mutation controls are absent.
4. Open a Voting Room directly, return to the owning Project's Session list, and repeat through a guest join link.
5. Delete a populated Project through the single confirmation and verify shared Teams remain.
6. Switch EN/PL and mobile/desktop layouts across the Project dashboard and Session page.

## Performance Considerations

- Project-scoped listing replaces N+1 access checks with one role lookup and one indexed Session query.
- The existing `(TenantId, ProjectId, CreatedAtUtc)` index must remain aligned with the repository filter and ordering.
- Cascade deletion can remove a large graph in one transaction; integration tests should include a representative multi-Session Project and ensure command timeouts remain acceptable.
- E2E scenario setup should seed through one bounded backend operation rather than repeated UI calls, and test cases must avoid unnecessary serialization while keeping each scenario isolated.

## Migration Notes

- Add one EF Core migration that replaces `FK_Sessions_Projects_TenantId_ProjectId` from `Restrict` to `Cascade`; no data backfill is required.
- The migration `Down` restores `Restrict`, but rollback cannot recover Projects already deleted under cascade semantics. Deployment requires normal database backup/PITR readiness.
- Client and server must deploy atomically because an old client sends an empty `ListSessionsRequest`, which the new server rejects. Browser tabs still running the previously cached WASM client will transiently receive `InvalidArgument` until reloaded — expected post-deploy behavior, not a bug.
- Existing `Member` users lose Session mutation rights without a data migration; this is an approved behavior change.
- The legacy `/sessions` URL intentionally becomes not found; no compatibility redirect is provided. Existing E2E tests that navigate `/sessions` are `[Ignore]`d from Phase 3 until the Phase 5 rebuild.
- Test environment changes must land before enabling the full role-matrix pipeline gate.

## References

- Related research: `context/changes/reorganize-project-and-sessions/research.md`
- Change definition: `context/changes/reorganize-project-and-sessions/change.md`
- Prior Project access model: `context/archive/2026-07-21-secure-ado-grpc-endpoints/plan.md`
- Session contract: `src/PlanDeck/Core/PlanDeck.Core.Shared/Contracts/ISessionService.cs:203`
- Session authorization: `src/PlanDeck/Core/PlanDeck.Application/Services/SessionGrpcService.cs:117`
- Session-member authorization gap: `src/PlanDeck/Core/PlanDeck.Application/Services/SessionMemberGrpcService.cs:10`
- Project deletion orchestration: `src/PlanDeck/Core/PlanDeck.Application/Services/ProjectGrpcService.cs:191`
- Project-to-Session FK: `src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/Configurations/PlanningSessionConfiguration.cs:42`
- Current Project/Sessions navigation: `src/PlanDeck/Web/PlanDeck.Client/Layout/MainLayout.razor:20`
- Current Session page: `src/PlanDeck/Web/PlanDeck.Client/Pages/Sessions.razor:1`
- E2E environment guard: `src/PlanDeck/Tests/PlanDeck.E2e.Tests/AspireAppFixture.cs:19`
- Test identities: `src/PlanDeck/Web/PlanDeck.Server/Testing/TestMemberIdentities.cs:1`
- Pipeline E2E invocation: `.azuredevops/pipelines/azure-dev.yml:59`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Project-Scoped Backend and Authorization

#### Automated

- [x] 1.1 Session service and Session-member unit tests pass — 63ccac6
- [x] 1.2 Session persistence integration tests pass — 63ccac6
- [x] 1.3 Server project builds with the updated shared contract — 63ccac6

#### Manual

- [ ] 1.4 Member can read and vote but cannot perform Session administration
- [ ] 1.5 Inaccessible and unknown resources share the not-found experience

### Phase 2: Cascading Project Lifecycle

#### Automated

- [x] 2.1 Project and room unit tests pass
- [x] 2.2 Project and Session persistence integration tests pass
- [x] 2.3 Cascade migration Up and Down paths succeed

#### Manual

- [ ] 2.4 Single confirmation communicates permanent graph deletion
- [ ] 2.5 Populated Project deletion preserves shared Teams and invalidates voting rooms

### Phase 3: Project-First UI

#### Automated

- [ ] 3.1 Client and server build with the new routes and contracts
- [ ] 3.2 Route, role-control, and localized-error unit tests pass
- [ ] 3.3 English and Polish resource key parity is preserved

#### Manual

- [ ] 3.4 Project-first navigation works without a Project selector
- [ ] 3.5 Legacy sessions route shows the normal not-found experience
- [ ] 3.6 Role-aware controls and guest deep links behave as approved
- [ ] 3.7 Existing mobile viewport remains usable

### Phase 4: Deterministic Multi-Role E2E Harness

#### Automated

- [ ] 4.1 Test-auth and scenario integration tests pass
- [ ] 4.2 E2E project discovers the three-role fixture without real Entra
- [ ] 4.3 Pipeline supplies protected Test parameters without leaking secrets

#### Manual

- [ ] 4.4 Local Aspire recognizes simultaneous Owner, Admin, and Member contexts
- [ ] 4.5 Remote Testing scenario setup and cleanup enforce test credentials
- [ ] 4.6 Production exposes no test authentication or scenario endpoints

### Phase 5: Full Role Matrix E2E and Regression

#### Automated

- [ ] 5.1 Unit test suite passes
- [ ] 5.2 Integration test suite passes
- [ ] 5.3 Full local Aspire E2E suite passes
- [ ] 5.4 Full protected remote Test E2E suite passes
- [ ] 5.5 Entire solution builds

#### Manual

- [ ] 5.6 Real Owner, Admin, and Member workflows work in English and Polish
- [ ] 5.7 Navigation, cascade deletion, direct links, mobile layout, and guest voting match approved behavior
- [ ] 5.8 Pipeline failures remain diagnosable by layer
