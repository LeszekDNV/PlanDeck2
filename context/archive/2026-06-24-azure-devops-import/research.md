---
date: 2026-06-24T11:30:00+02:00
researcher: Copilot CLI (Claude Opus 4.8)
git_commit: e09bb7b
branch: main
repository: LeszekDNV/PlanDeck2
topic: "S-03 azure-devops-import — connect Azure DevOps and import selected tasks (FR-003)"
tags: [research, codebase, azure-devops, import, sessions, grpc]
status: complete
last_updated: 2026-06-24
last_updated_by: Copilot CLI (Claude Opus 4.8)
---

# Research: S-03 azure-devops-import — connect Azure DevOps and import selected tasks

**Date**: 2026-06-24T11:30:00+02:00
**Researcher**: Copilot CLI (Claude Opus 4.8)
**Git Commit**: e09bb7b
**Branch**: main
**Repository**: LeszekDNV/PlanDeck2

## Research Question

What is needed to deliver roadmap slice **S-03 (`azure-devops-import`)**: a user can connect to Azure
DevOps and import selected tasks into PlanDeck (PRD FR-003)? The slice is framed as *wiring the existing
import client to persisted tasks and a selection UI*, not building the integration from scratch.

## Summary

**The S-03 outcome is, for the most part, already implemented in the current codebase.** The end-to-end
path — connect to ADO, fetch work items, let the user select a subset, and persist the selected items as
`SessionTask`s — exists and is wired in two places (the *create-session* dialog and the *active-session
configuration* panel). This was built incidentally alongside **S-04 (`create-configure-session`, status:
done)**, which shares the same task-creation machinery.

What exists today:

- **Integration plumbing (complete):** `IAzureDevOpsWorkItemClient` + `AzureDevOpsWorkItemClient` (WIQL
  query → `workitemsbatch` fetch, PAT auth, rate-limit/conflict handling), the code-first gRPC contract
  `IAzureDevOpsWorkItemService`, its server implementation `AzureDevOpsWorkItemGrpcService`, DI/config
  registration, gRPC endpoint mapping, and the client wrapper `AzureDevOpsClientService`.
- **Selection UI (present):** MudBlazor checkbox lists in both the create dialog and the config panel,
  with "Add selected (n)" buttons and per-item toggling.
- **Persistence (present):** selected work items become `NewSessionTaskDto` → `SessionTask` with
  `Source = AzureDevOps`, `AdoWorkItemId`, `AdoRevision`, `WorkItemType`, `State`; dedup at app layer and
  a filtered unique index at the DB layer.
- **Localization (present):** `Sessions_ImportAdo`, `Sessions_AddSelected`, `Sessions_EditAdoWarning` in
  both `en` and `pl`.
- **Tests (partial):** unit tests cover ADO-task dedup in `SessionGrpcService`. No E2E coverage and no
  direct test for `AzureDevOpsWorkItemGrpcService`/client.

So S-03 is best treated as a **hardening / gap-closing slice**, not a greenfield build. The main genuine
gaps are: (1) no UI to control *what* is fetched (WIQL filter / limit are hardcoded defaults), (2) the
active-session add path uses N per-task gRPC calls instead of the batch operation, (3) no E2E/import-path
tests, and (4) **ADO connection config is a single global `AzureDevOpsOptions`** (one org/project/PAT for
the whole app), which is not per-tenant — a product-scope question rather than a missing wire.

## Detailed Findings

### Integration client + contract (Infrastructure / Core.Shared / Application)

- `Core/PlanDeck.Infrastructure/AzureDevOps/AzureDevOpsWorkItemClient.cs:16-77` — `ImportWorkItemsAsync`:
  builds a WIQL query (`SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project AND
  {whereClause}`), default where clause limits to `User Story`/`Product Backlog Item`/`Bug`/`Task`,
  default limit 100, hard cap 200 (`:20`). Fetches details via `workitemsbatch` with `errorPolicy=Omit`.
- `AzureDevOpsWorkItemClient.cs:79-105` — `WriteEstimateAsync` (write-back; this is S-08 territory, not
  S-03, but lives in the same client). Uses JSON-Patch with optional `op:test /rev` for optimistic
  concurrency.
- `AzureDevOpsWorkItemClient.cs:120-160` — `BuildDescription` merges Description + Acceptance Criteria and
  converts HTML to text.
- `AzureDevOpsWorkItemClient.cs:182-214` — `SendAsync`/`CreateRequest`: PAT via Basic auth
  (`Convert.ToBase64String(":{PAT}")`), 429 → throws with Retry-After, 409/412 → revision-changed error.
- `Core/PlanDeck.Infrastructure/AzureDevOps/AzureDevOpsOptions.cs:1-18` — config: `OrganizationUrl`,
  `Project`, `EstimateField` (default `Microsoft.VSTS.Scheduling.StoryPoints`), `DescriptionField`,
  `AcceptanceCriteriaField`, `PersonalAccessToken`. **Single global instance — not per-tenant.**
- `Core/PlanDeck.Core.Shared/Contracts/IAzureDevOpsWorkItemService.cs:7-80` — `[Service]` contract:
  `ImportWorkItemsAsync` / `WriteEstimateAsync`, plus `AzureDevOpsWorkItemDto` (Id, Title, State,
  WorkItemType, Revision, Estimate, Description).
- `Core/PlanDeck.Application/Services/AzureDevOpsWorkItemGrpcService.cs:7-42` — thin mapping from contract
  request → `IAzureDevOpsWorkItemClient` → DTO reply.
- `Core/PlanDeck.Application/Abstractions/IAzureDevOpsWorkItemClient.cs:1-23` — abstraction + records.

### DI / config / endpoint wiring (Server / Client) — complete

- `Web/PlanDeck.Server/Extensions/ServiceCollectionExtensions.cs:70-71,111-112` — `Configure<AzureDevOpsOptions>(...)`
  + `AddHttpClient<IAzureDevOpsWorkItemClient, AzureDevOpsWorkItemClient>()` (registered in both the
  test-scheme and the real-auth branches).
- `ServiceCollectionExtensions.cs:126` — `services.AddScoped<AzureDevOpsWorkItemGrpcService>()`.
- `Web/PlanDeck.Server/Program.cs:92` — `app.MapGrpcService<AzureDevOpsWorkItemGrpcService>()`.
- `Web/PlanDeck.Client/Program.cs:25` — `AddScoped<IAzureDevOpsClientService, AzureDevOpsClientService>()`.
- `Web/PlanDeck.Client/Services/AzureDevOpsClientService.cs:9-30` — wraps the gRPC channel
  (`channel.CreateGrpcService<IAzureDevOpsWorkItemService>()`).

### Selection UI + persistence (Client) — present in two flows

Create-session dialog flow:
- `Web/PlanDeck.Client/Pages/Sessions.razor.cs:156-172` — `LoadAdoAsync` → `AdoService.ImportWorkItemsAsync()`
  (no args = default WIQL + limit 100), `RpcException` → `ShowError`.
- `Sessions.razor.cs:174-208` — `ToggleAdo` (HashSet selection) + `StageSelectedAdo` (maps selected →
  `NewSessionTaskDto` with `Source=AzureDevOps`, ADO fields; client-side dedup vs `_stagedTasks`).
- `Sessions.razor.cs:210-219` — `SubmitCreateAsync` folds checked-but-unstaged items via `StageSelectedAdo()`
  before creating the session.
- `Sessions.razor:425-448` — markup: "Import from Azure DevOps" button, loading spinner, checkbox list,
  "Add selected (n)" button.

Active-session config-panel flow:
- `Sessions.razor.cs:548-564` — `LoadConfigAdoAsync` (same default import call).
- `Sessions.razor.cs:566-576` — `ToggleConfigAdo`.
- `Sessions.razor.cs:578-630` — `AddSelectedAdoTasksAsync`: loops selected items, skips items already on
  the session, and calls `SessionService.AddTaskAsync(...)` **once per item** (N round-trips), then
  `ReplaceSelected(updated)`.
- `Sessions.razor:257-282` — markup mirror of the create dialog.
- `Sessions.razor:197-200,379-382` — task list renders an `ADO #<id>` `MudChip` for ADO-sourced tasks.
- `Sessions.razor:465-468` — edit dialog shows `Sessions_EditAdoWarning` ("changes are local, not written
  back") for ADO tasks.

### Server-side mapping, dedup, persistence

- `Core/PlanDeck.Application/Services/SessionGrpcService.cs:343-360` — `MapNewTask` copies ADO fields
  (`AdoWorkItemId`, `AdoRevision`, `WorkItemType`, `State`, `Source`) onto `SessionTask`.
- `SessionGrpcService.cs:334-335` — `IsDuplicateAdoTask`: skips when an `AdoWorkItemId` already exists on
  the session. Applied on create (`:38-39`), single add (`:124-125`), and bulk add (`:152-153`).
- `Core/PlanDeck.Core.Shared/Contracts/ISessionService.cs:23-26` — `AddTaskAsync` (single) and
  `AddTasksAsync` (batch) both exist; `NewSessionTaskDto` (`:126`) carries the ADO fields.
- `Core/PlanDeck.Application/Domain/SessionTask.cs:1-24` — entity has `Source`, `AdoWorkItemId`,
  `AdoRevision`, `WorkItemType`, `State`, `AgreedEstimate`. `TaskSource` enum (`AdHoc=0`, `AzureDevOps=1`)
  at `Domain/TaskSource.cs`.
- `Core/PlanDeck.Infrastructure/Migrations/20260619003148_AddSessionTaskAdoUniqueIndex.cs:13-18` — filtered
  unique index `IX_SessionTasks_SessionId_AdoWorkItemId` on `(SessionId, AdoWorkItemId)` `WHERE
  [AdoWorkItemId] IS NOT NULL`. Dedup is therefore **per-session**: the same work item can be imported into
  different sessions (likely intended), and a concurrent double-import within one session would surface as
  a `DbUpdateException` (app-layer dedup catches the common case, not the race).

### Localization

- `Web/PlanDeck.Client/Resources/SharedResource.resx` + `SharedResource.pl.resx` — existing ADO/import keys:
  `Sessions_ImportAdo`, `Sessions_AddSelected`, `Sessions_EditAdoWarning` (en + pl). Injection pattern:
  `@inject IStringLocalizer<SharedResource> L` (razor) / `L["Key"]` (code-behind).

### Tests

- `Tests/PlanDeck.Unit.Tests/Sessions/SessionGrpcServiceTests.cs` — NUnit with hand-written fakes (no
  Moq/NSubstitute). Covers `CreateSession_WithDuplicateAdoWorkItems_KeepsFirstOnly` and
  `AddTask_WithExistingAdoWorkItem_IsIdempotent`.
- `Tests/PlanDeck.E2e.Tests/Pages/SessionsPage.cs` — Playwright page object; **no ADO import helpers**.
- No dedicated test for `AzureDevOpsWorkItemGrpcService` or `AzureDevOpsWorkItemClient`.

## Code References

- `Core/PlanDeck.Infrastructure/AzureDevOps/AzureDevOpsWorkItemClient.cs:16-105` — WIQL import + write-back.
- `Core/PlanDeck.Infrastructure/AzureDevOps/AzureDevOpsOptions.cs:1-18` — single global ADO config.
- `Core/PlanDeck.Core.Shared/Contracts/IAzureDevOpsWorkItemService.cs:7-80` — import/write contract + DTO.
- `Core/PlanDeck.Application/Services/AzureDevOpsWorkItemGrpcService.cs:7-42` — gRPC mapping.
- `Web/PlanDeck.Client/Services/AzureDevOpsClientService.cs:9-30` — client wrapper.
- `Web/PlanDeck.Client/Pages/Sessions.razor.cs:156-219` — create-dialog import/select/stage.
- `Web/PlanDeck.Client/Pages/Sessions.razor.cs:548-630` — config-panel import/select/add.
- `Web/PlanDeck.Client/Pages/Sessions.razor:257-282,425-448` — import UI markup (both flows).
- `Core/PlanDeck.Application/Services/SessionGrpcService.cs:334-360` — dedup + ADO mapping.
- `Core/PlanDeck.Infrastructure/Migrations/20260619003148_AddSessionTaskAdoUniqueIndex.cs:13-18` — unique index.
- `Web/PlanDeck.Server/Extensions/ServiceCollectionExtensions.cs:70-71,111-112,126` — DI/config.
- `Web/PlanDeck.Server/Program.cs:92` — endpoint mapping.

## Architecture Insights

- Code-first gRPC over gRPC-Web (`protobuf-net.Grpc`): contracts in `Core.Shared/Contracts`, implementations
  in `Application/Services`, endpoints mapped in `Server/Program.cs` — the ADO feature follows this exactly.
- ADO import is **stateless on the server**: `ImportWorkItemsAsync` returns DTOs; the client holds selection
  state and persists via the *session* service. There is no server-side "import these ADO ids into session X"
  operation — persistence reuses the generic `AddTask(s)`/`CreateSession` task path.
- Layering respected: `AzureDevOpsWorkItemClient` (HTTP, Infrastructure) is hidden behind
  `IAzureDevOpsWorkItemClient` (Application abstraction); no ASP.NET/gRPC types leak into the client.
- `EstimateField`/`DescriptionField`/`AcceptanceCriteriaField` are configurable (resolves the change.md
  "which field is the estimate field" unknown at the *config* level) — but only globally, not per tenant.

## Historical Context (from prior changes)

- `context/foundation/roadmap.md:139-150` — S-03 definition; notes the import client already exists and the
  slice "wires it to persisted tasks and a selection UI"; on the north-star critical path because S-08
  writes back to ADO-sourced tasks; PAT/auth flagged as the main risk; open unknown = estimate field per
  tenant/project.
- `context/foundation/roadmap.md:152-163` — S-04 (`create-configure-session`, **done**) is the slice that
  created the session/task machinery the ADO import now rides on, which is why much of S-03 already exists.
- `context/changes/azure-devops-import/change.md` — this change's identity (status advanced new → preparing).

## Related Research

- None found under `context/changes/**/research.md` or `context/archive/**/`. (`context/foundation/lessons.md`
  is absent.)

## Open Questions

1. **Scope of S-03 vs. what's done.** Given the outcome already works end-to-end, should this change be
   reduced to a *hardening* slice (filter UI + tests + batch add), or does the product intend something
   bigger (per-tenant ADO connection)? — Decision owner: user. **Blocking for plan scope.**
2. **Per-tenant ADO connection.** `AzureDevOpsOptions` is one global org/project/PAT. For multi-tenant use,
   each tenant/team would need its own connection config (and secure PAT storage). Is that in scope for S-03,
   or deferred? — Owner: user. Likely the biggest remaining build if in scope.
3. **WIQL filter / limit in the UI.** Import currently uses the hardcoded default WIQL and limit 100 (cap
   200). FR-003 says "import *selected* tasks" — item selection exists, but there's no control over *what is
   fetched*. Add a filter/limit input, or accept defaults? — Owner: user.
4. **Active-session add efficiency/consistency.** `AddSelectedAdoTasksAsync` issues one `AddTaskAsync` per
   item; the create flow stages then bulk-creates. Switch the config-panel path to `AddTasksAsync` (batch)
   for consistency and fewer round-trips? — Low risk, recommended.
5. **Concurrency on the unique index.** App-layer dedup is per-session and not race-safe; a concurrent
   double-import surfaces as `DbUpdateException`. Add a friendly catch/translate, or accept as edge case?
   — Owner: user.
6. **Test coverage.** No E2E for the import→select→add path and no unit test for the ADO gRPC service/client.
   Recommended additions: `SessionsPage` ADO helpers + an E2E happy-path, and a unit test for
   `AzureDevOpsWorkItemGrpcService` mapping.
