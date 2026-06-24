---
date: 2026-06-24T22:35:00+02:00
researcher: Copilot CLI
git_commit: 7cf869abf13c4b844260a3fccf60635153ea0b90
branch: main
repository: LeszekDNV/PlanDeck2
topic: "S-08 — Write the agreed estimate back to the originating Azure DevOps task"
tags: [research, codebase, azure-devops, write-back, planning-poker, grpc]
status: complete
last_updated: 2026-06-24
last_updated_by: Copilot CLI
---

# Research: S-08 — Azure DevOps estimate write-back

**Date**: 2026-06-24T22:35:00+02:00
**Researcher**: Copilot CLI
**Git Commit**: 7cf869abf13c4b844260a3fccf60635153ea0b90
**Branch**: main
**Repository**: LeszekDNV/PlanDeck2

## Research Question

S-08 (north star, `ado-estimate-writeback`): a user can write the agreed planning-poker
estimate back to the originating Azure DevOps work item, with success or failure surfaced
explicitly and never silently dropped (PRD FR-010, US-01). What already exists end-to-end,
and what is the remaining work + risk?

## Summary

The **entire gRPC transport for write-back already exists and is wired up** — infrastructure
client, application gRPC service, code-first contract, client-side wrapper, DI, gRPC-Web
mapping, and a test fake. The Azure DevOps client even implements the optimistic `/rev`
concurrency test and explicit error surfacing (409/412/429/non-2xx all throw).

What is **missing** is the last mile:

1. **No UI** — `Sessions.razor` shows `AgreedEstimate` as a chip but offers no "write back to
   ADO" action on a task.
2. **No orchestration / mapping** — nothing converts a persisted `AgreedEstimate` (a scale
   *face* string like `"5"`, `"XL"`, `"?"`, `"☕"`) into the `double` the ADO StoryPoints
   field (and the existing `WriteEstimateAsync` contract) requires. **This is the core design
   problem of S-08.**
3. **No success/failure feedback wiring** in the UI for the guardrail, and **no localized
   strings** for it.
4. **No tests** beyond a single happy-path forwarding test; all error/guardrail paths are
   untested, and there is no E2E test for the round-trip.

A secondary nuance: the application gRPC `WriteEstimateAsync` takes a raw `WorkItemId`/
`ExpectedRevision`/`Estimate` and does **not** load the `SessionTask` — so it currently does
not, by itself, guarantee "writes to the *correct* task/field" from persisted state. The plan
must decide whether the client passes `AdoWorkItemId`/`AdoRevision`/mapped estimate from the
loaded task, or whether a new server-side operation should load the task and own the mapping
(recommended for the guardrail "never corrupt or overwrite the wrong task or field").

## Detailed Findings

### Area 1 — gRPC write-back surface (ALREADY COMPLETE)

- **Contract** `Core/PlanDeck.Core.Shared/Contracts/IAzureDevOpsWorkItemService.cs`
  - `WriteEstimateAsync(WriteEstimateRequest, CallContext)` exists alongside `ImportWorkItemsAsync`.
  - `WriteEstimateRequest`: `WorkItemId:int` (Order 1), `ExpectedRevision:int?` (Order 2), `Estimate:double` (Order 3).
  - `WriteEstimateReply`: `WorkItemId:int` (Order 1), `Revision:int` (Order 2).
- **Server service** `Core/PlanDeck.Application/Services/AzureDevOpsWorkItemGrpcService.cs:36-49`
  - `GuestAccessGuard.RejectGuests(currentUser)` then pass-through to `client.WriteEstimateAsync(...)`,
    mapping to `WriteEstimateReply`. No extra error handling — exceptions bubble to the gRPC framework.
- **Infrastructure client** `Core/PlanDeck.Infrastructure/AzureDevOps/AzureDevOpsWorkItemClient.cs:80-106`
  - Validates `WorkItemId > 0`; emits a JSON-Patch `test /rev` op when `ExpectedRevision` is set
    (optimistic concurrency), then `add /fields/{EstimateField}` = `request.Estimate` (a `double`).
  - PATCH to `_apis/wit/workitems/{id}?api-version=7.2-preview.3`, content type `application/json-patch+json`.
  - `SendAsync` (lines 188-211) throws explicitly on 429 (rate limit), 409/412 (revision changed),
    and any other non-success status (with body). **Nothing is silently dropped** at this layer.
- **Client wrapper** `Web/PlanDeck.Client/Services/AzureDevOpsClientService.cs` +
  `IAzureDevOpsClientService.cs` — `WriteEstimateAsync(int workItemId, int? expectedRevision, double estimate)`
  already implemented via `channel.CreateGrpcService<IAzureDevOpsWorkItemService>()`.
- **Wiring** `Web/PlanDeck.Server/Program.cs` — `AddCodeFirstGrpc()`, `UseGrpcWeb(DefaultEnabled=true)`,
  `app.MapGrpcService<AzureDevOpsWorkItemGrpcService>()`.
- **DI** `Web/PlanDeck.Server/Extensions/ServiceCollectionExtensions.cs` — prod:
  `AddHttpClient<IAzureDevOpsWorkItemClient, AzureDevOpsWorkItemClient>()` + `Configure<AzureDevOpsOptions>`;
  test scheme: `AddScoped<IAzureDevOpsWorkItemClient, FakeAzureDevOpsWorkItemClient>()`; plus
  `AddScoped<AzureDevOpsWorkItemGrpcService>()`.
- **Options** `Core/PlanDeck.Infrastructure/AzureDevOps/AzureDevOpsOptions.cs` —
  `EstimateField` default `"Microsoft.VSTS.Scheduling.StoryPoints"` (a numeric field).
- **Test fake** `Web/PlanDeck.Server/Testing/FakeAzureDevOpsWorkItemClient.cs` — `WriteEstimateAsync`
  returns `new AzureDevOpsWriteEstimateResult(WorkItemId, ExpectedRevision.GetValueOrDefault()+1)`.

### Area 2 — Agreed estimate persistence, linkage, and the mapping problem

- **`SessionTask`** `Core/PlanDeck.Application/Domain/SessionTask.cs` — `AdoWorkItemId:int?`,
  `AdoRevision:int?`, `WorkItemType`, `State`, `AgreedEstimate:string?`, `Source:TaskSource{AdHoc,AzureDevOps}`.
- **EF mapping** `Core/PlanDeck.Infrastructure/Persistence/Configurations/SessionTaskConfiguration.cs`
  - `AgreedEstimate` → `nvarchar(32)`, nullable.
  - Unique filtered index on `(SessionId, AdoWorkItemId)` where `AdoWorkItemId IS NOT NULL`
    (migration `20260619003148_AddSessionTaskAdoUniqueIndex`) — one ADO work item per session.
  - `AgreedEstimate` column added by migration `20260622153451_AddSessionTaskAgreedEstimate`.
- **Where `AgreedEstimate` is SET** (the voting/reveal path, from the realtime-vote-integrity change):
  - `Core/PlanDeck.Application/Planning/PlanningRoomService.cs:240` — `task.AgreedEstimate = estimate;`
    (`ApplyAgreedEstimate`, line 232); reset to `null` at line 212.
  - `Core/PlanDeck.Application/Planning/VotingRoundService.cs:58` → `sessionRepository.SetAgreedEstimateAsync(...)`.
  - `Core/PlanDeck.Infrastructure/Persistence/SessionRepository.cs:53-65` — direct assignment
    `task.AgreedEstimate = estimate; SaveChangesAsync()`. **No numeric conversion or validation.**
- **Scale faces (the values that land in `AgreedEstimate`)** `Services/SessionGrpcService.cs:18,20`:
  - Fibonacci: `["0","1","2","3","5","8","13","21","?","☕"]`
  - T-Shirt: `["XS","S","M","L","XL","?","☕"]`
- **CONSEQUENCE — the central design problem**: `AgreedEstimate` is a *face string*; the ADO
  StoryPoints field and `WriteEstimateRequest.Estimate` are `double`. Only the numeric Fibonacci
  faces parse cleanly. `"?"`, `"☕"`, and **all** T-Shirt sizes have no natural `double`. The plan
  must define mapping/guard rules: which faces are writable, what happens for non-numeric faces
  (disable the action / block with a message), and whether a T-Shirt→points mapping is in scope
  (likely out of scope for v1 — gate write-back to numeric scales/values).
- **Read path**: `SessionGrpcService.ToDto` (line 436) passes `AgreedEstimate` through to
  `SessionTaskDto.AgreedEstimate` (Order 9). `SessionTaskDto` also carries `AdoWorkItemId` (Order 5)
  and `AdoRevision` (Order 6) — so the client already has everything needed to call the existing
  wrapper, *if* the string→double mapping is solved client-side. (A server-side op that loads the
  task is the safer alternative for the "correct task/field" guardrail.)
- **Tenant scoping**: `SessionTask : TenantEntity` (`ITenantScoped`); `PlanDeckDbContext` applies a
  global tenant query filter and stamps/guards `TenantId` on save (fail-closed if tenant empty).

### Area 3 — Client UI & localization patterns to follow

- **Task list** `Web/PlanDeck.Client/Pages/Sessions.razor:200-238` — each task is a `MudPaper` with
  title, an ADO `#id` chip when `Source==AzureDevOps` (lines 205-208), the `AgreedEstimate` green
  `MudChip` (lines 209-212), and a row of `MudIconButton` Edit/Delete actions (lines 229-236).
  **The write-back action belongs in this action row, gated on `Source==AzureDevOps` &&
  `AgreedEstimate` present && writable.**
- **Closest reference pattern** `Web/PlanDeck.Client/Components/AdoImportPanel.razor(.cs)` —
  `MudButton` with `Disabled="_loading"`, `MudProgressCircular` while busy; code-behind sets
  `_loading=true` in `try`, calls the client service, `catch (RpcException)` →
  `Snackbar.Add(L["Error_Generic"], Severity.Error)`, `finally _loading=false`.
- **Feedback** = `ISnackbar` (`@inject ISnackbar Snackbar`): `Severity.Success` /
  `Severity.Error`. Existing success example: `Sessions.razor.cs` → `Snackbar.Add(L["Sessions_Activated"], Severity.Success)`.
  Per the guardrail, write-back **must** show success *and* failure explicitly (don't copy
  AdoImportPanel's silent-success).
- **Localization** `@inject IStringLocalizer<SharedResource> L`, keys in
  `Web/PlanDeck.Client/Resources/SharedResource.resx` (+ `.pl.resx`). Existing keys:
  `Error_Generic`, `Sessions_Activated`, `Sessions_ImportAdo`, `Sessions_ShareCopied`. **New keys
  needed** (en+pl), e.g. `Sessions_WriteEstimate` (button), `Sessions_WriteEstimateSuccess`,
  `Sessions_WriteEstimateFailed`, and ideally a concurrency-specific message
  (`Sessions_WriteEstimateConflict`) since the client distinguishes 409/412.
- **Code-behind convention** (repo rule): logic lives in `Sessions.razor.cs` partial class, never
  in `@code`. Add the handler + per-task busy state there.

### Area 4 — Tests & guardrails

- **Unit** `Tests/PlanDeck.Unit.Tests/AzureDevOps/AzureDevOpsWorkItemGrpcServiceTests.cs` — NUnit 4,
  `[SetUp]` manual DI, spy-fake recording `LastWriteRequest`. Only `WriteEstimateAsync_ForwardsRequestAndMapsResult`
  (happy path) exists. Missing: guest rejection, conflict/412, 429, invalid id, mapping rules.
- **Session service tests** `Tests/PlanDeck.Unit.Tests/Sessions/SessionGrpcServiceTests.cs` —
  fakes for repo/member-repo/user-context/notifier; error guardrails asserted via
  `Assert.ThrowsAsync<RpcException>` + `StatusCode` (e.g. `FailedPrecondition`). This is the
  pattern any new server-side write-back operation should follow.
- **Integration** `Tests/PlanDeck.Integration.Tests/Persistence/SessionPersistenceTests.cs` — real
  SQL Server via `AspireAppFixture.ConnectionString`; `DbContext` built with a `FakeCurrentUserContext`;
  fail-closed tenant tests throw `InvalidOperationException`.
- **E2E** `Tests/PlanDeck.E2e.Tests/SessionsTests.cs` + `Pages/SessionsPage.cs` — Playwright page-object;
  existing `ImportFromAzureDevOps_AddsWorkItemWithAdoChip` imports fake item `1001`. No write-back E2E yet.
- **PRD guardrail text** `context/foundation/prd.md`:
  - Guardrails: "Writing an estimate back to Azure DevOps never corrupts or overwrites the wrong task
    or field; a failed write is surfaced explicitly and never silently drops the result."
  - US-01: "Saving the estimate writes to the correct Azure DevOps task and field, and surfaces
    success or failure to the user."
  - FR-010: "A user can save the agreed estimate back to the originating Azure DevOps task."
- **Health-check** `context/foundation/health-check.md` — ADO write-back flagged as the second
  integration risk; must respect work-item behavior and target the originating work item's estimate field.

## Code References

- `Core/PlanDeck.Core.Shared/Contracts/IAzureDevOpsWorkItemService.cs` — write-back contract (op + DTOs).
- `Core/PlanDeck.Application/Services/AzureDevOpsWorkItemGrpcService.cs:36-49` — server op (guest guard + passthrough).
- `Core/PlanDeck.Infrastructure/AzureDevOps/AzureDevOpsWorkItemClient.cs:80-106,188-211` — PATCH `/rev` + error surfacing.
- `Core/PlanDeck.Infrastructure/AzureDevOps/AzureDevOpsOptions.cs` — `EstimateField=StoryPoints`.
- `Web/PlanDeck.Client/Services/AzureDevOpsClientService.cs` — `WriteEstimateAsync` wrapper (exists).
- `Core/PlanDeck.Application/Domain/SessionTask.cs` — `AdoWorkItemId/AdoRevision/AgreedEstimate`.
- `Core/PlanDeck.Application/Services/SessionGrpcService.cs:18,20,436` — scale faces + `ToDto` passthrough.
- `Core/PlanDeck.Infrastructure/Persistence/SessionRepository.cs:53-65` — `SetAgreedEstimateAsync` (string, no convert).
- `Core/PlanDeck.Application/Planning/PlanningRoomService.cs:212,240` — where `AgreedEstimate` is set/reset.
- `Web/PlanDeck.Client/Pages/Sessions.razor:200-238` — task action row + estimate chip.
- `Web/PlanDeck.Client/Components/AdoImportPanel.razor.cs:50-76` — loading/error pattern to mirror.
- `Web/PlanDeck.Client/Resources/SharedResource.resx` (+ `.pl.resx`) — localization keys.
- `Tests/PlanDeck.Unit.Tests/AzureDevOps/AzureDevOpsWorkItemGrpcServiceTests.cs:73-114` — fake + happy-path test.

## Architecture Insights

- Layering holds: contract in `Core.Shared`, impl in `Application`, HTTP in `Infrastructure`, host
  wires endpoint in `Server`, client wraps behind an interface — write-back already obeys this.
- Error policy is "throw, don't drop": the infra client converts ADO failures into exceptions, and
  the gRPC framework turns them into `RpcException` for the client `catch` — aligning naturally with
  the PRD guardrail, provided the UI surfaces both branches.
- The real engineering content of S-08 is **not transport** (done) but **semantic mapping +
  UX + tests**: face-string → numeric estimate, deciding writable vs non-writable faces/scales,
  passing/looking up the correct `AdoWorkItemId`+`AdoRevision`, and proving the failure paths.

## Open Questions

1. **Face → double mapping**: write back only numeric Fibonacci faces? Block `"?"`/`"☕"` and all
   T-Shirt sizes (disable the action with a tooltip/message)? Or define a T-Shirt→points table? (v1
   likely: only numeric values are writable.)
2. **Where does mapping live?** Client-side (parse `AgreedEstimate`, call existing wrapper with
   `AdoWorkItemId`/`AdoRevision`) vs a new server-side op that loads the `SessionTask` by id, owns the
   mapping, enforces tenant + `Source==AzureDevOps`, and uses the stored revision. The latter better
   satisfies "writes to the correct task/field" and centralizes validation — recommended.
3. **Stale revision UX**: on 409/412 the client throws a distinct message; should the UI offer a
   refresh/retry, and should it re-read the work item's current revision?
4. **Round behavior**: write-back gated to revealed/agreed state only? Re-write after a re-vote?
5. **Localization**: confirm exact en/pl strings for success, failure, conflict, and "not writable".

## Related Research

- `context/changes/realtime-vote-integrity/` — owns the voting/reveal flow that produces
  `AgreedEstimate` (`PlanningRoomService`, `VotingRoundService`, `SetAgreedEstimateAsync`).
- `context/foundation/roadmap.md` (S-08, Backlog Handoff) and `context/foundation/prd.md`
  (FR-010, US-01, Guardrails); `context/foundation/health-check.md` (ADO integration risk).
