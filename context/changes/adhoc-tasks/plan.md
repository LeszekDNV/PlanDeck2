# Ad-hoc Tasks (S-02) — Implementation Plan

## Overview

S-02 extends the existing ad-hoc task capability into a fuller manual task-authoring experience on the planning session. FR-004 ("create ad-hoc tasks manually") is already delivered inside the session flow (S-04), so this change does **not** add a standalone task source — it adds the editing, description, bulk-entry, active-session management, and UI-quality work the user asked for:

1. Edit an existing task (title + new description) — add / edit / delete.
2. New optional `Description` field on tasks (`nvarchar(max)`).
3. Add / edit / delete tasks **after** the session is activated (relaxing the current Draft-only rule for task operations), with the live voting room kept in sync in real time.
4. Bulk-add tasks by pasting multiple lines (one task per line, optional `title | description` separator).
5. A modern, full-width, responsive redesign of `/sessions` with subtle animations on meaningful changes.

## Current State Analysis

- **Tasks are session-owned snapshot children.** `SessionTask : TenantEntity` (`Core/PlanDeck.Application/Domain/SessionTask.cs`) holds `Title`, `Source` (`TaskSource.AdHoc`/`AzureDevOps`), `SortOrder`, ADO snapshot fields, and `AgreedEstimate`. No `Description`. EF config `SessionTaskConfiguration.cs` (Title `HasMaxLength(500)`, no description).
- **Ad-hoc create/add already works.** `SessionGrpcService.CreateSessionAsync` and `AddTaskAsync` map `NewSessionTaskDto{Source=AdHoc}` via `MapNewTask` (`Services/SessionGrpcService.cs:94,235`), validating a non-empty title (`SessionValidationMessages.TaskTitleRequired`). `RemoveTaskAsync` deletes. There is **no update operation** and **no bulk operation**.
- **Editing is Draft-only.** `AddTaskAsync`/`RemoveTaskAsync`/`UpdateSessionConfigAsync` all call `LoadDraftAsync`, which throws `SessionNotDraftException` (→ `FailedPrecondition`) when the session is `Active`. The UI mirrors this with `_isLocked` (`Sessions.razor.cs:48`) hiding all task controls once Active.
- **Live voting room seeds once.** `PlanningRoomService.EnsureSeeded` populates an in-memory `PlanningRoom` from DB tasks only `if (!room.Seeded)` (`Planning/PlanningRoomService.cs:22`). The SignalR `PlanningRoomHub.JoinRoom` calls it on join (`Hubs/PlanningRoomHub.cs:29`). There is **no path to add/update/remove a task in a live room** after seeding. `PlanningTaskState` / `RoomTask` carry no description.
- **Seed shape.** `IVotingRoundService.AuthorizeAndLoadSeedAsync` returns tasks as `(Guid TaskId, string Title, int SortOrder, string? AgreedEstimate)` — no description.
- **Wire contracts** live in `Core.Shared/Contracts/ISessionService.cs` (`SessionTaskDto`, `NewSessionTaskDto`, request/reply types) and `Core.Shared/Realtime/PlanningRoomState.cs`. Both are shared client/server.
- **UI.** `/sessions` (`Sessions.razor` + `.razor.cs`) is a single vertical `MudContainer MaxWidth.Medium`: a session list, then a config `MudCard` (name/team/scale, task list with delete-only, ad-hoc add field, ADO import, member assignment), then a create `MudDialog`. Task list shows title + ADO chip; no edit, no description, no bulk.
- **Localization.** Single `SharedResource.resx` (en) + `SharedResource.pl.resx`. All session strings keyed `Sessions_*`. Validation message constants in `Core.Shared/Validation/SessionValidationMessages.cs` mapped to resx keys in `Sessions.razor.cs:552`.
- **Tests.** Unit `SessionGrpcServiceTests.cs`, `PlanningRoomServiceTests.cs`; integration `Persistence/Session*Tests.cs` (real MSSQL via Aspire); E2E `SessionsTests.cs` + `Pages/SessionsPage.cs` (Playwright). Realtime integration `Realtime/PlanningRoomHubTests.cs`.

## Desired End State

A signed-in user opens `/sessions` and sees a modern, full-width master-detail layout (session list left, configuration right; single column on mobile). For any session — **Draft or Active** — they can add tasks one at a time or by pasting many lines at once (`title | description`), edit a task's title and optional description in a dialog, and delete tasks (with a confirmation dialog when the task already has an agreed estimate). Editing an ADO-sourced task shows a "local only, not written back to Azure DevOps" warning. Each task may carry a free-form description (`nvarchar(max)`). When a session is Active and a live voting room is open, task add/edit/delete propagate to all connected participants in real time, preserving in-flight votes on surviving tasks. In the voting room the current task shows its title (highlighted) plus a collapsible description section above the voting panel. All new UI text is localized en/pl. Meaningful changes (task add/remove, session selection, config panel reveal, save confirmation) animate subtly via built-in MudBlazor transitions.

Verified by: unit tests (service edit/bulk/active rules, room reconciliation), MSSQL integration tests (description persistence + tenant isolation), and Playwright E2E (edit, bulk paste, two-client realtime propagation, responsive smoke).

### Key Discoveries:

- Ad-hoc creation is **already done** — this change is editing/description/bulk/active-management/UI, not a new task source (`Sessions.razor.cs:99,356`; `SessionGrpcService.cs:235`).
- The Draft-only invariant is enforced **server-side** in `LoadDraftAsync` (`SessionGrpcService.cs:176`) — relaxing item 3 means splitting "config edits" (stay Draft-only) from "task edits" (allowed in Active).
- The live room is **seed-once** with no mutation path (`PlanningRoomService.cs:22`) — real-time propagation requires a new reconcile method + a Server-side notifier that owns `IHubContext` (Application must not reference SignalR hosting types — layering rule).
- `PlanningTaskState`/`RoomTask`/the seed tuple all need a `Description` to surface it in the voting room.

## What We're NOT Doing

- No standalone/persisted task backlog or `/tasks` page (ad-hoc remains session-scoped).
- No write-back of edited titles/descriptions to Azure DevOps (that is S-08; ADO edits are local only).
- No task reordering / drag-and-drop (not requested).
- No change to voting mechanics, reveal/hidden-vote integrity, scales, membership, or guest links.
- No new heavy animation/UI libraries — MudBlazor built-ins only.
- No bulk **edit** (bulk applies to add only); editing is one task at a time.
- No rich/WYSIWYG markdown editor or formatting toolbar — description **input** stays plain text; markdown is interpreted on **display only**.

## Implementation Approach

Build inward-out in five phases. First land the data + wire contract (`Description`, update + bulk operations), then the Application service rules (edit/bulk + relaxing Draft-only for task ops while keeping config Draft-only), then the real-time propagation layer (a new `IPlanningRoomNotifier` abstraction implemented in the Server over `IHubContext`, plus a `PlanningRoomService.SyncTasks` reconcile that preserves votes/current task), then the client redesign + features, and finally full E2E/verification. Each phase compiles and tests green before the next.

## Critical Implementation Details

- **Layering for real-time propagation.** Task mutations are handled in `SessionGrpcService` (Application). The live room is SignalR (Server). Application must not depend on SignalR hosting types, so introduce `IPlanningRoomNotifier` (Application abstraction) with a method like `NotifyTasksChangedAsync(Guid sessionId, ...)`. `SessionGrpcService` calls it after a successful task mutation **only when the session is Active**. The Server implements it (`SignalRPlanningRoomNotifier`) using the in-memory `PlanningRoomService.SyncTasks(...)` to reconcile state, then broadcasts the resulting `PlanningRoomState` to the room group via `IHubContext<PlanningRoomHub>` under the `RoomKey` (tenant + session). A no-op/default notifier keeps unit tests and non-hosted paths simple.
- **Reconcile must preserve in-flight state.** `SyncTasks` adds new tasks, updates `Title`/`Description`/`SortOrder` on survivors, removes deleted ones, keeps each survivor's `Votes`/`IsRevealed`/`AgreedEstimate`, and only resets `CurrentTaskId` if the current task was deleted (then fall back to the first by `SortOrder`).
- **Active vs Draft split.** Introduce a task-scoped loader (e.g. `LoadEditableAsync`) that allows Draft **and** Active for `AddTask`/`UpdateTask`/`RemoveTask`/bulk, while `UpdateSessionConfigAsync` keeps `LoadDraftAsync` (name/scale/team stay Draft-only).
- **Bulk parsing is client-side.** The textarea is parsed in the client into a `List<NewSessionTaskDto>` (split lines; per line split on first `|` into title/description; trim; skip empty titles; dedup by title within the batch). The server `AddTasksAsync` receives the already-structured list and validates each title.
- **Description is Markdown, rendered display-only.** The stored description is treated as Markdown. Input is a plain `MudTextField` (multiline); rendering converts Markdown → HTML client-side with **Markdig** configured via `.DisableHtml()` (raw/inline HTML stripped) and rendered through a `MarkupString`, so user-authored content cannot inject script/HTML (XSS-safe without a separate sanitizer). Markdig is referenced from `PlanDeck.Client` (rendering is a WASM concern). Markdown is rendered only where the description is **shown** — the voting-room collapsible section and an optional preview in the edit dialog — never in the plain-text input.

## Phase 1: Domain, Contract & Persistence

### Overview

Add the `Description` field and the new wire operations so client and server share the contract; migrate the database.

### Changes Required:

#### 1. Domain entity

**File**: `src/PlanDeck/Core/PlanDeck.Application/Domain/SessionTask.cs`

**Intent**: Add an optional free-form description to the task.

**Contract**: `public string? Description { get; set; }`.

#### 2. EF configuration

**File**: `src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/Configurations/SessionTaskConfiguration.cs`

**Intent**: Map `Description` as unbounded text.

**Contract**: Configure `Description` with **no** `HasMaxLength` so it maps to `nvarchar(max)`.

#### 3. Migration

**File**: `src/PlanDeck/Core/PlanDeck.Infrastructure/Migrations/` (new `*_AddSessionTaskDescription`)

**Intent**: Add the nullable `Description nvarchar(max)` column to `SessionTasks`.

**Contract**: EF Core migration generated via `dotnet ef migrations add AddSessionTaskDescription` against `PlanDeckDbContext`; update model snapshot. Applied automatically on startup in Development.

#### 4. Wire DTOs

**File**: `src/PlanDeck/Core/PlanDeck.Core.Shared/Contracts/ISessionService.cs`

**Intent**: Carry description across the wire and add update + bulk-add operations.

**Contract**:
- Add `[DataMember(Order = n)] public string? Description` to `SessionTaskDto` and `NewSessionTaskDto` (next free order).
- Add `UpdateTaskAsync(UpdateTaskRequest, CallContext)` → `UpdateTaskReply{ SessionDto Session }`; `UpdateTaskRequest{ Guid SessionId, Guid TaskId, string Title, string? Description }`.
- Add `AddTasksAsync(AddTasksRequest, CallContext)` → `AddTasksReply{ SessionDto Session }`; `AddTasksRequest{ Guid SessionId, List<NewSessionTaskDto> Tasks }`.
- All new request/reply types `[DataContract]` with ordered `[DataMember]`s following the file's existing style.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build PlanDeck.slnx`
- Migration is created and model snapshot updated (file exists under `Migrations/`)
- Integration test proves `Description` round-trips and is tenant-isolated: `dotnet test Tests/PlanDeck.Integration.Tests/PlanDeck.Integration.Tests.csproj`

#### Manual Verification:

- App starts and applies the migration cleanly (Aspire run; Podman running)
- New column is `nvarchar(max)` and nullable in the created schema

**Implementation Note**: After completing this phase and all automated verification passes, pause for manual confirmation before proceeding.

---

## Phase 2: Application Service Logic

### Overview

Implement edit + bulk operations and allow task operations while Active, keeping config edits Draft-only.

### Changes Required:

#### 1. Task-editable loader

**File**: `src/PlanDeck/Core/PlanDeck.Application/Services/SessionGrpcService.cs`

**Intent**: Allow task add/update/remove/bulk in both Draft and Active; keep `UpdateSessionConfigAsync` Draft-only.

**Contract**: Add a private `LoadEditableAsync` (loads any non-deleted session regardless of status) used by task operations; leave `LoadDraftAsync` on `UpdateSessionConfigAsync`.

#### 2. Update + bulk + description mapping

**File**: `src/PlanDeck/Core/PlanDeck.Application/Services/SessionGrpcService.cs`

**Intent**: Implement `UpdateTaskAsync` (set title+description on an existing task, validating non-empty title) and `AddTasksAsync` (append a validated list, continuing the `SortOrder` sequence, honoring the existing ADO-duplicate guard); thread `Description` through `MapNewTask` and both `ToDto` mappers.

**Contract**: `UpdateTaskAsync` resolves the task by id within the loaded session, applies trimmed title (reject empty via `TaskTitleRequired`) and `Description` (null/trim), persists, returns updated `SessionDto`. `AddTasksAsync` maps each `NewSessionTaskDto`, skips ADO duplicates, persists once. Editing applies to ADO-sourced tasks too (no source restriction server-side).

#### 3. Active-session real-time hook

**File**: `src/PlanDeck/Core/PlanDeck.Application/Services/SessionGrpcService.cs` + new `src/PlanDeck/Core/PlanDeck.Application/Abstractions/IPlanningRoomNotifier.cs`

**Intent**: After a successful task mutation on an **Active** session, notify the live room so participants see the change. Application depends only on its own abstraction.

**Contract**: `IPlanningRoomNotifier.NotifyTasksChangedAsync(Guid sessionId, IReadOnlyList<(Guid TaskId, string Title, string? Description, int SortOrder, string? AgreedEstimate)> tasks, CancellationToken)`. Inject into `SessionGrpcService`; call it from `AddTaskAsync`/`AddTasksAsync`/`UpdateTaskAsync`/`RemoveTaskAsync` when `session.Status == Active`. Provide a default no-op implementation registered for non-hosted/test paths.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build PlanDeck.slnx`
- Unit tests cover: update title+description, bulk add (incl. dedup + sort continuation), edit allowed while Active, config still rejected while Active, empty-title rejection: `dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj`

#### Manual Verification:

- N/A (pure backend; covered by automated tests)

**Implementation Note**: Pause for manual confirmation after automated verification passes.

---

## Phase 3: Real-time Propagation to the Live Voting Room

### Overview

Reconcile the in-memory room with DB task changes and broadcast, surfacing descriptions in room state.

### Changes Required:

#### 1. Description in realtime state

**File**: `src/PlanDeck/Core/PlanDeck.Core.Shared/Realtime/PlanningRoomState.cs`

**Intent**: Carry description to clients in the voting room.

**Contract**: Add `string? Description` to `PlanningTaskState`.

#### 2. Seed shape + reconcile

**File**: `src/PlanDeck/Core/PlanDeck.Application/Planning/PlanningRoomService.cs` (+ `IPlanningRoomService.cs`, `IVotingRoundService`/seed tuple)

**Intent**: Add `Description` to `RoomTask` and the seed tuple; add a `SyncTasks` reconcile method that merges DB task changes into a live room while preserving votes and the active task.

**Contract**: `RoomTask` gains `Description`. `EnsureSeeded` and `AuthorizeAndLoadSeedAsync` task tuple extended with `string? Description`. New `PlanningRoomService.SyncTasks(RoomKey key, IReadOnlyList<(Guid TaskId, string Title, string? Description, int SortOrder, string? AgreedEstimate)> tasks)`: under the room lock, upsert survivors (update Title/Description/SortOrder, keep Votes/IsRevealed/AgreedEstimate), add new, remove missing, reset `CurrentTaskId` to first-by-SortOrder only if the current task was removed; returns `PlanningRoomState`. `ToState` includes `Description`.

#### 3. Server-side notifier

**File**: `src/PlanDeck/Web/PlanDeck.Server/Realtime/SignalRPlanningRoomNotifier.cs` (new) + DI in `Extensions/ServiceCollectionExtensions.cs`

**Intent**: Implement `IPlanningRoomNotifier` in the host: reconcile the in-memory room and broadcast the new state to the session's SignalR group.

**Contract**: Implementation resolves the `RoomKey` (tenant from `ICurrentUserContext`, session id), calls `PlanningRoomService.SyncTasks`, then `IHubContext<PlanningRoomHub>.Clients.Group(key.GroupName).SendAsync("RoomStateChanged", state)`. Registered in place of the no-op default via the existing `AddLocalServices`/`AddExternalServices` extension blocks.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build PlanDeck.slnx`
- `PlanningRoomServiceTests` cover `SyncTasks`: add/update/remove, votes preserved on survivors, current-task reset only on deletion: `dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj`
- Realtime integration still green: `dotnet test Tests/PlanDeck.Integration.Tests/PlanDeck.Integration.Tests.csproj`

#### Manual Verification:

- With two browsers in one Active session's voting room, adding/editing/deleting a task from `/sessions` updates both rooms live; in-flight votes on surviving tasks remain

**Implementation Note**: Pause for manual confirmation after automated verification passes.

---

## Phase 4: Client UI — Redesign & Features

### Overview

Rebuild `/sessions` as a responsive full-width master-detail with edit dialog, bulk paste, descriptions, Active-session task management, animations, and en/pl strings; surface task description in the voting room.

### Changes Required:

#### 1. Client service methods

**File**: `src/PlanDeck/Web/PlanDeck.Client/Services/ISessionClientService.cs` + `SessionClientService.cs`

**Intent**: Expose the new `UpdateTaskAsync` and `AddTasksAsync` (and `Description` on DTO mapping is automatic) to components.

**Contract**: `UpdateTaskAsync(Guid sessionId, Guid taskId, string title, string? description)` and `AddTasksAsync(Guid sessionId, IReadOnlyList<NewSessionTaskDto> tasks)` calling the gRPC contract, returning `SessionDto`.

#### 2. Master-detail redesign

**File**: `src/PlanDeck/Web/PlanDeck.Client/Pages/Sessions.razor` (+ `.razor.cs`)

**Intent**: Replace the `MaxWidth.Medium` vertical layout with a full-width responsive `MudGrid` (sessions list in a left column, configuration in a right column; single column on small breakpoints). Apply subtle MudBlazor transitions (e.g. `MudCollapse` for the config panel and task additions/removals) on session selection, panel reveal, and save confirmation.

**Contract**: `MudContainer MaxWidth.False` (or `ExtraExtraLarge`) wrapping a `MudGrid` with responsive `MudItem` breakpoints; existing handlers reused. No behavior change to create/activate/member flows beyond layout.

#### 3. Task editing + description + Active management

**File**: `src/PlanDeck/Web/PlanDeck.Client/Pages/Sessions.razor` (+ `.razor.cs`)

**Intent**: Add an edit dialog (title + description) reachable per task; show task management controls in **both** Draft and Active (remove the blanket `_isLocked` gating of task controls — keep it only for config fields name/team/scale); confirm deletion when `task.AgreedEstimate` is set; show a "local only, not written back to ADO" warning when editing an ADO-sourced task. The description field is a plain multiline `MudTextField` (Markdown source) with an optional live rendered preview.

**Contract**: New `MudDialog` bound to an editing task model; `EditTaskAsync` calls `SessionService.UpdateTaskAsync`. Delete path calls `Dialog.ShowMessageBoxAsync` when `AgreedEstimate is not null`. Config field `Disabled` bindings switch from `_isLocked` to a config-only lock; task controls become status-independent. Description preview uses the shared Markdown component (item 7).

#### 4. Bulk paste component

**File**: `src/PlanDeck/Web/PlanDeck.Client/Pages/Sessions.razor` (+ `.razor.cs`), shared parse helper

**Intent**: Provide a multi-line textarea (in the create dialog and the config panel) that parses pasted text into tasks and submits them via `AddTasksAsync` (config) or stages them (create dialog).

**Contract**: A `ParseBulkTasks(string raw) -> List<NewSessionTaskDto>`: split on newlines; per line split on the first `|` into `Title`/`Description`; trim; skip empty titles; dedup by title (ordinal-ignore-case) within the batch; `Source = AdHoc`. Reuse in both surfaces.

#### 5. Voting room description

**File**: `src/PlanDeck/Web/PlanDeck.Client/Pages/VotingRoom.razor` (+ `.razor.cs`)

**Intent**: Above the voting panel, show the current task title (highlighted) and a collapsible section that renders the description as **Markdown** (display-only), fed by `PlanningTaskState.Description`.

**Contract**: Bind to the active task's `Description`; render via the shared Markdown component (item 7) inside a `MudCollapse`/expansion section; hidden/empty state when no description.

#### 6. Localization

**File**: `src/PlanDeck/Web/PlanDeck.Client/Resources/SharedResource.resx` + `SharedResource.pl.resx`

**Intent**: Add all new UI strings (edit task, description label, description-supports-markdown hint, preview label, bulk paste label/hint/placeholder, ADO-edit warning, delete-with-estimate confirm title/text, collapse/expand labels) in en + pl.

**Contract**: Matching keys in both resx files; referenced via `L["..."]`. New validation→resx mappings added to `MapInvalidArgument` if any new server messages are introduced.

#### 7. Markdown rendering component

**File**: `src/PlanDeck/Web/PlanDeck.Client/` (new shared component, e.g. `Components/MarkdownView.razor`) + `PlanDeck.Client.csproj`

**Intent**: A single reusable display-only Markdown renderer used by the voting room and the edit-dialog preview, so Markdown handling is defined once and XSS-safe.

**Contract**: Add a `Markdig` package reference to `PlanDeck.Client`. Component takes a `string? Markdown` parameter, converts it with a Markdig pipeline built using `.DisableHtml()` (and a sensible advanced-extensions set), and renders the result as a `MarkupString`. Empty/null input renders nothing.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build PlanDeck.slnx`
- No missing-resource gaps (every new `L["..."]` key exists in both resx) — verified by build + a quick key-parity check
- Full unit suite green: `dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj`

#### Manual Verification:

- `/sessions` uses full width, is responsive (desktop two-column, mobile single-column), looks modern
- Edit dialog updates title + description; description shows in voting room as a collapsible section
- Description renders as **Markdown** (display-only) in the voting room and the edit-dialog preview; raw HTML/script in a description is not executed (XSS-safe)
- Bulk paste adds multiple tasks; `title | description` lines split correctly; empty lines skipped
- Task add/edit/delete work while the session is Active; delete-with-estimate asks for confirmation; ADO edit shows the local-only warning
- Animations are subtle and not distracting; en/pl both render

**Implementation Note**: Pause for manual confirmation after automated verification passes.

---

## Phase 5: E2E & Full Verification

### Overview

Add Playwright coverage for the new flows, including two-client realtime propagation and a responsive smoke check, and run the full suite.

### Changes Required:

#### 1. Page object extensions

**File**: `src/PlanDeck/Tests/PlanDeck.E2e.Tests/Pages/SessionsPage.cs` (+ `VotingRoomPage.cs`)

**Intent**: Add locators/actions for edit dialog, description field, bulk-paste textarea, Active-session task controls, and the voting-room description section.

**Contract**: Page-object methods following the existing pattern (wait-for-visible before asserting; account for WASM boot).

#### 2. E2E scenarios

**File**: `src/PlanDeck/Tests/PlanDeck.E2e.Tests/SessionsTests.cs` (+ a realtime test, possibly under `VotingRoomTests.cs`)

**Intent**: Cover: edit a task's title+description; bulk paste multiple tasks (with a `title | description` line); manage tasks in an Active session; two-client realtime propagation (a task added in `/sessions` appears in another client's open voting room); a responsive smoke check at a mobile viewport.

**Contract**: New `[Test]` methods deriving from `PageTest`, using `AspireAppFixture.BaseUrl`; two-client test opens two browser contexts. Reuse test-auth scheme.

### Success Criteria:

#### Automated Verification:

- Whole solution builds: `dotnet build PlanDeck.slnx`
- Full test suite green (unit + integration + E2E): `dotnet test PlanDeck.slnx` (Podman running; Playwright browsers installed)

#### Manual Verification:

- E2E run is stable across a couple of runs (no flakiness in the realtime/two-client test)

**Implementation Note**: Pause for manual confirmation after automated verification passes.

---

## Testing Strategy

### Unit Tests:

- `SessionGrpcServiceTests`: `UpdateTaskAsync` (title+description, empty-title reject), `AddTasksAsync` (dedup, sort continuation, ADO-duplicate skip), task edits allowed while Active, config edits rejected while Active, notifier invoked only when Active.
- `PlanningRoomServiceTests`: `SyncTasks` add/update/remove, votes preserved on survivors, current-task reset only on deletion, description surfaced in state.

### Integration Tests:

- MSSQL: `Description` persists and round-trips; cascade + tenant isolation unaffected by the new column.
- Realtime hub tests remain green with the extended seed/state shape.

### Manual Testing Steps:

1. Create a session, bulk-paste several `title | description` lines, confirm tasks + descriptions.
2. Edit a task (title + description) via the dialog; open the voting room and confirm the collapsible **Markdown-rendered** description (e.g. headings/bold/lists/links render; a raw `<script>`/HTML in the text does not execute).
3. Activate the session; add/edit/delete tasks; confirm a second client's open voting room updates live and in-flight votes survive.
4. Delete a task that has an agreed estimate; confirm the confirmation dialog.
5. Edit an ADO task; confirm the local-only warning; resize to mobile and confirm single-column layout.

## Performance Considerations

`nvarchar(max)` descriptions are loaded with the session aggregate; sizes are small for planning tasks, so no special handling. `SyncTasks` runs under the existing per-room lock — O(tasks) reconciliation, negligible for session-sized lists.

## Migration Notes

Single additive, nullable column (`Description nvarchar(max)` on `SessionTasks`) — backward compatible; existing rows get `NULL`. Applied on startup in Development; no data backfill.

## References

- Roadmap item: `context/foundation/roadmap.md` (S-02, lines 127–137)
- Change identity: `context/changes/adhoc-tasks/change.md`
- Existing ad-hoc flow: `src/PlanDeck/Core/PlanDeck.Application/Services/SessionGrpcService.cs`, `src/PlanDeck/Web/PlanDeck.Client/Pages/Sessions.razor(.cs)`
- Live room: `src/PlanDeck/Core/PlanDeck.Application/Planning/PlanningRoomService.cs`, `src/PlanDeck/Web/PlanDeck.Server/Hubs/PlanningRoomHub.cs`
- Prior slice (S-04) brief: `context/archive/2026-06-18-create-configure-session/plan-brief.md`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Domain, Contract & Persistence

#### Automated

- [x] 1.1 Solution builds: `dotnet build PlanDeck.slnx`
- [x] 1.2 Migration created and model snapshot updated
- [x] 1.3 Integration test proves Description round-trips and is tenant-isolated

#### Manual

- [x] 1.4 App starts and applies the migration cleanly
- [x] 1.5 New column is nvarchar(max) and nullable in the created schema

### Phase 2: Application Service Logic

#### Automated

- [ ] 2.1 Solution builds: `dotnet build PlanDeck.slnx`
- [ ] 2.2 Unit tests cover update, bulk add, Active-task-edit allowed, config-edit-while-Active rejected, empty-title reject

### Phase 3: Real-time Propagation to the Live Voting Room

#### Automated

- [ ] 3.1 Solution builds: `dotnet build PlanDeck.slnx`
- [ ] 3.2 PlanningRoomServiceTests cover SyncTasks add/update/remove, votes preserved, current-task reset only on deletion
- [ ] 3.3 Realtime integration tests still green

#### Manual

- [ ] 3.4 Two-browser Active session: task add/edit/delete from /sessions updates both rooms live; in-flight votes survive

### Phase 4: Client UI — Redesign & Features

#### Automated

- [ ] 4.1 Solution builds: `dotnet build PlanDeck.slnx`
- [ ] 4.2 Every new L["..."] key exists in both resx (key parity)
- [ ] 4.3 Full unit suite green

#### Manual

- [ ] 4.4 /sessions full-width and responsive (desktop two-column, mobile single-column), modern look
- [ ] 4.5 Edit dialog updates title+description; description shows as collapsible section in voting room
- [ ] 4.6 Description renders as Markdown (display-only) in voting room + edit preview; raw HTML/script not executed (XSS-safe)
- [ ] 4.7 Bulk paste adds multiple tasks; title|description split correct; empty lines skipped
- [ ] 4.8 Task add/edit/delete work while Active; delete-with-estimate confirms; ADO edit shows local-only warning
- [ ] 4.9 Animations subtle; en and pl both render

### Phase 5: E2E & Full Verification

#### Automated

- [ ] 5.1 Whole solution builds: `dotnet build PlanDeck.slnx`
- [ ] 5.2 Full suite green (unit + integration + E2E): `dotnet test PlanDeck.slnx`

#### Manual

- [ ] 5.3 E2E run stable across a couple of runs (no flakiness in two-client realtime test)
