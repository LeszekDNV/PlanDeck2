# Secure Project-Scoped Azure DevOps Endpoints Implementation Plan

## Overview

Replace the global, anonymously reachable Azure DevOps integration with a project-owned security
boundary. A PlanDeck project owns one Azure DevOps connection, stores its PAT in Azure Key Vault,
groups sessions, and grants access through Owner/Admin/Member roles plus tenant-team assignments.
Every Azure DevOps import and write-back must derive its connection from an authorized project or
session; callers can no longer select arbitrary work-item metadata or invoke a raw write RPC.

This change also closes the deployment bypass that currently publishes the application with the
Testing environment and test authentication enabled. Production must fail closed, while local
Aspire and local E2E runs use a real development Azure Key Vault provisioned by Aspire.

## Current State Analysis

- All gRPC services are mapped without endpoint authorization in
  `src/PlanDeck/Web/PlanDeck.Server/Program.cs:149-155`.
- `GuestAccessGuard.RejectGuests` rejects guests but not anonymous callers
  (`src/PlanDeck/Core/PlanDeck.Application/Services/GuestAccessGuard.cs:13-20`).
- Anonymous identities resolve to `TenantId = Guid.Empty` and `UserId = Guid.Empty`, while
  `IsGuest` remains false (`src/PlanDeck/Web/PlanDeck.Server/Identity/HttpContextCurrentUserContext.cs:14-18,37-48`).
- `IAzureDevOpsWorkItemService` exposes caller-controlled import and raw write operations
  (`src/PlanDeck/Core/PlanDeck.Core.Shared/Contracts/IAzureDevOpsWorkItemService.cs:8-15,62-83`).
- `AzureDevOpsWorkItemClient` uses one global organization, project, field mapping, and PAT and can
  PATCH any positive work-item ID in the organization
  (`src/PlanDeck/Core/PlanDeck.Infrastructure/AzureDevOps/AzureDevOpsWorkItemClient.cs:80-106,213-239`).
- Session creation and task-add contracts trust client-supplied `Source`, `AdoWorkItemId`, revision,
  title, and other ADO metadata (`ISessionService.cs:131-153`;
  `SessionGrpcService.cs:42-52,135-190,456-474`).
- Session write-back safely derives the ID and revision from a persisted `SessionTask`, but the task
  itself may have been forged and the operation still resolves the global PAT
  (`SessionGrpcService.cs:254-315`).
- Tenant query filters protect local rows, but there is no project aggregate, project membership, or
  project authorization (`PlanDeckDbContext.cs:29-63`).
- `AppUser` has no Entra object ID or normalized identity constraints and is not provisioned during
  sign-in (`AppUser.cs:3-8`; `AppUserConfiguration.cs:17-25`).
- Sessions have optional `TeamId` and no project owner (`PlanningSession.cs:3-19`).
- Teams are tenant-wide and membership is email-based (`TeamMember.cs:3-12`).
- Session members are also email-based and are used to authorize specific voting-room participants
  (`VotingRoundService.cs:69-84`). This remains a separate participant concern, not project access.
- Publish mode forces `ASPNETCORE_ENVIRONMENT=Testing` and
  `Authentication__UseTestScheme=true` (`src/PlanDeck/Aspire/PlanDeck.AppHost/AppHost.cs:40-46`).
- Aspire provisions Key Vault only in publish mode today (`AppHost.cs:9-37`); the server has no
  Key Vault client integration package or `SecretClient` registration.

## Desired End State

An authenticated Entra user is provisioned as an internal `AppUser` and sees only projects they can
access directly or through an assigned tenant team. Creating a project atomically makes the creator
its Owner. Owners manage the immutable Azure DevOps organization/project target, PAT, field mapping,
ownership, and deletion. Admins manage project members and assigned teams. Members can create and
run sessions and use the project's Azure DevOps integration.

Every session has an immutable `ProjectId`; `TeamId` is removed from sessions. Import requires a
project and membership, and adding imported tasks sends only work-item IDs. The server re-fetches
those IDs through the project's connection and persists authoritative ADO metadata. Write-back
loads the session and task, resolves the same project's connection, and never accepts a raw target
from the caller. The public raw write operation no longer exists.

Azure stores only the PAT secret. SQL stores project metadata and an opaque Key Vault secret name,
never the PAT or authorization header. Local Aspire, local integration/E2E tests, and deployed
environments receive a real Key Vault reference from Aspire. Production cannot start with test
authentication.

### Key Discoveries:

- Tenant filtering is necessary defense-in-depth but cannot express project membership
  (`PlanDeckDbContext.cs:59-63`).
- Team assignments must grant exactly Member; direct membership controls Owner/Admin and takes
  precedence, preventing privilege escalation through teams.
- The existing `SessionMember` slice is an intentional session-participant feature and should be
  retained. It can authorize a specific authenticated voter but must never grant project or ADO
  access.
- `PlanningRoomHub` already has a mixed member/guest policy and strict guest `sid` checks
  (`PlanningRoomHub.cs:9-32,214-259`); project-aware participant resolution should reuse that
  boundary rather than duplicate claims logic.
- Aspire 13.4.6 has no supported local Key Vault emulator API. The approved local path is a real
  development Key Vault provisioned by `AddAzureKeyVault`, accessed through
  `DefaultAzureCredential`.
- `AddAzureKeyVault` defaults to broad role assignment. The plan must clear defaults and assign the
  minimum role that still supports application-owned get/set/delete operations.
- Existing data may be reset for this MVP. Historical migrations remain intact; one explicit new
  migration performs the irreversible session reset and schema transition.

## What We're NOT Doing

- Not implementing per-field or per-operation custom ACLs beyond Owner/Admin/Member.
- Not granting Admins control of the ADO target, PAT, ownership transfer, or project deletion.
- Not allowing a team assignment to grant Admin or Owner.
- Not storing PATs, partial PATs, secret values, or authorization headers in SQL, DTOs, logs, or
  client state.
- Not exposing Key Vault secret names or versions through gRPC.
- Not retaining support for the global `AzureDevOpsOptions.PersonalAccessToken`.
- Not retaining the public raw `WriteEstimateAsync(workItemId, revision, estimate)` RPC.
- Not trusting ADO title, revision, type, state, or description sent by the browser.
- Not supporting a PlanDeck project's migration to another ADO organization/project after its first
  task import. A different target requires a new PlanDeck project.
- Not replacing session-specific participant assignment or guest share links with project
  membership.
- Not introducing a Key Vault emulator or fake vault for local E2E. Unit tests may use an
  `IProjectSecretStore` fake.
- Not preserving existing session data during the MVP schema reset. Rollback requires backup restore.

## Implementation Approach

Implement the security boundary from the outside in:

1. Establish valid member and room identities and require authorization on endpoint mappings.
2. Add project, membership, team-assignment, and ADO-connection persistence with reusable access
   resolvers.
3. Integrate a real Aspire-provisioned Key Vault and owner-only connection lifecycle.
4. Make sessions and ADO operations project-scoped, remove caller-controlled write paths, and
   re-fetch imported work items server-side.
5. Add the project administration and project-first session UX.
6. Prove the complete role and isolation matrix through unit, SQL, transport, and browser tests,
   then deploy with an explicit irreversible reset procedure.

Repositories keep tenant predicates and composite tenant/resource relationships as defense-in-depth.
Application access resolvers own project/session permission decisions. Endpoint policies reject
invalid identity shapes before business code. The client controls presentation only and is never an
authorization boundary.

## Critical Implementation Details

### Timing & lifecycle

Project creation spans Key Vault and SQL, which cannot share a transaction. Validate the ADO target
and PAT in memory, create the opaque Key Vault secret, then commit the project and Owner membership
in SQL; if SQL fails, synchronously compensate by deleting the newly created secret and surface the
failure. PAT rotation writes a new Key Vault version under the same opaque name and invalidates the
in-memory cache. Project deletion removes SQL metadata only after Key Vault soft-delete starts
successfully.

### State sequencing

The ADO organization/project target becomes immutable when the first selected work item is
successfully re-fetched and persisted into a session. Record that transition on the connection in
the same SQL transaction as the imported tasks so concurrent target edits cannot race the first
import.

### Debug & observability

Log project ID, operation, ADO status category, secret operation type, and correlation identifiers,
but never PAT values, Basic authorization headers, secret names, upstream response bodies, or
caller-supplied WIQL. Key Vault/ADO authorization failures must map to typed application failures and
sanitized gRPC details.

## Phase 1: Fail-Closed Identity and Endpoint Authorization

### Overview

Make authenticated member and room identities explicit, provision stable internal users, remove the
published test-auth bypass, and enforce authorization at every gRPC/SignalR boundary.

### Changes Required:

#### 1. Internal user identity and provisioning

**Files**:
- `src/PlanDeck/Core/PlanDeck.Application/Domain/AppUser.cs`
- `src/PlanDeck/Core/PlanDeck.Application/Abstractions/IAppUserRepository.cs` (new)
- `src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/Configurations/AppUserConfiguration.cs`
- `src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/AppUserRepository.cs` (new)
- `src/PlanDeck/Web/PlanDeck.Server/Identity/IAppUserProvisioner.cs` (new)
- `src/PlanDeck/Web/PlanDeck.Server/Identity/AppUserProvisioner.cs` (new)

**Intent**: Resolve every member to a stable internal `AppUserId` backed by Entra `tid` + `oid`, and
accept pending email invitations during successful sign-in without authorizing by mutable email.

**Contract**: `AppUser` gains `EntraObjectId`, `NormalizedEmail`, and active state. Enforce unique
`(TenantId, EntraObjectId)` and normalized-email identity constraints. OIDC token validation upserts
the user, resolves pending project/team invitations transactionally, and places an internal
`plandeck_user_id` claim into the member cookie. Test authentication emits deterministic external
and internal IDs.

#### 2. Current actor and identity-shape validation

**Files**:
- `src/PlanDeck/Core/PlanDeck.Application/Abstractions/ICurrentUserContext.cs`
- `src/PlanDeck/Web/PlanDeck.Server/Identity/HttpContextCurrentUserContext.cs`
- `src/PlanDeck/Web/PlanDeck.Server/Identity/TestAuthenticationHandler.cs`
- `src/PlanDeck/Web/PlanDeck.Server/Identity/GuestAuthentication.cs`

**Intent**: Stop representing missing or malformed identity claims as valid empty GUIDs and provide a
single member/guest actor model for HTTP, gRPC, and SignalR.

**Contract**: Member identity requires valid `tid`, `oid`, and `plandeck_user_id`; guest identity
requires valid `tid`, participant `oid`, `sid`, and `is_guest=true`. Invalid required claims fail
authentication as `Unauthenticated`. Application audit fields use internal `AppUserId`, not Entra
`oid`. Guest session scope remains separate.

#### 3. Authorization policies and endpoint mappings

**Files**:
- `src/PlanDeck/Web/PlanDeck.Server/Identity/PlanDeckPolicies.cs` (new)
- `src/PlanDeck/Web/PlanDeck.Server/Extensions/ServiceCollectionExtensions.cs`
- `src/PlanDeck/Web/PlanDeck.Server/Program.cs`
- `src/PlanDeck/Web/PlanDeck.Server/Hubs/PlanningRoomHub.cs`

**Intent**: Reject anonymous and malformed callers before they reach application services while
preserving the authenticated guest's own-session read/vote path.

**Contract**:
- `MemberAccount`: valid, active non-guest member identity.
- `RoomIdentity`: valid MemberAccount or valid session-scoped guest.
- Project, team, session-member, and ADO service mappings require `MemberAccount`.
- Session service and planning-room hub require `RoomIdentity`; application checks continue to
  restrict guests to their own active session.
- Auth current-user RPC and `/guest/join` remain anonymous.
- Remove or protect the scaffold `HelloGrpcService`.
- Authentication failure returns `Unauthenticated`; a valid identity lacking permission returns
  `PermissionDenied` or concealed `NotFound` according to resource visibility.

#### 4. Fail-closed environment configuration

**Files**:
- `src/PlanDeck/Aspire/PlanDeck.AppHost/AppHost.cs`
- `src/PlanDeck/Web/PlanDeck.Server/Extensions/ServiceCollectionExtensions.cs`
- deployment configuration referenced by `src/PlanDeck/azure.yaml`
- `src/PlanDeck/.env.example`

**Intent**: Prevent Azure deployments from silently enabling the test scheme or fake ADO client.

**Contract**: Publish mode no longer forces `Testing` or `Authentication__UseTestScheme=true`.
Production requires complete Entra settings and fails startup when missing. Test auth remains
explicitly opt-in only for Development/Testing and the E2E fixture. Separate configuration names
identify local development, local E2E, pilot, and production.

#### 5. Identity and transport tests

**Files**:
- existing identity/auth unit tests
- `src/PlanDeck/Tests/PlanDeck.Integration.Tests/` transport test fixture (extend)
- new gRPC-Web authentication integration tests

**Intent**: Prove that policy mappings, not just service guards, reject invalid callers.

**Contract**: Cover anonymous, malformed claims, member, guest, stale guest plus member cookie,
test-scheme environment restrictions, and production startup without Entra configuration.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build PlanDeck.slnx`
- Unit tests pass: `dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj`
- gRPC-Web authentication tests prove anonymous/malformed callers cannot reach protected services
- Production configuration test proves test authentication and incomplete Entra settings fail closed

#### Manual Verification:

- Member login creates/updates one stable AppUser and does not duplicate on repeat login
- Guest share-link voting still works only for the guest's scoped active session
- Published Aspire manifest no longer contains forced Testing/test-auth settings

**Implementation Note**: After completing this phase and all automated verification passes, pause
for manual confirmation before proceeding to Phase 2.

---

## Phase 2: Project Aggregate, Membership, and MVP Schema Reset

### Overview

Create the project security aggregate, role model, direct and team-derived access, and required
session ownership. Apply the approved destructive reset of existing session data.

### Changes Required:

#### 1. Project domain model

**Files**:
- `src/PlanDeck/Core/PlanDeck.Application/Domain/PlanDeckProject.cs` (new)
- `src/PlanDeck/Core/PlanDeck.Application/Domain/ProjectMember.cs` (new)
- `src/PlanDeck/Core/PlanDeck.Application/Domain/ProjectTeam.cs` (new)
- `src/PlanDeck/Core/PlanDeck.Application/Domain/ProjectRole.cs` (new)
- `src/PlanDeck/Core/PlanDeck.Application/Domain/InvitationStatus.cs` (new)
- `src/PlanDeck/Core/PlanDeck.Application/Domain/PlanningSession.cs`
- `src/PlanDeck/Core/PlanDeck.Application/Domain/TeamMember.cs`

**Intent**: Make the PlanDeck project the owner of sessions and the authorization boundary for ADO,
without conflating it with an Azure DevOps project.

**Contract**:
- `PlanDeckProject`: tenant-scoped name, description, audit fields.
- `ProjectMember`: project, optional resolved AppUser, normalized invitation email, Owner/Admin/Member,
  Pending/Accepted, inviter and acceptance audit.
- `ProjectTeam`: many-to-many assignment between tenant-wide projects and teams; always grants
  Member only.
- `PlanningSession.ProjectId` is required and immutable; remove `TeamId`.
- Resolve `TeamMember` to AppUserId with pending invitation support so inherited project membership
  never authorizes by email at request time.
- Retain `SessionMember` as participant-only state; it does not grant project or ADO permissions.

#### 2. EF configuration and relational integrity

**Files**:
- `src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/PlanDeckDbContext.cs`
- new project/member/team configurations under
  `src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/Configurations/`
- existing AppUser, TeamMember, PlanningSession, SessionMember, and SessionTask configurations

**Intent**: Enforce role, tenant, ownership, and relationship invariants in SQL as well as application
logic.

**Contract**:
- Composite tenant/resource alternate keys and foreign keys prevent malformed cross-tenant links.
- Exactly one accepted/resolved Owner per project is maintained through filtered uniqueness and
  transactional ownership transfer.
- Unique accepted direct member and pending invitation constraints are case-normalized.
- Unique `(TenantId, ProjectId, TeamId)` assignment.
- Project deletion is restricted while sessions exist; member/team joins cascade.
- Add `(TenantId, ProjectId, CreatedAtUtc)` session-list index.
- Pending invitations grant no access; team rows have no role column.

#### 3. Project repositories and access resolvers

**Files**:
- `src/PlanDeck/Core/PlanDeck.Application/Abstractions/IProjectRepository.cs` (new)
- `src/PlanDeck/Core/PlanDeck.Application/Abstractions/IProjectAccessResolver.cs` (new)
- `src/PlanDeck/Core/PlanDeck.Application/Abstractions/ISessionAccessResolver.cs` (new)
- corresponding Infrastructure implementations

**Intent**: Centralize resource authorization so gRPC services, repositories, and SignalR do not
reimplement role logic.

**Contract**:
- Effective role is direct accepted role when present; otherwise assigned-team membership grants
  Member; otherwise no access.
- Owner: connection target/PAT, ownership transfer, deletion, all Admin/Member actions.
- Admin: direct Member/Admin invitations and role changes, member removal, team assignment, all
  Member actions; never mutate Owner or ADO connection.
- Member: project read, session create/manage/use, ADO use.
- Explicit inaccessible cross-project/cross-tenant resources return `NotFound`; visible project with
  insufficient role returns `PermissionDenied`.
- Guest session access derives project only internally and never exposes project/ADO permissions.

#### 4. Project gRPC contract and service

**Files**:
- `src/PlanDeck/Core/PlanDeck.Core.Shared/Contracts/IProjectService.cs` (new)
- `src/PlanDeck/Core/PlanDeck.Application/Services/ProjectGrpcService.cs` (new)
- `src/PlanDeck/Web/PlanDeck.Server/Extensions/ServiceCollectionExtensions.cs`
- `src/PlanDeck/Web/PlanDeck.Server/Program.cs`

**Intent**: Expose project lifecycle, membership, team assignment, ownership transfer, and effective
role without exposing secret references.

**Contract**: Operations create/list/get projects; invite/remove/change direct members; assign or
unassign teams; transfer ownership; and delete a project. Creation atomically creates the accepted
Owner membership for the current AppUser. DTOs expose role, invitation state, and membership source,
but no PAT or Key Vault identifier.

#### 5. Destructive MVP migration

**Files**:
- new generated migration under
  `src/PlanDeck/Core/PlanDeck.Infrastructure/Migrations/`
- `PlanDeckDbContextModelSnapshot.cs`

**Intent**: Move directly to required project ownership without preserving incompatible session data.

**Contract**: The migration explicitly deletes existing sessions (cascading tasks and session
members), creates project tables and identity fields, drops `Sessions.TeamId`, adds required
`Sessions.ProjectId` without a default after the table is empty, and creates all checks, composite
FKs, and indexes. Historical migrations are not squashed. The migration documentation marks rollback
as restore-from-backup only.

#### 6. Project model tests

**Files**:
- new unit tests under `Tests/PlanDeck.Unit.Tests/Projects/`
- new/updated persistence tests under `Tests/PlanDeck.Integration.Tests/Persistence/`

**Intent**: Prove role invariants, invitation activation, inherited membership, tenant isolation, and
the irreversible reset shape.

**Contract**: Cover one Owner, atomic transfer, Admin boundaries, pending invitations, direct-role
precedence, team-derived Member only, removal fallback to inherited access, cross-tenant composite
FK rejection, required ProjectId, removed TeamId, and migration from the current schema.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build PlanDeck.slnx`
- Project role-matrix unit tests pass
- Persistence tests prove tenant/project isolation and relational constraints
- Migration applies from the current schema and produces required `ProjectId` with no `TeamId`

#### Manual Verification:

- Schema review confirms PAT/secret-value columns do not exist
- Owner/Admin/Member capabilities match the approved matrix
- Backup and irreversible reset procedure is accepted before migration is used outside disposable data

**Implementation Note**: Pause for manual migration and role-matrix confirmation before Phase 3.

---

## Phase 3: Project-Owned Azure DevOps Connection and Real Key Vault

### Overview

Provision and consume a real Azure Key Vault in every Aspire environment, then implement the
owner-only project connection lifecycle with secure secret handling.

### Changes Required:

#### 1. Connection metadata model

**Files**:
- `src/PlanDeck/Core/PlanDeck.Application/Domain/ProjectAzureDevOpsConnection.cs` (new)
- EF configuration and repository files (new)
- `PlanDeckDbContext.cs`

**Intent**: Store only non-secret ADO configuration and opaque secret reference in SQL.

**Contract**: One optional connection per project containing organization URL, ADO project,
field mappings, opaque Key Vault secret name, enabled/validation state, validation timestamp, and
`TargetLockedAtUtc`. Never serialize the secret name to clients. Organization/project becomes
immutable once `TargetLockedAtUtc` is set; PAT and field mappings remain rotatable.

#### 2. Secret-store abstraction and Key Vault implementation

**Files**:
- `src/PlanDeck/Core/PlanDeck.Application/Abstractions/IProjectSecretStore.cs` (new)
- `src/PlanDeck/Core/PlanDeck.Infrastructure/AzureDevOps/KeyVaultProjectSecretStore.cs` (new)
- `src/PlanDeck/Core/PlanDeck.Infrastructure/PlanDeck.Infrastructure.csproj`
- `src/PlanDeck/Web/PlanDeck.Server/PlanDeck.Server.csproj`

**Intent**: Keep Azure SDK types in Infrastructure and make secret reads/writes testable without
leaking PATs into configuration.

**Contract**: Use the Aspire Key Vault client integration/`SecretClient` and
`DefaultAzureCredential`. Generate names such as `pat-{Guid:N}`; use one name per project connection
and Key Vault versions for rotation. Support create/latest-read/rotate/soft-delete and cache
invalidation. Cache latest PAT briefly by opaque name; on ADO 401, evict and retry the secret read
once. Sanitized typed exceptions distinguish unavailable, forbidden, and missing secrets.

#### 3. Aspire Key Vault for local, E2E, pilot, and production

**Files**:
- `src/PlanDeck/Aspire/PlanDeck.AppHost/AppHost.cs`
- `src/PlanDeck/Aspire/PlanDeck.AppHost/PlanDeck.AppHost.csproj`
- server DI startup
- E2E `AspireAppFixture`

**Intent**: Ensure local development and local E2E exercise a real development vault provisioned by
Aspire, while preserving strict environment separation.

**Contract**:
- Declare `AddAzureKeyVault("key-vault")` outside the publish-only branch and reference it from the
  server in local and publish modes.
- Register `AddAzureKeyVaultClient("key-vault")` in the server.
- Clear broad default role assignments and grant only the data-plane capability required for
  application get/set/delete operations; do not disable soft delete or purge protection.
- Local and E2E require authenticated Azure developer credentials and a dedicated non-production
  Azure environment/subscription/resource naming scope.
- E2E waits for the Key Vault resource and creates/rotates/deletes secrets through the public owner
  flow; no E2E fake resolver.
- Unit tests use an in-memory `IProjectSecretStore`; isolated tests that do not exercise vault
  integration may replace only this abstraction explicitly.

#### 4. Owner-only connection lifecycle

**Files**:
- project contract DTOs/operations
- `ProjectGrpcService.cs`
- connection repository and ADO validation abstractions

**Intent**: Let the project Owner configure, validate, rotate, disable, and remove the connection
without exposing the PAT or allowing Admin mutation.

**Contract**:
- Create validates URL scheme/host, project, fields, and PAT against ADO before writing the secret.
- Project+Owner membership and SQL metadata commit atomically after secret creation; SQL failure
  triggers synchronous secret cleanup.
- Rotation validates the new PAT then creates a new Key Vault version and evicts cache.
- Organization/project edits fail after `TargetLockedAtUtc`; changing target requires a new PlanDeck
  project.
- Delete requires Owner, starts Key Vault soft-delete, then removes metadata/project as allowed.
- Responses expose connection status and last validation only.

#### 5. Key Vault and connection tests

**Files**:
- unit tests for orchestration and compensation
- integration tests using the Aspire-provisioned development vault

**Intent**: Prove no secret leaks, role enforcement, cache/rotation behavior, and local/production
configuration parity.

**Contract**: Cover Owner success, Admin/Member denial, invalid PAT with no secret, SQL failure
compensation, rotation cache eviction, secret missing/forbidden/unavailable mapping, target lock, and
cleanup. Test logs and DTO serialization for absence of PAT and secret identifiers.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build PlanDeck.slnx`
- Key Vault orchestration unit tests pass
- Local integration test creates, reads, rotates, and soft-deletes a test PAT in the Aspire-provisioned vault
- DTO/log regression tests prove PAT and secret identifiers are not exposed

#### Manual Verification:

- `dotnet run --project Aspire/PlanDeck.AppHost` provisions/reuses the dedicated development Key Vault
- Aspire dashboard shows the server waiting for and receiving the `key-vault` reference
- Azure role assignment and generated infrastructure are reviewed for least privilege and environment isolation

**Implementation Note**: Pause for confirmation of real local-vault access and RBAC before Phase 4.

---

## Phase 4: Project-Scoped Sessions and Hardened ADO Operations

### Overview

Route all session and Azure DevOps behavior through project authorization and the project-owned
connection. Remove every caller-controlled path capable of arbitrary PAT-backed reads or writes.

### Changes Required:

#### 1. Session contract and repository scoping

**Files**:
- `src/PlanDeck/Core/PlanDeck.Core.Shared/Contracts/ISessionService.cs`
- session repository abstraction/implementation
- `SessionGrpcService.cs`
- `VotingRoundService.cs`
- `PlanningRoomHub.cs`

**Intent**: Make ProjectId the immutable session owner and enforce project membership on all
management and ADO operations while preserving participant-only voting.

**Contract**:
- Replace Session/creation `TeamId` with required `ProjectId`; never reuse removed protobuf field
  numbers.
- List returns only accessible projects' sessions.
- Explicit session loads derive ProjectId server-side and apply project/session permissions.
- Project Owner/Admin/Member can create and manage project sessions.
- A retained `SessionMember` may join/vote in its assigned active session but receives no project
  list, project management, import, or write-back access.
- Guests retain own-active-session read/vote only.

#### 2. Project-specific ADO client

**Files**:
- `IAzureDevOpsWorkItemClient.cs`
- `AzureDevOpsWorkItemClient.cs`
- `AzureDevOpsOptions.cs`
- server DI registrations

**Intent**: Remove global configuration capture and execute each request using a validated,
project-resolved connection and secret.

**Contract**: Client operations receive an internal immutable connection context containing
organization/project/field mappings and resolved PAT. Remove global organization/project/PAT
options and flat environment keys. Validate HTTPS target, positive IDs, finite estimates, required
revision for write-back, and sanitize upstream failures. ADO errors never return raw response bodies.

#### 3. Safe import and authoritative task creation

**Files**:
- `IAzureDevOpsWorkItemService.cs`
- `AzureDevOpsWorkItemGrpcService.cs`
- `ISessionService.cs`
- `SessionGrpcService.cs`
- client service interfaces/wrappers

**Intent**: Allow project members to preview imports but prevent persistence of forged ADO metadata.

**Contract**:
- `ImportWorkItemsRequest` requires `ProjectId`; service resolves `AdoUse` and the project's active
  connection.
- Replace ADO-bearing `NewSessionTaskDto` input with server-authoritative contracts: ad-hoc inputs
  carry title/description, while selected ADO inputs carry only work-item IDs.
- Creating a session or adding selected ADO tasks re-fetches each ID through the session project's
  connection and persists returned title, description, type, state, revision, and ID.
- Reject IDs not belonging to the configured ADO project.
- Lock the connection target in the same transaction as the first persisted imported task.
- Preserve duplicate `(SessionId, AdoWorkItemId)` behavior and import limit.

#### 4. Remove raw write and harden session write-back

**Files**:
- `IAzureDevOpsWorkItemService.cs`
- `AzureDevOpsWorkItemGrpcService.cs`
- `IAzureDevOpsClientService.cs`
- `AzureDevOpsClientService.cs`
- `SessionGrpcService.cs`

**Intent**: Make the persisted session task the only public source of a write-back target.

**Contract**:
- Remove public `WriteEstimateAsync`, `WriteEstimateRequest`, `WriteEstimateReply`, and client
  wrapper.
- Keep infrastructure write support only behind
  `ISessionService.WriteTaskEstimateToAdoAsync(sessionId, taskId)`.
- Load session/task, verify project access, ADO source, locked project connection, numeric estimate,
  positive ID, and stored revision; resolve the session project's current PAT and field mapping.
- Re-fetch/verify the work item's project before PATCH where the ADO API response can establish it.
- Persist returned revision.
- Map authentication/authorization, concurrency, throttling, missing item, disabled connection, and
  generic upstream failures to sanitized, stable gRPC statuses.

#### 5. ADO security tests

**Files**:
- `AzureDevOpsWorkItemGrpcServiceTests.cs`
- `SessionGrpcServiceTests.cs`
- `VotingRoundServiceTests.cs`
- new project-aware ADO infrastructure and transport tests

**Intent**: Prove both anonymous and authenticated cross-project attackers cannot use another
project's connection.

**Contract**: Cover anonymous, guest, direct Member, team-derived Member, nonmember, Admin, Owner,
cross-tenant, cross-project, forged metadata, missing/inactive connection, target mismatch, stale
revision, missing revision, invalid estimate, upstream 401/403/404/409/412/429, and absence of the
raw write operation.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build PlanDeck.slnx`
- Unit tests pass: `dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj`
- ADO security tests prove forged IDs/metadata and cross-project access never invoke a foreign connection
- Contract test proves the raw write RPC and DTOs no longer exist

#### Manual Verification:

- Import and write-back use the selected session project's connection, not global configuration
- A session participant who is not a project member can vote but cannot import or write back
- ADO logs and client-visible errors contain no PAT, secret identifier, authorization header, or raw upstream body

**Implementation Note**: Pause for security review of the ADO boundary before Phase 5.

---

## Phase 5: Project Administration and Project-First Session UX

### Overview

Expose the new project boundary through localized MudBlazor UI and update session creation/import to
operate in an explicit project context.

### Changes Required:

#### 1. Project client service and navigation

**Files**:
- new `IProjectClientService` and `ProjectClientService`
- `src/PlanDeck/Web/PlanDeck.Client/Program.cs`
- `src/PlanDeck/Web/PlanDeck.Client/Layout/MainLayout.razor`
- layout code-behind

**Intent**: Give authenticated non-guest members a project entry point and typed wrapper over the
project gRPC contract.

**Contract**: Register the wrapper by interface. Navigation distinguishes member accounts from
guests and hides project/team/session administration for guests. UI visibility mirrors effective
role but does not replace server authorization.

#### 2. Project management page

**Files**:
- `src/PlanDeck/Web/PlanDeck.Client/Pages/Projects.razor` (new)
- `Projects.razor.cs` (new)
- focused MudBlazor components as needed

**Intent**: Let users create/select projects and perform only actions allowed by their effective role.

**Contract**:
- Create project with name, description, ADO organization/project, field mapping, and PAT.
- Never display the submitted PAT after send; clear it from component state in `finally`.
- Owner: connection validate/rotate, ownership transfer, delete.
- Owner/Admin: invite direct Member/Admin, change non-owner roles, remove members, assign/unassign
  tenant teams.
- Member: read project and connection health only.
- Show direct/team membership source and pending invitation state.
- All user-facing strings are localized in English and Polish.

#### 3. Team UI updates

**Files**:
- `Teams.razor`
- `Teams.razor.cs`
- team client contract/wrapper as required

**Intent**: Preserve tenant-wide reusable teams and show project assignments without allowing team
membership to escalate roles.

**Contract**: Team member invitations resolve to AppUser identities; project assignment is managed
from authorized project UI. Team pages do not expose ADO settings.

#### 4. Project-first sessions UI

**Files**:
- `Sessions.razor`
- `Sessions.razor.cs`
- session client service interface/implementation
- ADO import dialog/panel and client wrapper

**Intent**: Replace team selection with required project selection and send only authoritative-safe
ADO inputs.

**Contract**:
- Load accessible projects before sessions; select a project explicitly.
- Create session with ProjectId and no TeamId.
- Import preview passes ProjectId.
- Selected imported items contribute only work-item IDs to create/add requests; displayed metadata is
  preview-only.
- Existing session-member management remains for participant invitations.
- Write-back action remains session/task-based and displays sanitized localized success/failure.

#### 5. Localization and accessibility

**Files**:
- `SharedResource.resx`
- `SharedResource.pl.resx`
- affected Razor markup/code-behind

**Intent**: Localize project, roles, invitations, connection states, destructive actions, and security
errors and provide stable accessible test selectors.

**Contract**: Matching en/pl keys, labels and descriptions for role-sensitive controls, confirmation
dialogs for ownership/deletion/rotation, aria labels, and stable `data-testid` attributes on critical
flows.

#### 6. Client tests

**Files**:
- E2E page objects for Projects, Teams, Sessions, and VotingRoom
- focused component/client tests if an existing runner supports them

**Intent**: Verify role-sensitive presentation and safe request shapes without treating UI hiding as
authorization.

**Contract**: Cover PAT field clearing, Owner/Admin/Member control visibility, pending invitation,
team assignment, required project selection, no TeamId control, preview-only ADO metadata, and
localized feedback.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build PlanDeck.slnx`
- Unit/client wrapper tests pass
- Both localization files contain every new key
- E2E page objects can create/select a project and create a project-owned session

#### Manual Verification:

- Owner, Admin, Member, participant-only user, and guest each see the intended navigation/actions
- PAT is never redisplayed and is cleared from browser component state after submission
- Polish and English flows have complete labels, confirmations, and error messages

**Implementation Note**: Pause for manual UX and localization confirmation before Phase 6.

---

## Phase 6: Security Verification, Real-Vault E2E, and Deployment

### Overview

Exercise the full matrix at domain, SQL, gRPC-Web, SignalR, browser, and Azure infrastructure layers,
then execute the approved destructive deployment safely.

### Changes Required:

#### 1. Unit role and failure matrix

**Files**:
- project/access/identity/ADO/session test fixtures under `PlanDeck.Unit.Tests`

**Intent**: Make every permission and failure mapping deterministic and cheap to run.

**Contract**: Parameterized tests cover Owner/Admin/direct Member/team Member/pending invite/nonmember/
guest/anonymous/malformed identity and all project, session, team, connection, import, and write-back
permissions.

#### 2. SQL integration and migration verification

**Files**:
- persistence tests under `PlanDeck.Integration.Tests`
- migration test harness

**Intent**: Prove tenant/project isolation, constraints, cascades/restricts, and the destructive
current-schema-to-new-schema migration.

**Contract**: Apply the migration from the current model, assert session reset, required ProjectId,
removed TeamId, one Owner, invitation/team joins, cross-tenant FK rejection, and no secret-value
columns. Re-run against an empty schema.

#### 3. Real gRPC-Web and SignalR authorization tests

**Files**:
- new in-process/full-host transport tests under `PlanDeck.Integration.Tests`
- existing guest join/hub tests

**Intent**: Prove middleware, endpoint policies, resource resolvers, and guest scope work together.

**Contract**: Send real calls as anonymous, malformed, guest-own-session, guest-foreign-session,
direct member, team member, nonmember, cross-project, and cross-tenant identities. Verify status
codes, concealed resource behavior, and that denied ADO requests never reach the external client or
Key Vault.

#### 4. Real-vault E2E critical path

**Files**:
- `PlanDeck.E2e.Tests/AspireAppFixture.cs`
- E2E tests and page objects

**Intent**: Prove the approved owner-to-member user journey with the Aspire-provisioned development
Key Vault.

**Contract**:
1. Start AppHost in an isolated E2E Azure environment and wait for SQL, server, and Key Vault.
2. Owner creates a project and connection; the PAT is stored in the real test vault.
3. Owner invites/assigns a Member directly or via team.
4. Member creates a project session, previews import, and persists server-refetched ADO tasks.
5. Member votes and writes a numeric estimate back through the session operation.
6. An outsider cannot list/access the project or invoke its ADO connection.
7. Cleanup soft-deletes test secrets and removes project data; cleanup failure fails the test with
   enough non-secret diagnostics.

#### 5. Deployment and rollback procedure

**Files**:
- deployment documentation/configuration directly related to PlanDeck Aspire deployment
- generated Aspire infrastructure review artifacts

**Intent**: Deploy the identity, Key Vault, schema reset, and application atomically enough to avoid
running incompatible versions.

**Contract**:
- Back up the database and announce a maintenance window.
- Preview infrastructure changes before provisioning.
- Provision Key Vault/RBAC and validate Entra configuration before schema reset.
- Stop old traffic, apply the destructive migration, deploy the new app, and run smoke tests.
- Verify no forced test auth in the deployed environment.
- Rollback restores the database backup and prior application; migration down is not considered a
  data recovery mechanism.

### Success Criteria:

#### Automated Verification:

- Full solution builds: `dotnet build PlanDeck.slnx`
- Full test suite passes: `dotnet test PlanDeck.slnx`
- gRPC-Web/SignalR security matrix passes with no foreign ADO/Key Vault calls
- Real-vault E2E owner-to-member import/write-back path passes and cleans up its secret
- Infrastructure preview succeeds and generated RBAC contains no unintended broad default assignment

#### Manual Verification:

- Security review confirms no anonymous, guest, nonmember, cross-project, or cross-tenant PAT-backed operation
- Azure portal confirms development/pilot/production vault separation, soft delete/purge protection, and intended managed-identity RBAC
- Database backup, maintenance window, reset, smoke test, and restore procedure are rehearsed before non-disposable deployment

**Implementation Note**: This phase completes the plan only after both automated results and the
manual security/deployment review are accepted.

---

## Testing Strategy

### Unit Tests:

- Identity parsing/provisioning, pending invitation activation, and idempotent AppUser upsert.
- Complete project role matrix and direct-vs-team effective role.
- Owner-only connection lifecycle, compensation, rotation, and target lock.
- Session/project access and participant-only `SessionMember` behavior.
- Authoritative ADO re-fetch, safe write-back, sanitized status mapping, and raw-contract removal.
- Secret DTO/log redaction and cache invalidation.

### Integration Tests:

- SQL project/identity/member/team/session/connection constraints and tenant isolation.
- Migration from the current schema with approved session reset.
- Real Key Vault create/read/version/soft-delete through Aspire in the dedicated test environment.
- Real gRPC-Web endpoint policies and cross-project concealment.
- SignalR member/session-member/guest authorization.
- Denied requests prove zero calls to ADO and Key Vault.

### Manual Testing Steps:

1. Sign in as a new Entra user and verify one AppUser is provisioned.
2. Create a project with valid ADO target/PAT and verify only connection health, never PAT, is shown.
3. Invite Admin and Member users; assign a tenant team; verify effective roles.
4. Confirm Admin can manage members/teams but cannot rotate PAT, change target, transfer ownership, or delete.
5. As Member, create a project session and import selected work-item IDs.
6. Confirm persisted task metadata matches a fresh ADO response, not modified browser data.
7. Run voting and write back a numeric estimate; confirm revision update and explicit success.
8. Attempt anonymous, guest, nonmember, and cross-project import/write-back; confirm denial and no ADO mutation.
9. Rotate PAT and verify the next operation uses the latest Key Vault version.
10. Attempt to change ADO organization/project after first persisted import; confirm it is blocked.
11. Verify a session-only participant can vote but cannot list the project or use ADO.
12. Run the Polish locale and confirm project/security/connection messages.

## Performance Considerations

- Resolve effective project access with projection/`EXISTS` queries and supporting composite indexes;
  do not materialize all members or teams.
- Cache the latest PAT briefly by opaque secret name. Explicitly invalidate on rotation and evict/retry
  once after ADO 401; never cache in client state or distributed logs.
- Batch ADO re-fetch for selected work-item IDs using the existing work-items batch endpoint and cap
  the accepted count. Avoid one request per item.
- Project/session lists are indexed by tenant, project, and creation time.
- Key Vault and ADO calls require bounded SDK/HTTP timeouts and service-specific retries only for
  transient failures; do not retry authorization, validation, concurrency, or non-idempotent writes
  blindly.

## Migration Notes

- This is an intentionally destructive MVP migration for existing sessions. Back up before applying.
- Existing session, session-task, and session-member rows are deleted; teams and users are retained
  and adapted to resolved/pending identity.
- `Sessions.TeamId` is dropped and required `Sessions.ProjectId` is added only after session deletion.
- No global PAT is migrated automatically into Key Vault because there is no safe project owner or
  project boundary to assign it to.
- After deployment, an Owner creates each project connection through the application flow.
- Historical migrations remain unchanged. Rollback means prior binary plus database restore.

## References

- Change definition: `context/changes/secure-ado-grpc-endpoints/change.md`
- Related connection scope: `context/changes/tenant-scoped-ado-connections/change.md`
- Related production auth scope: `context/changes/separate-production-auth-configuration/change.md`
- Prior write-back research:
  `context/archive/2026-06-24-ado-estimate-writeback/research.md`
- Prior write-back plan:
  `context/archive/2026-06-24-ado-estimate-writeback/plan.md`
- Aspire Key Vault hosting:
  `https://aspire.dev/integrations/cloud/azure/azure-key-vault/azure-key-vault-host/`
- Aspire Key Vault client:
  `https://aspire.dev/integrations/cloud/azure/azure-key-vault/azure-key-vault-connect/`
- Azure Key Vault RBAC:
  `https://learn.microsoft.com/en-us/azure/key-vault/general/rbac-guide`
- Azure Key Vault security:
  `https://learn.microsoft.com/en-us/azure/key-vault/general/secure-key-vault`
- Key Vault multitenancy:
  `https://learn.microsoft.com/en-us/azure/architecture/guide/multitenant/service/key-vault`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Fail-Closed Identity and Endpoint Authorization

#### Automated

- [x] 1.1 Solution builds
- [x] 1.2 Unit tests pass
- [x] 1.3 gRPC-Web authentication tests reject anonymous and malformed callers
- [x] 1.4 Production configuration fails closed without valid Entra settings

#### Manual

- [x] 1.5 Member login provisions one stable AppUser
- [x] 1.6 Guest voting remains scoped to one active session
- [x] 1.7 Published manifest contains no forced Testing/test-auth settings

### Phase 2: Project Aggregate, Membership, and MVP Schema Reset

#### Automated

- [ ] 2.1 Solution builds
- [ ] 2.2 Project role-matrix unit tests pass
- [ ] 2.3 Persistence isolation and constraint tests pass
- [ ] 2.4 Migration applies from the current schema with required ProjectId and no TeamId

#### Manual

- [ ] 2.5 Schema contains no PAT or secret-value columns
- [ ] 2.6 Owner/Admin/Member capabilities match the approved matrix
- [ ] 2.7 Backup and irreversible reset procedure is accepted

### Phase 3: Project-Owned Azure DevOps Connection and Real Key Vault

#### Automated

- [ ] 3.1 Solution builds
- [ ] 3.2 Key Vault orchestration unit tests pass
- [ ] 3.3 Aspire-provisioned vault integration test creates, reads, rotates, and soft-deletes a test PAT
- [ ] 3.4 DTO and log tests prove PAT and secret identifiers are not exposed

#### Manual

- [ ] 3.5 Local AppHost provisions or reuses the dedicated development Key Vault
- [ ] 3.6 Aspire dashboard shows a healthy server-to-vault reference
- [ ] 3.7 Generated role assignments are least-privilege and environment-isolated

### Phase 4: Project-Scoped Sessions and Hardened ADO Operations

#### Automated

- [ ] 4.1 Solution builds
- [ ] 4.2 Unit tests pass
- [ ] 4.3 Forged metadata and cross-project ADO tests never invoke a foreign connection
- [ ] 4.4 Contract test proves the raw write RPC and DTOs are absent

#### Manual

- [ ] 4.5 Import and write-back use the session project's connection
- [ ] 4.6 Session-only participants can vote but cannot use project ADO
- [ ] 4.7 ADO errors and logs expose no secret or raw upstream detail

### Phase 5: Project Administration and Project-First Session UX

#### Automated

- [ ] 5.1 Solution builds
- [ ] 5.2 Client wrapper and related unit tests pass
- [ ] 5.3 English and Polish resource files contain every new key
- [ ] 5.4 E2E page objects create/select a project and a project-owned session

#### Manual

- [ ] 5.5 Role-sensitive navigation and controls match server permissions
- [ ] 5.6 PAT is never redisplayed and is cleared from browser state
- [ ] 5.7 English and Polish project flows are complete and accessible

### Phase 6: Security Verification, Real-Vault E2E, and Deployment

#### Automated

- [ ] 6.1 Full solution builds
- [ ] 6.2 Full test suite passes
- [ ] 6.3 gRPC-Web and SignalR security matrix passes without foreign external calls
- [ ] 6.4 Real-vault owner-to-member import/write-back E2E passes and cleans up
- [ ] 6.5 Infrastructure preview succeeds with intended Key Vault RBAC

#### Manual

- [ ] 6.6 Security review confirms every PAT-backed denial boundary
- [ ] 6.7 Azure vault separation, protection, and managed-identity RBAC are confirmed
- [ ] 6.8 Backup, reset, smoke-test, and restore procedure is rehearsed
