# Azure DevOps Import (S-03) Hardening Implementation Plan

## Overview

The S-03 outcome — connect to Azure DevOps, fetch work items, select a subset, and persist them as
`SessionTask`s — already works end-to-end (built alongside S-04). This plan hardens that slice: it adds a
structured import **filter** (WorkItemType + State + limit), extracts a **shared import component** to kill
the create/config duplication, switches the active-session add path to a **batch** call, and adds **unit +
E2E test coverage** (with a fake ADO client so E2E never touches real Azure DevOps).

## Current State Analysis

- Integration is complete: `AzureDevOpsWorkItemClient` (WIQL → `workitemsbatch`, PAT auth, 429/409 handling)
  at `Core/PlanDeck.Infrastructure/AzureDevOps/AzureDevOpsWorkItemClient.cs:16-105`, contract
  `IAzureDevOpsWorkItemService` (`Core/PlanDeck.Core.Shared/Contracts/IAzureDevOpsWorkItemService.cs`),
  server `AzureDevOpsWorkItemGrpcService`, client wrapper `AzureDevOpsClientService`, DI/config and endpoint
  mapping all wired (`Web/PlanDeck.Server/Extensions/ServiceCollectionExtensions.cs:70-71,111-112,126`,
  `Program.cs:92`).
- The client wrapper already accepts a WIQL where clause + limit
  (`IAzureDevOpsClientService.ImportWorkItemsAsync(string? wiqlWhereClause = null, int limit = 100)`), but
  **no UI surfaces them** — both call sites pass defaults.
- The import/select/persist logic is **duplicated** between the create-session dialog
  (`_adoItems`/`_selectedAdo`, `LoadAdoAsync`, `StageSelectedAdo`, `Sessions.razor:425-448`) and the
  active-session config panel (`_configAdoItems`/`_configSelectedAdo`, `LoadConfigAdoAsync`,
  `AddSelectedAdoTasksAsync`, `Sessions.razor:257-282`).
- The config panel persists with **one `AddTaskAsync` per item** (`Sessions.razor.cs:578-630`); the create
  flow stages then bulk-creates. `ISessionClientService.AddTasksAsync(...)` (batch) already exists
  (`Services/ISessionClientService.cs:27`).
- Server-side dedup (`IsDuplicateAdoTask`, `SessionGrpcService.cs:334-335`) + a filtered unique index
  `IX_SessionTasks_SessionId_AdoWorkItemId` (migration `20260619003148`) already protect against duplicates.
- Tests: dedup is covered in `SessionGrpcServiceTests`; there is **no** test for
  `AzureDevOpsWorkItemGrpcService` and **no** E2E import coverage (`SessionsPage` has no ADO helpers).
- E2E boots the real server via Aspire with `Authentication__UseTestScheme=true`
  (`AspireAppFixture.cs`, `AppHost.cs:43-49`); the test-scheme branch of `AddExternalServices` registers the
  **real** HttpClient-backed ADO client, so an unmocked E2E import would call real Azure DevOps.

## Desired End State

A user importing from Azure DevOps (in both the create-session dialog and the active-session config panel)
sees a single, consistent import panel with **WorkItemType** and **State** multi-selects plus a **limit**
field, loads a filtered list, checks a subset, and adds them — staged on create, batch-persisted on config.
The create/config ADO duplication is gone. Unit tests cover the gRPC mapping and the WIQL builder; an E2E
test exercises import→select→add against a deterministic fake ADO client, so CI/E2E never depends on a real
Azure DevOps instance or PAT.

Verify: `dotnet build PlanDeck.slnx` succeeds; `dotnet test PlanDeck.slnx` (Podman running) passes including
the new unit + E2E tests; manual UI check shows filters working in both flows with `en`/`pl` labels.

### Key Discoveries:

- Client wrapper already plumbs WIQL + limit — only UI is missing (`Services/AzureDevOpsClientService.cs:9-19`).
- Component namespace `PlanDeck.Client.Components` is globally imported (`_Imports.razor:14`), so a new
  component drops into pages without an explicit `@using`.
- Repo convention: components use `.razor` + `.razor.cs` code-behind, **not** `@code` (the existing
  `MarkdownView.razor` `@code` block is a legacy exception — do not copy it).
- Test-scheme branch is the safe injection point for a fake (env-gated to Development/Testing only,
  `ServiceCollectionExtensions.cs:55-74`).

## What We're NOT Doing

- **No per-tenant ADO connection config** — `AzureDevOpsOptions` stays a single global org/project/PAT.
- **No write-back changes** (`WriteEstimateAsync` / S-08) — left untouched.
- **No free-text WIQL input** — filters are structured selects mapped to WIQL client-side.
- **No new server gRPC operation** — persistence keeps reusing `CreateSession` / `AddTasks`.
- **No ADO sync/refresh** of already-imported tasks (stale revision handling stays out of scope).
- **No change to the unique index, dedup, or concurrency model.**

## Implementation Approach

Phase 1 is a client-side refactor + feature: a pure `AzureDevOpsWiqlBuilder` in `Core.Shared` turns selected
types/states into a WIQL `WHERE` fragment; a new `AdoImportPanel` component owns filters + load + list +
selection and raises selected items via an `OnAddSelected` callback, leaving the parent to decide *stage*
(create dialog) vs *batch-persist* (config panel). `Sessions.razor(.cs)` is rewired to use the component in
both places and the duplicated ADO fields/methods are deleted. Phase 2 adds a fake `IAzureDevOpsWorkItemClient`
registered under the test scheme plus unit tests (gRPC mapping, WIQL builder) and an E2E happy-path.

## Critical Implementation Details

- **WIQL injection safety**: filter values come from fixed predefined lists, but the builder must still
  single-quote values and double any embedded `'` so the generated `[System.WorkItemType] IN (...)` /
  `[System.State] IN (...)` fragment is well-formed. Both dimensions empty ⇒ return `null` so the client
  falls back to its existing default WHERE clause.
- **Fake client scope**: the fake replaces the real ADO client **only** inside the `useTestScheme` branch of
  `AddExternalServices`; the production/real-auth branch is untouched, so real ADO behavior is unaffected.

## Phase 1: Shared ADO import component, structured filters, and refactor

### Overview

Introduce the WIQL builder and the reusable `AdoImportPanel`, surface WorkItemType/State/limit filters, and
replace the duplicated ADO logic in both the create dialog and the config panel — with the config flow now
persisting via the batch `AddTasksAsync`.

### Changes Required:

#### 1. WIQL builder (shared, pure)

**File**: `Core/PlanDeck.Core.Shared/AzureDevOps/AzureDevOpsWiqlBuilder.cs` (new)

**Intent**: Centralize, in a unit-testable pure function, the translation of selected WorkItemType/State
values into a WIQL `WHERE` fragment that the client passes to `ImportWorkItemsAsync`.

**Contract**: `public static string? BuildWhereClause(IReadOnlyCollection<string> workItemTypes,
IReadOnlyCollection<string> states)`. Returns `null` when both collections are empty. Otherwise emits
`[System.WorkItemType] IN ('a','b')` and/or `[System.State] IN ('x','y')` joined with ` AND `. Each value is
wrapped in single quotes with embedded `'` doubled.

#### 2. Reusable import panel component

**File**: `Web/PlanDeck.Client/Components/AdoImportPanel.razor` (+ `AdoImportPanel.razor.cs` code-behind) (new)

**Intent**: One MudBlazor component owning the entire import UX — type/state multi-selects, a limit input, a
"load" action, the result checkbox list, and selection state — emitting the chosen work items to its parent.
Both the create dialog and the config panel embed it.

**Contract**: Parameters: `[Parameter] IReadOnlyCollection<int> AlreadyPresentIds` (ids to mark/skip as
already added), `[Parameter] bool Busy` (parent-driven disable while persisting), `[Parameter]
EventCallback<IReadOnlyList<AzureDevOpsWorkItemDto>> OnAddSelected`. Internal state: selected
`WorkItemType`/`State` value sets, `int Limit` (default 100), loaded `List<AzureDevOpsWorkItemDto>`, selected
id set, loading flag. On "load": builds the WHERE clause via `AzureDevOpsWiqlBuilder.BuildWhereClause(...)`
and calls `AdoService.ImportWorkItemsAsync(where, Limit)`, catching `RpcException` → `Snackbar`. On "add":
invokes `OnAddSelected` with the selected items, then clears selection. Predefined option lists are
component constants: WorkItemTypes = `User Story`, `Product Backlog Item`, `Bug`, `Task`; States = `New`,
`Active`, `Resolved`, `Closed`, `Committed`, `Done`. Code-behind partial class
`PlanDeck.Client.Components.AdoImportPanel` (no `@code`).

#### 3. Localization keys

**Files**: `Web/PlanDeck.Client/Resources/SharedResource.resx` and `SharedResource.pl.resx`

**Intent**: Add resource strings for the new filter controls; reuse existing `Sessions_ImportAdo` (load) and
`Sessions_AddSelected` (add) keys.

**Contract**: New keys (en / pl): `Sessions_AdoWorkItemType` ("Work item type" / "Typ elementu pracy"),
`Sessions_AdoState` ("State" / "Stan"), `Sessions_AdoLimit` ("Max items" / "Maksymalna liczba"),
`Sessions_AdoNoResults` ("No work items found" / "Nie znaleziono elementów pracy").

#### 4. Rewire create dialog + config panel to the component

**Files**: `Web/PlanDeck.Client/Pages/Sessions.razor` and `Sessions.razor.cs`

**Intent**: Replace both bespoke ADO markup blocks (`:425-448` and `:257-282`) with `<AdoImportPanel ... />`,
and delete the duplicated code-behind ADO state/methods (`_adoItems`, `_selectedAdo`, `_configAdoItems`,
`_configSelectedAdo`, `_adoLoading`, `_configAdoLoading`, `LoadAdoAsync`, `ToggleAdo`, `StageSelectedAdo`,
`LoadConfigAdoAsync`, `ToggleConfigAdo`, `AddSelectedAdoTasksAsync`). Provide two thin parent handlers.

**Contract**: Create-dialog instance binds `AlreadyPresentIds` to staged ADO ids and `OnAddSelected` to a
handler that stages each not-already-staged item as a `NewSessionTaskDto` (`Source=AzureDevOps`, ADO fields)
into `_stagedTasks`. Config-panel instance binds `AlreadyPresentIds` to `_selected.Tasks` ADO ids, `Busy` to
`_addingTask`, and `OnAddSelected` to a handler that maps non-duplicate items to `NewSessionTaskDto` and
calls `SessionService.AddTasksAsync(_selected.Id, list)` once (batch), then `ReplaceSelected(updated)` with
`RpcException` → `ShowError`. Keep `SubmitCreateAsync`'s existing staging-before-create behavior intact.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build PlanDeck.slnx`
- Existing unit tests pass: `dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj`

#### Manual Verification:

- In the create dialog: selecting types/states + limit, loading, checking items, and "Add selected" stages
  tasks that persist on create with an `ADO #<id>` chip.
- In the config panel: same flow persists selected tasks via a single batch call and they render immediately.
- Re-adding an already-present work item does not create a duplicate.
- Labels render correctly in both `en` and `pl`.

**Implementation Note**: After completing this phase and all automated verification passes, pause for manual
confirmation that the UI testing was successful before proceeding to Phase 2.

---

## Phase 2: Test coverage (fake ADO client, unit, E2E)

### Overview

Add a deterministic fake ADO client behind the test scheme so E2E never hits real Azure DevOps, then add unit
tests for the gRPC mapping and the WIQL builder, and an E2E happy-path for import→select→add.

### Changes Required:

#### 1. Fake ADO client for the test scheme

**File**: `Web/PlanDeck.Server/Testing/FakeAzureDevOpsWorkItemClient.cs` (new)

**Intent**: A deterministic in-memory `IAzureDevOpsWorkItemClient` so the test-scheme server returns a fixed
set of work items without network/PAT, enabling E2E import.

**Contract**: Implements `IAzureDevOpsWorkItemClient`. `ImportWorkItemsAsync` returns a small fixed list of
`AzureDevOpsWorkItem`s (spanning a couple of types/states, with ids/titles/revisions), honoring
`request.Limit` via `Take`. `WriteEstimateAsync` returns `new AzureDevOpsWriteEstimateResult(request.WorkItemId,
request.ExpectedRevision.GetValueOrDefault() + 1)`. Lives under a `Testing` namespace in the Server project,
mirroring the existing `TestAuthenticationHandler` test-only type.

#### 2. Register the fake under the test scheme

**File**: `Web/PlanDeck.Server/Extensions/ServiceCollectionExtensions.cs`

**Intent**: In the `useTestScheme` branch, use the fake instead of the real HttpClient-backed client; leave
the real-auth branch unchanged.

**Contract**: In the `if (useTestScheme)` block (around `:62-74`), replace
`services.AddHttpClient<IAzureDevOpsWorkItemClient, AzureDevOpsWorkItemClient>()` with
`services.AddScoped<IAzureDevOpsWorkItemClient, FakeAzureDevOpsWorkItemClient>()` (the
`Configure<AzureDevOpsOptions>` line can stay or go — the fake ignores it). Real-auth branch keeps the
HttpClient registration.

#### 3. Unit test — gRPC service mapping

**File**: `Tests/PlanDeck.Unit.Tests/AzureDevOps/AzureDevOpsWorkItemGrpcServiceTests.cs` (new)

**Intent**: Verify `AzureDevOpsWorkItemGrpcService` maps domain work items → DTOs and passes the WIQL
where-clause + limit straight through to the client.

**Contract**: NUnit `[TestFixture]` with a hand-written fake `IAzureDevOpsWorkItemClient` (matching the
repo's no-mocking-library convention). Assert `ImportWorkItemsAsync` forwards `WiqlWhereClause`/`Limit` into
`AzureDevOpsImportRequest` and maps every field (Id/Title/State/WorkItemType/Revision/Estimate/Description)
onto `AzureDevOpsWorkItemDto`; assert `WriteEstimateAsync` maps `WorkItemId`/`Revision`.

#### 4. Unit test — WIQL builder

**File**: `Tests/PlanDeck.Unit.Tests/AzureDevOps/AzureDevOpsWiqlBuilderTests.cs` (new)

**Intent**: Lock the builder's contract: empty/both-empty ⇒ null; single dimension ⇒ one `IN (...)`; both ⇒
`AND`-joined; values quoted and `'` escaped.

**Contract**: NUnit cases over `AzureDevOpsWiqlBuilder.BuildWhereClause(...)` asserting exact string output
for representative inputs.

#### 5. E2E — import happy path

**Files**: `Tests/PlanDeck.E2e.Tests/Pages/SessionsPage.cs` and `Tests/PlanDeck.E2e.Tests/SessionsTests.cs`

**Intent**: Add page-object helpers to drive the import panel and a test asserting fake-sourced work items
get added to a session.

**Contract**: `SessionsPage` gains locators/methods to open the import panel, trigger load, select the
work-item checkboxes, click "Add selected", and (create flow) save. New `SessionsTests` case creates a
session, imports + selects at least one fake work item, and asserts the task list shows the `ADO #<id>` chip.
Follows the existing `PageTest` + Page Object pattern (WASM-aware waits on a known element).

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build PlanDeck.slnx`
- New + existing unit tests pass: `dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj`
- E2E suite passes locally (Podman running): `dotnet test Tests/PlanDeck.E2e.Tests/PlanDeck.E2e.Tests.csproj`

#### Manual Verification:

- The E2E import test passes without any real Azure DevOps configuration (fake client serves the data).
- The fake client is active only under the test scheme; a normal `dotnet run` still uses the real client.

**Implementation Note**: After this phase and all automated verification passes, pause for manual
confirmation before considering the change complete.

---

## Testing Strategy

### Unit Tests:

- `AzureDevOpsWorkItemGrpcService` field mapping + WIQL/limit pass-through (fake client).
- `AzureDevOpsWiqlBuilder` output for empty / single-dimension / both-dimension / quote-escaping inputs.

### Integration Tests:

- None new; existing session persistence integration tests remain the coverage for `SessionTask` writes.

### Manual Testing Steps:

1. Create a session, expand the ADO import panel, pick a WorkItemType and State, set a limit, load.
2. Check a couple of items, "Add selected", confirm they appear staged and persist on create with chips.
3. On an active (editable) session, repeat via the config panel; confirm a single batch persist.
4. Re-import an already-added item; confirm no duplicate.
5. Switch UI culture to `pl`; confirm filter labels are localized.

## Performance Considerations

Batch persist in the config flow replaces N gRPC round-trips with one. Import volume is capped at 200 by the
existing client; no new performance concerns.

## Migration Notes

None — no schema or data changes.

## References

- Related research: `context/changes/azure-devops-import/research.md`
- Client wrapper (WIQL/limit already plumbed): `Web/PlanDeck.Client/Services/AzureDevOpsClientService.cs:9-19`
- Existing duplicated ADO UI: `Web/PlanDeck.Client/Pages/Sessions.razor:257-282,425-448`
- Server dedup + mapping: `Core/PlanDeck.Application/Services/SessionGrpcService.cs:334-360`
- Test scheme injection point: `Web/PlanDeck.Server/Extensions/ServiceCollectionExtensions.cs:55-74`
- E2E fixture / test-auth wiring: `Tests/PlanDeck.E2e.Tests/AspireAppFixture.cs`, `Aspire/PlanDeck.AppHost/AppHost.cs:43-49`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Shared ADO import component, structured filters, and refactor

#### Automated

- [x] 1.1 Solution builds: `dotnet build PlanDeck.slnx`
- [x] 1.2 Existing unit tests pass: `dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj`

#### Manual

- [x] 1.3 Create dialog: filter + load + select + add stages tasks that persist with ADO chips
- [x] 1.4 Config panel: same flow persists via a single batch call and renders immediately
- [x] 1.5 Re-adding an already-present work item does not duplicate
- [x] 1.6 Filter labels render in both `en` and `pl`

### Phase 2: Test coverage (fake ADO client, unit, E2E)

#### Automated

- [ ] 2.1 Solution builds: `dotnet build PlanDeck.slnx`
- [ ] 2.2 New + existing unit tests pass: `dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj`
- [ ] 2.3 E2E suite passes locally (Podman): `dotnet test Tests/PlanDeck.E2e.Tests/PlanDeck.E2e.Tests.csproj`

#### Manual

- [ ] 2.4 E2E import test passes with no real Azure DevOps configured (fake serves data)
- [ ] 2.5 Fake client active only under the test scheme; normal `dotnet run` uses the real client
