# Azure DevOps Estimate Write-Back (S-08) Implementation Plan

## Overview

Close PlanDeck's import → vote → write-back loop (the north star) by letting a user push the
agreed planning-poker estimate of an Azure DevOps-sourced task back to the originating work
item's estimate field. Per the PRD guardrail, a write must target the correct task/field and
its success or failure must be surfaced explicitly — never silently dropped.

The gRPC transport for the raw write already exists end-to-end. This plan adds the missing last
mile: a **tenant-scoped, server-owned orchestration operation** that loads the `SessionTask`,
validates it, maps the estimate string to a numeric value, calls the existing ADO client, and
persists the returned revision; explicit error mapping; and a UI action with localized feedback.

## Current State Analysis

- **Transport already complete**: `IAzureDevOpsWorkItemService.WriteEstimateAsync` (contract),
  `AzureDevOpsWorkItemGrpcService.WriteEstimateAsync` (`Core/PlanDeck.Application/Services/AzureDevOpsWorkItemGrpcService.cs:36`),
  `AzureDevOpsWorkItemClient.WriteEstimateAsync` (`Core/PlanDeck.Infrastructure/AzureDevOps/AzureDevOpsWorkItemClient.cs:80`)
  with optimistic `/rev` test, and client wrapper `AzureDevOpsClientService.WriteEstimateAsync`.
- **The raw op is "dumb"**: it takes `WorkItemId`/`ExpectedRevision`/`Estimate` from the caller,
  does not load a `SessionTask`, does not verify tenant ownership or task source, and does not
  persist anything. It cannot, by itself, satisfy "writes to the correct task/field".
- **Estimate is a scale face string**: `SessionTask.AgreedEstimate` is `nvarchar(32)`
  (`SessionTaskConfiguration.cs`), holding Fibonacci faces `["0".."21","?","☕"]` or T-Shirt
  `["XS".."XL"]` (`SessionGrpcService.cs:18,20`). The ADO field is numeric (`StoryPoints`,
  `AzureDevOpsOptions.EstimateField`). Only numeric faces map to a `double`.
- **`SessionTask` carries the link**: `AdoWorkItemId:int?`, `AdoRevision:int?`, `AgreedEstimate:string?`,
  `Source:TaskSource{AdHoc,AzureDevOps}` (`Core/PlanDeck.Application/Domain/SessionTask.cs`).
- **Estimate is produced on an Active session** via the voting/reveal flow
  (`PlanningRoomService.cs:240` → `SessionRepository.SetAgreedEstimateAsync`). Write-back therefore
  must NOT be Draft-restricted; load via `ISessionRepository.GetSessionAsync` (any status).
- **`SessionGrpcService` is the natural home**: it already owns `SessionTask` lifecycle, holds
  `ISessionRepository` + `ICurrentUserContext`, and maps domain exceptions to `RpcException`
  (`SessionGrpcService.cs:98-130`). It does NOT yet inject `IAzureDevOpsWorkItemClient`.
- **Repo has a mutation precedent**: `ISessionRepository.SetAgreedEstimateAsync` (`SessionRepository.cs:53-65`)
  loads a task by `(sessionId, taskId)` and `SaveChanges` — the pattern to mirror for revision update.
- **Infra client error policy is "throw, don't drop"** (`AzureDevOpsWorkItemClient.cs:188-211`):
  429 → `InvalidOperationException`; 409/412 → `InvalidOperationException` ("revision changed");
  other non-2xx → `HttpRequestException`. These are untyped, so the server cannot distinguish them
  without string-matching — this plan introduces typed exceptions.
- **Client feedback pattern**: `Sessions.razor` injects `ISnackbar`; `Sessions.razor.cs:689 ShowError`
  maps `RpcException.StatusCode` → localized snackbar. Task action row at `Sessions.razor:229-236`.
- **Localization**: `@inject IStringLocalizer<SharedResource> L`; keys in
  `Web/PlanDeck.Client/Resources/SharedResource.resx` (+ `.pl.resx`).

## Desired End State

On a session's task list, a task that came from Azure DevOps and has a **numeric** agreed estimate
shows a "write estimate to Azure DevOps" action. Clicking it pushes the numeric estimate to the
originating work item's estimate field using the stored revision for optimistic concurrency; on
success the user sees a success snackbar and the task's stored `AdoRevision` is updated to the
revision ADO returned; on a revision conflict, rate-limit, or any other failure the user sees a
distinct, localized error and nothing is silently dropped. Non-numeric (T-Shirt, `?`, `☕`)
estimates never expose the action. Verified by unit tests over every server path and a Playwright
round-trip (import → estimate → write → success) against the test-scheme fake.

### Key Discoveries:

- New operation belongs in `ISessionService`/`SessionGrpcService` and must inject
  `IAzureDevOpsWorkItemClient` (`Core/PlanDeck.Application/Abstractions/IAzureDevOpsWorkItemClient.cs`).
- Estimate parse: `double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out _)`.
- Persisting the new revision needs a new repo method mirroring `SetAgreedEstimateAsync`.
- Typed ADO exceptions are required to map 409/412 and 429 to distinct gRPC statuses.

## What We're NOT Doing

- Not mapping T-Shirt sizes or `?`/`☕` to numbers; non-numeric estimates are not writable in v1.
- Not writing the estimate to any field other than the configured `EstimateField` (StoryPoints).
- Not adding auto-retry on conflict (would risk overwriting an external change).
- Not adding a new persisted "last written to ADO" timestamp/flag column.
- Not changing the import flow, the voting/reveal flow, or the existing raw
  `IAzureDevOpsWorkItemService.WriteEstimateAsync` operation (left in place; unused by the new path
  but still covered by its test).
- Not bulk write-back (one task per action in v1).

## Implementation Approach

Server owns the round-trip. A new `ISessionService.WriteTaskEstimateToAdoAsync(sessionId, taskId)`
loads the task tenant-scoped, validates (`Source==AzureDevOps`, `AdoWorkItemId` present, agreed
estimate present and numeric), parses the estimate invariant-culture to `double`, calls
`IAzureDevOpsWorkItemClient.WriteEstimateAsync` with the stored `AdoWorkItemId` + `AdoRevision`,
persists the returned revision onto the task, and returns the refreshed `SessionDto`. The infra
client gains typed exceptions so the server maps concurrency → `Aborted`, rate-limit →
`ResourceExhausted`, other ADO failures → `Unavailable`, and validation failures → `NotFound` /
`FailedPrecondition`. The client adds a thin wrapper method, a per-task action button gated on
numeric ADO estimate, and a dedicated handler that maps each status to a localized snackbar.

## Critical Implementation Details

- **Status code choice for concurrency**: use `StatusCode.Aborted` for 409/412 (conventionally the
  concurrency/abort status) so it is distinct from `FailedPrecondition`, which `ShowError` already
  maps to the "active locked" message — the write-back handler must not collide with that.
- **Session status**: load with `GetSessionAsync` (works for Active/Completed), NOT `LoadDraftAsync`;
  the agreed estimate only exists after voting on a non-Draft session.

## Phase 1: Server-side write-back operation + error mapping + unit tests

### Overview

Add the tenant-scoped orchestration operation, the repository revision-update method, and typed ADO
exceptions with gRPC status mapping. Cover every path with unit tests.

### Changes Required:

#### 1. Typed Azure DevOps exceptions

**File**: `Core/PlanDeck.Application/Abstractions/IAzureDevOpsWorkItemClient.cs`

**Intent**: Introduce typed exceptions so callers can distinguish concurrency conflicts and rate
limiting from generic failures without string-matching.

**Contract**: Add `public sealed class AzureDevOpsConcurrencyException : Exception` and
`public sealed class AzureDevOpsRateLimitException : Exception` in the same namespace as the client
abstraction. No interface signature change.

#### 2. Throw typed exceptions from the infra client

**File**: `Core/PlanDeck.Infrastructure/AzureDevOps/AzureDevOpsWorkItemClient.cs`

**Intent**: Replace the untyped `InvalidOperationException` throws in `SendAsync` for the
rate-limit (429) and revision-conflict (409/412) branches with the new typed exceptions so the
server can map them to specific statuses; keep the generic `HttpRequestException` branch.

**Contract**: In `SendAsync` (lines 188-211): 429 → throw `AzureDevOpsRateLimitException`
(preserve the Retry-After detail in the message); 409/412 → throw `AzureDevOpsConcurrencyException`.
Behavior otherwise unchanged.

#### 3. Repository revision-update method

**File**: `Core/PlanDeck.Application/Abstractions/ISessionRepository.cs` and
`Core/PlanDeck.Infrastructure/Persistence/SessionRepository.cs`

**Intent**: Persist the revision ADO returns onto the originating `SessionTask` so subsequent
write-backs use a fresh `/rev` and don't raise false conflicts.

**Contract**: Add `Task<bool> SetAdoRevisionAsync(Guid sessionId, Guid taskId, int revision, CancellationToken cancellationToken)`
to the interface; implement in `SessionRepository` mirroring `SetAgreedEstimateAsync` (`SessionRepository.cs:53-65`)
— load the task by `(sessionId, taskId)` under the tenant filter, set `AdoRevision = revision`,
`SaveChangesAsync`, return whether a row was found.

#### 4. New write-back contract operation + DTOs

**File**: `Core/PlanDeck.Core.Shared/Contracts/ISessionService.cs`

**Intent**: Expose a code-first gRPC operation that performs the whole write-back from a session +
task identity, returning the refreshed session so the client can replace its in-memory copy
(matching the existing add/remove/update task operations).

**Contract**: Add `[Operation] Task<WriteTaskEstimateReply> WriteTaskEstimateToAdoAsync(WriteTaskEstimateRequest request, CallContext context = default)`
to `ISessionService`. New `[DataContract] WriteTaskEstimateRequest { [DataMember(Order=1)] Guid SessionId; [DataMember(Order=2)] Guid TaskId; }`
and `[DataContract] WriteTaskEstimateReply { [DataMember(Order=1)] SessionDto Session; [DataMember(Order=2)] int WorkItemId; [DataMember(Order=3)] int Revision; }`.

#### 5. Server operation implementation

**File**: `Core/PlanDeck.Application/Services/SessionGrpcService.cs`

**Intent**: Implement the orchestration: reject guests, load the task tenant-scoped, validate it is
an ADO task with a numeric agreed estimate, parse the estimate, call the ADO client with the stored
work-item id + revision, persist the returned revision, and return the refreshed session DTO.
Map domain/ADO failures to explicit gRPC statuses.

**Contract**: Inject `IAzureDevOpsWorkItemClient` into the primary constructor (alongside the
existing dependencies at `SessionGrpcService.cs:11-16`). Implement `WriteTaskEstimateToAdoAsync`:
- `GuestAccessGuard.RejectGuests(currentUser)`.
- Load session via `repository.GetSessionAsync`; if null/absent → `RpcException(NotFound)`. Find the
  task by `request.TaskId`; if absent → `RpcException(NotFound)`.
- Defensive validation → `RpcException(FailedPrecondition)` when: `Source != AzureDevOps`,
  `AdoWorkItemId is null`, `AgreedEstimate` is null/whitespace, or it fails
  `double.TryParse(AgreedEstimate, NumberStyles.Any, CultureInfo.InvariantCulture, out var estimate)`.
- Call `client.WriteEstimateAsync(new AzureDevOpsWriteEstimateRequest(task.AdoWorkItemId.Value, task.AdoRevision, estimate), ct)`.
- On success: `repository.SetAdoRevisionAsync(sessionId, taskId, result.Revision, ct)`; reload the
  session (or update the in-memory task) and return `WriteTaskEstimateReply { Session = ToDto(session), WorkItemId, Revision }`.
- Catch `AzureDevOpsConcurrencyException` → `RpcException(Aborted)`;
  `AzureDevOpsRateLimitException` → `RpcException(ResourceExhausted)`; any other ADO/HTTP failure
  → `RpcException(Unavailable)`. (Detail strings are not relied on by the client for mapping.)

#### 6. Server unit tests

**File**: `Tests/PlanDeck.Unit.Tests/Sessions/SessionGrpcServiceTests.cs` (extend; reuse the
existing fixture's `FakeSessionRepository` + `FakeCurrentUserContext` pattern) and a configurable
fake `IAzureDevOpsWorkItemClient`.

**Intent**: Prove the guardrail and every branch.

**Contract**: Tests — happy path (forwards `AdoWorkItemId`+`AdoRevision`+parsed estimate, persists
returned revision, returns refreshed session); task not found → `NotFound`; non-ADO task → `FailedPrecondition`;
missing agreed estimate → `FailedPrecondition`; non-numeric estimate (`"XL"`) → `FailedPrecondition`;
guest → rejected; ADO concurrency exception → `Aborted`; ADO rate-limit exception → `ResourceExhausted`;
generic ADO failure → `Unavailable`. Assertion style `Assert.ThrowsAsync<RpcException>` + `StatusCode`.
The fake repository may need to support seeding a session with an ADO task + agreed estimate and to
record `SetAdoRevisionAsync` calls.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build PlanDeck.slnx` (from `src/PlanDeck/`)
- Unit tests pass: `dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj`
- New write-back unit tests cover all nine branches and pass

#### Manual Verification:

- Code review confirms the operation is tenant-scoped (uses `GetSessionAsync`, no cross-tenant leak)
  and never writes for non-ADO or non-numeric tasks
- Status mapping matches the table (Aborted / ResourceExhausted / Unavailable / NotFound / FailedPrecondition)

**Implementation Note**: After completing this phase and all automated verification passes, pause
for manual confirmation before proceeding to Phase 2.

---

## Phase 2: Client action, localization, and E2E round-trip

### Overview

Surface the write-back as a per-task action that only appears for ADO tasks with a numeric estimate,
with a dedicated handler that maps each gRPC status to a localized snackbar, and prove the loop
end-to-end with a Playwright test against the fake ADO client.

### Changes Required:

#### 1. Client service wrapper method

**File**: `Web/PlanDeck.Client/Services/ISessionClientService.cs` and its implementation
(`SessionClientService.cs`)

**Intent**: Expose the new gRPC operation to the UI behind the existing session client interface.

**Contract**: Add `Task<WriteTaskEstimateReply> WriteTaskEstimateToAdoAsync(Guid sessionId, Guid taskId)`;
implement via the existing `GrpcChannel.CreateGrpcService<ISessionService>()` pattern used by the
other methods, passing a `WriteTaskEstimateRequest`.

#### 2. Task action button (markup)

**File**: `Web/PlanDeck.Client/Pages/Sessions.razor`

**Intent**: Add a write-back `MudIconButton` to the task action row, visible only for ADO tasks with
a numeric agreed estimate, with a per-task busy/disabled state and a localized aria-label/tooltip.

**Contract**: In the action row (`Sessions.razor:229-236`), add a cloud-upload `MudIconButton`
guarded by `@if (CanWriteEstimate(task))`, `Disabled="@IsWritingEstimate(task.Id)"`,
`OnClick="@(() => WriteEstimateToAdoAsync(task))"`, `aria-label="@L[\"Sessions_WriteEstimate\"]"`,
plus a `data-testid` (e.g. `write-estimate`) for the E2E locator.

#### 3. Write-back handler + visibility/busy helpers (code-behind)

**File**: `Web/PlanDeck.Client/Pages/Sessions.razor.cs`

**Intent**: Hold the action logic per the repo's code-behind convention, mapping each gRPC status to
a distinct localized message and replacing the in-memory session on success.

**Contract**: Add `private readonly HashSet<Guid> _writingEstimateTaskIds = [];` and helpers
`CanWriteEstimate(SessionTaskDto)` (true when `Source==AzureDevOps && AdoWorkItemId is not null &&
double.TryParse(AgreedEstimate, NumberStyles.Any, CultureInfo.InvariantCulture, out _)`) and
`IsWritingEstimate(Guid)`. Add `WriteEstimateToAdoAsync(SessionTaskDto task)`: guard `_selected`,
add id to busy set, `try` call `SessionService.WriteTaskEstimateToAdoAsync(_selected.Id, task.Id)`,
`ReplaceSelected(reply.Session)`, `Snackbar.Add(L["Sessions_WriteEstimateSuccess"], Severity.Success)`;
`catch (RpcException ex)` map `ex.StatusCode`: `Aborted` → `Sessions_WriteEstimateConflict`,
`ResourceExhausted` → `Sessions_WriteEstimateRateLimited`, else → `Sessions_WriteEstimateFailed`
(`Severity.Error`); `finally` remove id from busy set. Add `using System.Globalization;`.

#### 4. Localization keys (en + pl)

**File**: `Web/PlanDeck.Client/Resources/SharedResource.resx` and `SharedResource.pl.resx`

**Intent**: Provide all user-facing strings for the action and its outcomes in both languages.

**Contract**: Add keys: `Sessions_WriteEstimate` (button/tooltip), `Sessions_WriteEstimateSuccess`,
`Sessions_WriteEstimateConflict` (revision changed — refresh and retry), `Sessions_WriteEstimateRateLimited`,
`Sessions_WriteEstimateFailed`. Provide English values in `.resx` and Polish in `.pl.resx`.

#### 5. E2E round-trip test

**File**: `Tests/PlanDeck.E2e.Tests/SessionsTests.cs` and `Tests/PlanDeck.E2e.Tests/Pages/SessionsPage.cs`

**Intent**: Prove the full loop against the test-scheme fake ADO client (import → set estimate →
write back → success feedback).

**Contract**: The fake (`Web/PlanDeck.Server/Testing/FakeAzureDevOpsWorkItemClient.cs`) already
returns a successful `WriteEstimateAsync` (revision+1). The E2E must reach a state where an
imported ADO task has a numeric agreed estimate — extend `SessionsPage` with a write-estimate
action helper (click the `write-estimate` `data-testid`) and assert the success snackbar/text
appears. If setting a numeric agreed estimate through the UI is not feasible in the E2E harness,
seed it via the existing session/voting test affordances; document the chosen approach in the test.
Mirror the existing `ImportFromAzureDevOps_AddsWorkItemWithAdoChip` page-object pattern and WASM-boot
waits.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build PlanDeck.slnx`
- Unit tests still pass: `dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj`
- E2E round-trip test passes locally (Aspire + Podman running):
  `dotnet test Tests/PlanDeck.E2e.Tests/PlanDeck.E2e.Tests.csproj --filter "FullyQualifiedName~WriteEstimate"`
- Both `.resx` and `.pl.resx` contain every new key (no missing-resource fallback)

#### Manual Verification:

- ADO task with a numeric estimate shows the action; T-Shirt / `?` / `☕` tasks do not
- Successful write shows a success snackbar; the task's stored revision advances (second write does
  not raise a false conflict)
- Forcing a conflict/rate-limit/failure surfaces the correct distinct localized message
- Polish locale shows translated strings for all five keys

**Implementation Note**: After completing this phase and all automated verification passes, pause
for manual confirmation.

---

## Testing Strategy

### Unit Tests:

- `SessionGrpcService.WriteTaskEstimateToAdoAsync`: happy path (correct id/revision/estimate
  forwarded; returned revision persisted; refreshed session returned), not-found, non-ADO,
  no-estimate, non-numeric, guest, concurrency→Aborted, rate-limit→ResourceExhausted, generic→Unavailable.
- Reuse the existing NUnit fixture conventions in `SessionGrpcServiceTests.cs`; add a configurable
  fake `IAzureDevOpsWorkItemClient` (success + each exception).

### Integration Tests:

- Optional: extend `SessionPersistenceTests` to assert `SetAdoRevisionAsync` updates `AdoRevision`
  for the correct task only (real SQL Server via `AspireAppFixture`). Include only if cheap.

### Manual Testing Steps:

1. Create a session (Fibonacci), import an ADO task, run a round so the task gets a numeric estimate.
2. Click the write-back action → success snackbar; confirm the work item's estimate field in ADO.
3. Click again → no false conflict (stored revision advanced).
4. Externally bump the work item revision, then write back → conflict message shown.
5. Switch a session to T-Shirt scale → confirm the action is absent for those tasks.
6. Switch locale to Polish → confirm all messages are translated.

## Performance Considerations

Single-item, user-initiated network call; no hot path. The post-write reload of the session is one
extra DB read, acceptable for a manual action.

## Migration Notes

No schema change. `AdoRevision` already exists; only its value is updated post-write. No migration.

## References

- Research: `context/changes/ado-estimate-writeback/research.md`
- Contract surface: `Core/PlanDeck.Core.Shared/Contracts/ISessionService.cs`,
  `Core/PlanDeck.Core.Shared/Contracts/IAzureDevOpsWorkItemService.cs`
- Server precedent: `Core/PlanDeck.Application/Services/SessionGrpcService.cs:98-130` (exception→status),
  `Core/PlanDeck.Infrastructure/Persistence/SessionRepository.cs:53-65` (`SetAgreedEstimateAsync`)
- ADO client: `Core/PlanDeck.Infrastructure/AzureDevOps/AzureDevOpsWorkItemClient.cs:80,188-211`
- UI: `Web/PlanDeck.Client/Pages/Sessions.razor:229-236`, `Sessions.razor.cs:689` (`ShowError`)
- PRD: `context/foundation/prd.md` (FR-010, US-01, Guardrails); roadmap S-08

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Server-side write-back operation + error mapping + unit tests

#### Automated

- [x] 1.1 Solution builds: `dotnet build PlanDeck.slnx`
- [x] 1.2 Unit tests pass: `dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj`
- [x] 1.3 New write-back unit tests cover all nine branches and pass

#### Manual

- [ ] 1.4 Code review confirms tenant-scoping and no write for non-ADO/non-numeric tasks
- [ ] 1.5 Status mapping matches the table (Aborted / ResourceExhausted / Unavailable / NotFound / FailedPrecondition)

### Phase 2: Client action, localization, and E2E round-trip

#### Automated

- [ ] 2.1 Solution builds: `dotnet build PlanDeck.slnx`
- [ ] 2.2 Unit tests still pass
- [ ] 2.3 E2E round-trip test passes locally (Aspire + Podman)
- [ ] 2.4 Both `.resx` and `.pl.resx` contain every new key

#### Manual

- [ ] 2.5 Action shows only for numeric ADO estimates; absent for T-Shirt / `?` / `☕`
- [ ] 2.6 Success snackbar shown; stored revision advances (no false second-write conflict)
- [ ] 2.7 Conflict / rate-limit / failure each surface the correct distinct localized message
- [ ] 2.8 Polish locale shows translated strings for all five keys
