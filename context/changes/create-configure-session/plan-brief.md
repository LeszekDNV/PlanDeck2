# Create & Configure Planning Session (S-04) — Plan Brief

> Full plan: `context/changes/create-configure-session/plan.md`

## What & Why

Deliver FR-005 + FR-006 — a signed-in user can create a planning session from a set of selected tasks and configure it (task selection + voting scale only, deliberately minimal). The session is the hinge of the product loop: real-time voting (S-06) and Azure DevOps estimate write-back (S-08) both attach to it. This is the next data-backed vertical slice on the F-01 persistence baseline.

## Starting Point

The Team slice (S-01) is a near-exact template: entity `: TenantEntity` → EF config → `DbSet` → migration → repo abstraction (Application) → repo impl (Infrastructure) → code-first gRPC contract (`Core.Shared`) → `*GrpcService` (Application) → server DI + `MapGrpcService` → client service → MudBlazor page + en/pl resx → integration + E2E tests. Tenant isolation is automatic via `TenantEntity`. **But there is no persisted task source** — S-02 (ad-hoc) and S-03 (ADO import) aren't done; ADO import today is a **live WIQL query returning transient DTOs**, nothing stored. There is no `Session`/`SessionTask` entity or `/sessions` page.

## Desired End State

A signed-in user opens `/sessions`, creates a session (name, optional team, tasks added ad-hoc and/or imported live from Azure DevOps, and a voting scale), and the session + its task snapshot persist tenant-isolated. While **Draft**, the user can rename, change scale, and add/remove tasks; **Activate** locks configuration. All strings in English and Polish. Proven by an MSSQL integration test (persistence, cascade, tenant isolation) and a Playwright E2E (create with ad-hoc tasks + scale, see it listed).

## Key Decisions Made

| Decision | Choice | Why | Source |
| --- | --- | --- | --- |
| Task-source gap | Session owns tasks as snapshot child entities (`SessionTask`) | No persisted task source exists; keeps S-04 self-contained without blocking on S-02/S-03 | Plan |
| Voting scales | Fibonacci + T-shirt presets + Custom (user values) | Resolves the FR-006 blocker; covers the common scales plus flexibility | Plan |
| Team linkage | `TeamId` nullable on session | S-05 needs it later; not mandatory now | Plan |
| Lifecycle | Minimal `SessionStatus` (Draft → Active) | Gates editability; full lifecycle deferred to S-06 | Plan |
| Configuration edit | Allowed while Draft (name, scale, tasks); locked once Active | Satisfies FR-006 "configure" without over-building | Plan |
| Task sources in UI | Both ad-hoc (typed) + live ADO import (existing WIQL) | Uses the working ADO call; ad-hoc de-risks demos | Plan |
| Scale storage | `VotingScaleType` enum + resolved `List<string> ScaleValues` | Voting round (S-06) reads one uniform list regardless of type | Plan |
| Tests | Integration (MSSQL) + E2E Playwright (like Team) | Cover isolation/cascade + the full UI loop | Plan |
| ADO snapshot fields | `SessionTask` carries `AdoWorkItemId` + `AdoRevision` now | Needed for S-08 write-back; cheap to store early | Plan |

> All rows are `Plan` — no frame/research doc preceded this; decisions came from the planning questions.

## Scope

**In scope:** `PlanningSession` + `SessionTask` entities/enums/migration; `ISessionService` gRPC contract + `SessionGrpcService` (validation, preset resolution, Draft-only enforcement); `ISessionRepository`/`SessionRepository`; client service + `/sessions` page (create dialog + Draft config panel, ad-hoc + ADO import, scale picker); nav entry; en/pl strings; unit + integration + E2E tests.

**Out of scope:** standalone persisted task source (S-02/S-03); voting / hidden-vote / reveal / agreed-estimate (S-06); member assignment (S-05); guest links (S-07); ADO write-back (S-08); notifications/history; any auth/localization-infra/realtime change.

## Architecture / Approach

`PlanningSession : TenantEntity` owns `SessionTask : TenantEntity` children via cascade delete (mirroring `Team`/`TeamMember`). Voting scale = `VotingScaleType` enum + persisted ordered `List<string> ScaleValues` (presets resolved server-side; custom validated). `SessionStatus` (Draft/Active) gates edits, enforced in the Application service (not just the UI). Data access behind `ISessionRepository` over `PlanDeckDbContext` + `ICurrentUserContext`; tenant scoping automatic. Client gets `ISessionClientService` + a single MudBlazor `/sessions` page modeled on `Teams.razor`; ADO tasks come from the existing `IAzureDevOpsClientService` live import, snapshotted at add-time.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Domain & persistence | Session+task entities, enums, EF config, migration, MSSQL integration test | `ScaleValues` primitive-collection mapping; cascade FK shape |
| 2. Backend (contract/service/repo) | gRPC contract, service (validation/preset/Draft-guard), repo, wiring, unit tests | Keeping Application off Infrastructure; server-side Draft enforcement |
| 3. Client UI & localization | `/sessions` page (create + Draft config, ad-hoc + ADO), nav, en/pl strings | Composing the two task sources in one dialog; full resx coverage |
| 4. E2E test | Playwright create-session flow + page object | WASM boot timing; test-auth + Aspire/Podman |

**Prerequisites:** F-01 + S-01 done (they are); Podman running for integration/E2E; Playwright browsers installed; Entra (or test-auth scheme) for sign-in; ADO configured only for the manual import check.
**Estimated effort:** ~3–4 sessions across 4 phases (auth + localization foundations already exist, so lighter than the Team slice).

## Open Risks & Assumptions

- Snapshot-not-link for ADO tasks is the right model: the session stores `AdoWorkItemId` + `AdoRevision` at add-time; S-08 writes back against the stored revision. If product later wants live re-sync, that's a separate slice.
- Draft-only editing must be enforced server-side; UI gating alone would be a correctness hole.
- Preset scale values are canonicalized at the service boundary so downstream voting reads one resolved list — presets are not re-derived per consumer.
- `TeamId` is stored but no assignment/membership logic exists yet (S-05).

## Success Criteria (Summary)

- A signed-in user creates and configures (Draft) a session from ad-hoc and/or imported ADO tasks with a chosen voting scale, in English or Polish.
- Sessions and their tasks are strictly tenant-isolated and cascade on delete (integration test proves it on real MSSQL).
- A Playwright E2E creates a session with ad-hoc tasks + a scale end-to-end; full suite green.
