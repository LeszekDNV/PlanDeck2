# Ad-hoc Tasks (S-02) — Plan Brief

> Full plan: `context/changes/adhoc-tasks/plan.md`

## What & Why

S-02's headline capability — "create ad-hoc tasks manually" (FR-004) — is **already delivered** inside the session flow (built in S-04). This change therefore extends ad-hoc tasks into a fuller manual authoring experience the user explicitly asked for: edit existing tasks (title + a new description), bulk-add by pasting many lines, manage tasks **after** a session is activated (with the live voting room kept in sync), and a modern, full-width, responsive redesign of `/sessions` with subtle animations.

## Starting Point

Tasks are session-owned snapshot children (`SessionTask : TenantEntity`) with no description; ad-hoc create/add/remove already works through `SessionGrpcService` and the `/sessions` MudBlazor page. Editing is **Draft-only** (enforced server-side in `LoadDraftAsync`, mirrored by `_isLocked` in the UI). The live voting room (`PlanningRoomService`) seeds **once** from the DB and has no path to mutate tasks afterwards. The page is a single narrow (`MaxWidth.Medium`) vertical layout.

## Desired End State

For any session (Draft or Active) a user can add tasks one-by-one or by pasting many lines (`title | description`), edit a task's title + optional description in a dialog, and delete tasks (confirming when an agreed estimate exists). Descriptions are `nvarchar(max)`; ADO-sourced tasks are editable locally with a "not written back to ADO" warning. When a session is Active, task changes propagate to all connected voting-room participants in real time, preserving in-flight votes; the voting room shows the current task's title plus a collapsible description. `/sessions` is a responsive full-width master-detail with subtle MudBlazor animations, fully localized en/pl.

## Key Decisions Made

| Decision | Choice | Why | Source |
| --- | --- | --- | --- |
| S-02 scope | Extend ad-hoc (edit/desc/bulk/active/UI), not a new task source | FR-004 already shipped in S-04; build what's actually missing | Plan |
| Description column | `nvarchar(max)`, nullable | User requested unbounded description | Plan |
| Active-session edits | Allow task add/edit/delete while Active; config stays Draft-only | User wants editing post-activation without breaking config lock | Plan |
| Live-room sync | New `IPlanningRoomNotifier` + `PlanningRoomService.SyncTasks` reconcile + broadcast | Keep real-time consistent; preserve votes/current task | Plan |
| Edit/delete rules | Edit always; delete confirms when AgreedEstimate set; warn on current task | Protect agreed results / in-flight rounds | Plan |
| Edit UX | MudDialog (title + description) | Matches existing dialog pattern; room for longer text | Plan |
| Bulk paste | Line = task, optional `title | description`; dedup/trim/skip empty | User-requested power with controlled parsing (client-side) | Plan |
| Bulk locations | Create dialog + config panel (Draft + Active) | Consistent single component, used twice | Plan |
| Description in room | Propagated; title highlighted + collapsible description above voting panel | User-specified voting-room presentation | Plan |
| Description format | Markdown, rendered display-only (Markdig `.DisableHtml()`); plain-text input | User asked descriptions to support Markdown on display; HTML-stripping keeps it XSS-safe | Plan |
| ADO edit | Editable with local-only warning; source/#ADO chip kept | Convenience without implying S-08 write-back | Plan |
| Layout | Full-width responsive master-detail (MudGrid) | Modern, uses full width, mobile single-column | Plan |
| Animations | Subtle built-in MudBlazor transitions on meaningful changes | Polish without heavy libs | Plan |
| Testing | Full: unit + integration + E2E incl. two-client realtime + responsive smoke | User chose full coverage | Plan |

## Scope

**In scope:** `Description` on `SessionTask` (entity/DTO/EF/migration); `UpdateTaskAsync` + `AddTasksAsync` gRPC ops; relaxing Draft-only for task ops; `IPlanningRoomNotifier` + `SyncTasks` reconcile + Server SignalR broadcast; `Description` in realtime state + voting-room UI; Markdown rendering of the description (display-only, Markdig, XSS-safe); `/sessions` redesign (master-detail, edit dialog, bulk paste, active management, animations); en/pl strings; unit + integration + E2E tests.

**Out of scope:** standalone task backlog/`/tasks` page; ADO write-back (S-08); task reordering/drag-drop; voting mechanics / scales / membership / guest links; new UI libraries (beyond Markdig for rendering); bulk *edit*; rich/WYSIWYG markdown editor (input stays plain text).

## Architecture / Approach

Inward-out: data + wire contract → Application service rules → real-time propagation → client UI → E2E. The cross-layer crux is real-time propagation: task mutations live in Application (`SessionGrpcService`), the live room is SignalR in the Server. Application stays off SignalR by depending on a new `IPlanningRoomNotifier` abstraction; the Server implements it (`SignalRPlanningRoomNotifier`) by reconciling the in-memory room via `PlanningRoomService.SyncTasks` (preserves votes/`IsRevealed`/`AgreedEstimate`, resets `CurrentTaskId` only if the active task is deleted) and broadcasting `RoomStateChanged` to the room group via `IHubContext`. The service calls the notifier only when the session is Active. Bulk parsing is client-side into `List<NewSessionTaskDto>`.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Domain, contract & persistence | `Description`, update/bulk wire ops, EF + migration | nvarchar(max) mapping; contract DataMember ordering |
| 2. Application service logic | Update/bulk + Active-task-edit (config stays Draft-only); notifier hook | Splitting task-edit vs config-edit invariant cleanly |
| 3. Real-time propagation | `SyncTasks` reconcile + Server notifier + description in room state | Preserving votes/current task; layering (no SignalR in Application) |
| 4. Client UI redesign & features | Master-detail, edit dialog, bulk paste, active mgmt, animations, en/pl, voting-room description | Responsive layout; resx parity; not over-animating |
| 5. E2E & full verification | Playwright incl. two-client realtime + responsive smoke | Realtime/two-client flakiness; WASM boot timing |

**Prerequisites:** F-01, S-04, S-06 done (they are); Podman running for integration/E2E; Playwright browsers installed; test-auth scheme.
**Estimated effort:** ~4–6 sessions across 5 phases (UI redesign + realtime propagation are the heavy parts).

## Open Risks & Assumptions

- Real-time reconciliation must preserve in-flight votes and the current task; getting `SyncTasks` semantics wrong could disrupt a live round.
- `IPlanningRoomNotifier` must keep Application free of SignalR hosting types (layering rule); the broadcast lives in the Server impl.
- ADO-sourced edits are local only; this does not change S-08 write-back semantics.
- Bulk `title | description` parsing is client-side; the server validates titles and continues `SortOrder`.

## Success Criteria (Summary)

- A user can edit task title+description, bulk-paste tasks, and manage tasks in Active sessions; descriptions persist (`nvarchar(max)`) and surface (collapsible) in the voting room.
- Task changes in an Active session propagate live to all voting-room participants without losing in-flight votes.
- `/sessions` is a modern, responsive, full-width master-detail in en/pl; full test suite (unit + integration + E2E incl. two-client realtime) is green.
