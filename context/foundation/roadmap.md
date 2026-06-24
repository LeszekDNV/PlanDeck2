---
project: "PlanDeck"
version: 1
status: draft
created: 2026-06-18
updated: 2026-06-24
prd_version: 1
main_goal: quality
top_blocker: time
---

# Roadmap: PlanDeck

> Derived from `context/foundation/prd.md` (v1) + auto-researched codebase baseline.
> Edit-in-place; archive when superseded.
> Slices below are listed in dependency order. The "At a glance" table is the index.

## Vision recap

PlanDeck is a SCRUM planning-poker tool whose bet is radical simplicity: a user spins up a tailored estimation session in a few steps, the team votes in real time with hidden votes that reveal together, and the agreed estimate round-trips straight back into the originating Azure DevOps task. The whole product exists to close that loop with low setup friction where incumbents drown users in configuration. The riskiest assumption (the single belief that, if wrong, sinks the MVP) is that the full loop — import → real-time vote → write-back — holds together correctly and consistently end-to-end; the sequencing below is biased toward proving that loop with quality (correctness, isolation, vote integrity) rather than toward fastest possible first demo.

## North star

**S-08: a user can write the agreed estimate back to the originating Azure DevOps task** — closing the import → vote → write-back loop is the validation milestone that proves PlanDeck's core hypothesis; everything else only matters if this round-trip works.

> "North star" here means the smallest end-to-end slice whose successful delivery would prove the core product hypothesis — placed as early as its Prerequisites allow, because the rest of the roadmap is in service of reaching it. S-08 sits at the *end* of a prerequisite chain by necessity (it can only close a loop the earlier slices open), so the practical consequence is that its chain — F-01 → task source → session → voting round → write-back — is the prioritized critical path.

## At a glance

| ID | Change ID | Outcome (user can …) | Prerequisites | PRD refs | Status |
|---|---|---|---|---|---|
| F-01 | multitenant-persistence-baseline | (foundation) persisted, tenant-scoped domain model with verified migrations | — | Access Control Changes, Guardrails (tenant isolation), FR-001 | done |
| F-02 | realtime-vote-integrity | (foundation) authoritative hidden-vote/reveal contract with consistency guarantees | F-01 | Guardrails (vote consistency, hidden-until-reveal), Business Logic Changes | proposed |
| F-03 | deploy-realtime-validation-skeleton | (foundation) pilot ACA + Azure SQL env validating the gRPC-Web/SignalR/SQL stack | — | Constraints & Compatibility; infrastructure.md (ACA + Azure SQL) | ready |
| S-01 | team-and-members | create a team and add members to it | F-01 | FR-001, FR-002 | done |
| S-02 | adhoc-tasks | create ad-hoc tasks manually | F-01 | FR-004 | done |
| S-03 | azure-devops-import | connect Azure DevOps and import selected tasks | F-01 | FR-003 | done |
| S-04 | create-configure-session | create and configure a session from selected tasks | F-01, S-02 or S-03 | FR-005, FR-006 | done |
| S-05 | assign-session-members | assign/invite team members to a session | S-01, S-04 | FR-007 | done |
| S-06 | realtime-voting-round | run a hidden-vote → reveal → manual-pick round in a session | F-02, S-04, S-05 | FR-008, FR-009, US-01 | done |
| S-07 | guest-link-voting | join a session's vote via a share link with only a temporary username | S-04, F-02 | FR-013 | proposed |
| S-08 | ado-estimate-writeback | write the agreed estimate back to the originating Azure DevOps task | S-03, S-06 | FR-010, US-01 | proposed |

## Streams

Navigation aid — groups items that share a Prerequisites chain. Canonical ordering still lives in the dependency graph below; this table is the proposed reading order across parallel tracks.

| Stream | Theme | Chain | Note |
|---|---|---|---|
| A | Data & session spine | `F-01` → `S-01` → `S-04` → `S-05` → `S-06` | Critical path to the north star; quality-first, tenant isolation baked into F-01. |
| B | Task sourcing | `S-02` / `S-03` | Parallel task sources; both join Stream A at `S-04`. |
| C | Real-time integrity | `F-02` → `S-07` | `F-02` also unlocks `S-06` in Stream A; `S-07` is the guest-voting branch. |
| D | Round-trip close | `S-08` | North star; joins `S-03` (Stream B) and `S-06` (Stream A) to close the loop. |
| E | Deploy & stack validation | `F-03` | Light, parallel; de-risks gRPC-Web + SignalR + Azure SQL ahead of production use. |

## Baseline

What's already in place in the codebase as of 2026-06-18 (auto-researched + user-confirmed).
Foundations below assume these are present and do NOT re-scaffold them.

- **Frontend:** partial — Blazor WASM + MudBlazor scaffold; only `Home.razor`/`NotFound.razor` plus client service wrappers (`PlanningRoomClientService`, `AzureDevOpsClientService`). No feature screens; light/dark mode and the 6-language expansion are unbuilt.
- **Backend / API:** partial — code-first gRPC wired (`AzureDevOpsWorkItemGrpcService`, `HelloGrpcService`) and a SignalR `PlanningRoomHub` mapped at `/hubs/planning-room`. Real-time voting exists only as an in-memory spike (`PlanningRoomService`, `ConcurrentDictionary`, no persistence). No Team/Session services.
- **Data:** partial — `PlanDeckDbContext` plus `AddSqlDatabase` and `ApplyMigrationsAsync` are wired, but the `DbContext` is empty (no entities/`DbSet`s, no `Migrations/` folder). The domain model is unbuilt.
- **Auth:** present — MS Entra ID is pre-wired; `UseAuthentication`/`UseAuthorization` are in the pipeline (preserved unchanged). Guest-link voting is not built.
- **Deploy / infra:** partial — `azure.yaml` (azd), `.azuredevops/pipelines/azure-dev.yml`, `global.json`, and `.editorconfig` exist; target is Azure Container Apps + Azure SQL (per `infrastructure.md`). Not yet validated or deployed.
- **Observability:** present — Aspire `ServiceDefaults` (`AddServiceDefaults`/`MapDefaultEndpoints`) provide OpenTelemetry traces/metrics and health endpoints. Application Insights is planned but not wired.

> Note on the Azure DevOps integration: the import (WIQL) and write-back (PATCH with an optimistic `/rev` concurrency test) are already implemented in `AzureDevOpsWorkItemClient` and exposed over gRPC, but they are not yet connected to any persisted session or task. S-03 and S-08 wire this working client into the domain model and UI rather than building it from scratch.

## Foundations

### F-01: Multi-tenant persistence baseline

- **Outcome:** (foundation) the EF Core domain-persistence pattern and the per-user/tenant data-scoping convention are established, with a real migration applied on startup against the configured SQL database.
- **Change ID:** multitenant-persistence-baseline
- **PRD refs:** Access Control Changes (multi-tenant, flat role model), Success Criteria → Guardrails (tenant/data isolation), FR-001 (authenticated Entra identity scopes all data).
- **Unlocks:** every data-backed slice (S-01, S-02, S-03, S-04, S-05, S-06, S-08); directly reduces the "tenant/data isolation" guardrail risk by fixing the scoping convention once, centrally.
- **Prerequisites:** — (baseline reports the `DbContext`/migration wiring already present; this fills the empty model).
- **Parallel with:** F-03
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Sequenced first because every other slice persists data through it, and under the `quality` goal the isolation guarantee must be a central convention, not re-derived per slice. Kept minimal — it establishes the scoping pattern + one verified migration, not the full entity set; each consuming slice still adds and exercises its own entities through real behavior. Risk if skipped: each slice invents its own isolation, and a leak becomes a cross-tenant data exposure.
- **Status:** ready

### F-02: Real-time vote-integrity baseline

- **Outcome:** (foundation) the in-memory planning-room spike is hardened into the authoritative hidden-vote/reveal contract: votes are never lost, duplicated, or reordered; values are not observable by any participant before reveal; and a participant can drop and reconnect without corrupting room state.
- **Change ID:** realtime-vote-integrity
- **PRD refs:** Success Criteria → Guardrails (real-time vote consistency; vote values not observable before reveal), Business Logic Changes (synchronized hidden-vote-then-reveal round).
- **Unlocks:** S-06 (assigned-member voting round) and S-07 (guest voting), which both bind to this contract; reduces the two real-time guardrail risks before any user-facing voting ships.
- **Prerequisites:** F-01 (the contract binds to persisted, tenant-scoped sessions rather than ad-hoc string keys).
- **Parallel with:** S-01, S-02, S-03 (once F-01 lands)
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Promoted to its own foundation because two distinct slices (assigned-member and guest voting) depend on the same integrity guarantees, and the `quality` goal forbids deferring vote-consistency correctness behind user-facing work. Kept minimal — it defines and tests the contract/reconnection semantics; the consuming slices wire it to real sessions and UI. Risk if folded into a single slice: the second voting path re-implements integrity subtly differently.
- **Status:** proposed

### F-03: Deploy & real-time-stack validation skeleton

- **Outcome:** (foundation) a minimal pilot environment on Azure Container Apps + Azure SQL exists, and the exact runtime contract — hosted Blazor WASM load, gRPC-Web unary calls, a SignalR voting round staying connected through reveal, and SQL access via managed identity — is validated.
- **Change ID:** deploy-realtime-validation-skeleton
- **PRD refs:** Constraints & Compatibility (architecture preservation; real-time sessions), `infrastructure.md` (ACA + Azure SQL recommendation and its risk register).
- **Unlocks:** the north star (S-08) being demonstrable in a deployed environment; reduces the `infrastructure.md` high-impact unknowns (gRPC-Web behavior on ACA, WebSocket/SignalR drop during revision changes).
- **Prerequisites:** — (the existing scaffold is already deployable; this validates the stack incrementally).
- **Parallel with:** F-01, and every slice (it does not block local development).
- **Blockers:** —
- **Unknowns:**
  - Does gRPC-Web behave identically through ACA ingress as locally? — Owner: user. Block: no.
  - Do SignalR rooms survive ACA revision/scale changes with `minReplicas=1` + sticky sessions? — Owner: user. Block: no.
- **Risk:** Included as a foundation (not pure ops) because validating the real-time stack early is a `quality`-goal de-risking step the `infrastructure.md` risk register calls out. Kept light per the `time` blocker and "go simple on infra" — single replica, single region, no Azure SignalR backplane. Risk if deferred: a gRPC-Web/SignalR incompatibility surfaces only at production, after the loop is built.
- **Status:** ready

## Slices

### S-01: Team & members

- **Outcome:** a signed-in user can create a team and add members to it.
- **Change ID:** team-and-members
- **PRD refs:** FR-002 (create team, add members), FR-001 (management actions require Entra sign-in).
- **Prerequisites:** F-01
- **Parallel with:** S-02, S-03
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Teams persist membership across sessions and drive both assignment (S-05) and notifications, so they are the membership root the assigned-member voting path needs. Low integration risk; mostly CRUD over the F-01 persistence pattern.
- **Status:** done

### S-02: Ad-hoc tasks

- **Outcome:** a user can create ad-hoc tasks manually within PlanDeck.
- **Change ID:** adhoc-tasks
- **PRD refs:** FR-004 (create ad-hoc tasks).
- **Prerequisites:** F-01
- **Parallel with:** S-01, S-03
- **Blockers:** —
- **Unknowns:** —
- **Risk:** The simplest task source and an explicit de-risk for demos when the Azure DevOps integration is unavailable; it lets the session/voting slices proceed without depending on the external integration. Low risk.
- **Status:** done

### S-03: Azure DevOps import

- **Outcome:** a user can connect to Azure DevOps and import selected tasks into PlanDeck.
- **Change ID:** azure-devops-import
- **PRD refs:** FR-003 (connect ADO, import selected tasks).
- **Prerequisites:** F-01
- **Parallel with:** S-01, S-02
- **Blockers:** —
- **Unknowns:**
  - Which work-item field is the estimate field per tenant/project, and how is it configured by the user vs. defaulted? — Owner: user. Block: no.
- **Risk:** The import client (WIQL + batch fetch) already exists in `Infrastructure`; this slice wires it to persisted tasks and a selection UI rather than building the integration. It is on the north-star critical path because S-08 writes back to an ADO-sourced task. Integration/auth (PAT) handling is the main risk; mitigated by the working baseline client.
- **Status:** done

### S-04: Create & configure session

- **Outcome:** a user can create a planning session from a set of selected tasks and configure it (task selection and voting scale only).
- **Change ID:** create-configure-session
- **PRD refs:** FR-005 (create session from selected tasks), FR-006 (configure: task selection + voting scale).
- **Prerequisites:** F-01, S-02 or S-03 (a task source must exist to select from)
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:**
  - Which voting scales are offered (Fibonacci, T-shirt, custom)? — Owner: user. Block: yes.
- **Risk:** The hinge of the workflow — the session is what voting and write-back attach to. Deliberately minimal configuration per the PRD ("only task selection and voting scale"). Blocked until the voting-scale options are decided, because the scale is also required by the voting round (S-06); the create-only part (FR-005) could proceed if configuration is descoped, but the loop cannot complete without a scale.
- **Status:** done

### S-05: Assign session members

- **Outcome:** a user can assign/invite team members to a session.
- **Change ID:** assign-session-members
- **PRD refs:** FR-007 (assign/invite members to a session).
- **Prerequisites:** S-01, S-04
- **Parallel with:** S-07
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Assignment drives who can join as a known member (and later notifications/history). Distinct from the guest-link path (S-07): assignment is for known team members, the link is for ad-hoc participants. Low risk once teams (S-01) and sessions (S-04) exist.
- **Status:** done

### S-06: Real-time voting round

- **Outcome:** assigned members join a session and vote on each task in real time; participants see who has voted as it happens, values stay hidden until the round is revealed and then appear together, and the user manually selects the agreed estimate, which is persisted.
- **Change ID:** realtime-voting-round
- **PRD refs:** FR-008 (real-time join + vote), FR-009 (live who-voted; hidden-until-reveal; reveal together), US-01 (the planning-session loop).
- **Prerequisites:** F-02, S-04, S-05
- **Parallel with:** S-07 (guest path can be built against the same contract)
- **Blockers:** —
- **Unknowns:**
  - The result is selected manually (no auto-compute in v1) — confirm the UI affordance for picking the agreed value after divergence. — Owner: user. Block: no.
- **Risk:** The heart of the product and the slice the `quality` goal most protects (vote consistency + hidden reveal). Built on the F-02 integrity contract so correctness is not re-derived here. Main risk is the real-time UX under reconnection; mitigated by F-02 owning reconnection semantics.
- **Status:** done

### S-07: Guest-link voting

- **Outcome:** a user without an account can join a session's vote via a share link containing a code, providing only a temporary username, and vote like any participant.
- **Change ID:** guest-link-voting
- **PRD refs:** FR-013 (guest joins via share link + code + temporary username).
- **Prerequisites:** S-04, F-02
- **Parallel with:** S-05, S-06
- **Blockers:** —
- **Unknowns:**
  - How is the per-session code scoped/expired so guests cannot reach other sessions (isolation)? — Owner: user. Block: no.
- **Risk:** Frictionless guest voting is a deliberate differentiator, but it is the path that most stresses tenant isolation. It reuses the F-02 contract; the new surface is the link/code scoping, which must not leak across sessions. Can be built in parallel with the assigned-member path.
- **Status:** proposed

### S-08: Azure DevOps estimate write-back

- **Outcome:** a user can write the agreed estimate back to the originating Azure DevOps task, with success or failure surfaced explicitly and never silently dropped.
- **Change ID:** ado-estimate-writeback
- **PRD refs:** FR-010 (save agreed estimate back to ADO), US-01 (close the loop).
- **Prerequisites:** S-03, S-06
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:** —
- **Risk:** The north star — closing the round-trip proves the MVP. The write-back client (PATCH with an optimistic `/rev` concurrency test) already exists, so the risk is in correctly mapping the persisted agreed estimate to the right work item/field and surfacing failures per the guardrail (never corrupt or silently drop). Sequenced last on the critical path because it can only write back an estimate the voting round (S-06) has produced for an ADO-sourced task (S-03).
- **Status:** proposed

## Backlog Handoff

| Roadmap ID | Change ID | Suggested issue title | Ready for `/10x-plan` | Notes |
|---|---|---|---|---|
| F-01 | multitenant-persistence-baseline | Establish multi-tenant EF Core persistence + scoping convention | yes | Run `/10x-plan multitenant-persistence-baseline` |
| F-02 | realtime-vote-integrity | Harden planning-room into an authoritative hidden-vote/reveal contract | no | Prereq: F-01 |
| F-03 | deploy-realtime-validation-skeleton | Stand up a pilot ACA + Azure SQL env and validate the realtime stack | yes | Run `/10x-plan deploy-realtime-validation-skeleton`; light/parallel |
| S-01 | team-and-members | Create a team and add members | no | Prereq: F-01 |
| S-02 | adhoc-tasks | Create ad-hoc tasks manually | no | Prereq: F-01 |
| S-03 | azure-devops-import | Connect Azure DevOps and import selected tasks | no | Prereq: F-01; wires existing import client |
| S-04 | create-configure-session | Create and configure a planning session | no | Prereq: F-01 + a task source; blocked on voting-scale decision |
| S-05 | assign-session-members | Assign/invite team members to a session | no | Prereq: S-01, S-04 |
| S-06 | realtime-voting-round | Run a hidden-vote/reveal/manual-pick voting round | no | Prereq: F-02, S-04, S-05 |
| S-07 | guest-link-voting | Join a session's vote via a guest share link | no | Prereq: S-04, F-02 |
| S-08 | ado-estimate-writeback | Write the agreed estimate back to Azure DevOps (north star) | no | Prereq: S-03, S-06 |

## Open Roadmap Questions

1. **Voting scale options** — which scales does FR-006 offer (Fibonacci, T-shirt sizes, custom)? Owner: user. Block: S-04 (and transitively the voting round S-06, since a round needs a scale).
2. **Algorithmic / computed estimate suggestions** — v1 ships manual selection only, but the PRD leaves the door open to computed suggestions (mode/median) later. Owner: user. Block: none (explicitly post-MVP).
3. **Cross-cutting UI scope has no FR IDs** — the PRD's "Cross-cutting changes" declare localization expansion (en/pl → six languages, browser-locale default, persisted choice), light/dark display modes, and a clean uncluttered design, but none carry FR numbers, so they cannot be sliced under the "every slice traces to a PRD ID" rule. Promote them to numbered FRs (and a dedicated slice or two), or confirm they are deferred behind the core loop? Owner: user. Block: roadmap-wide (these are in-scope per the PRD but not yet actionable as slices).

## Parked

- **Session-start notifications (email / MS Teams)** — Why parked: FR-011 is nice-to-have and off the MVP critical path (PRD Success Criteria → Secondary); deferred under the `time` budget.
- **Past sessions / results history view** — Why parked: FR-012 is nice-to-have; agreed estimates already round-trip into Azure DevOps, so in-app history is convenience, not core (PRD Secondary). Deferred under the `time` budget.
- **Integration with other task platforms (JIRA, etc.)** — Why parked: PRD Non-Goals — Azure DevOps is the only external task source for v1.
- **Native mobile apps** — Why parked: PRD Non-Goals — web only for the MVP.
- **Formal roles & permissions / admin tiers** — Why parked: PRD Non-Goals — the access model stays flat ("Scrum Master" is whoever creates a session, not a role).
- **Configurable voting workflows beyond hidden-reveal + manual pick** — Why parked: PRD Non-Goals — the single hidden-vote-then-reveal-then-manually-select flow is the only supported workflow.

## Done

(Empty on first generation. `/10x-archive` appends an entry here — and flips that item's `Status` to `done` — when a change whose `Change ID` matches the item is archived. Do NOT pre-populate.)

- **F-01: (foundation) the EF Core domain-persistence pattern and the per-user/tenant data-scoping convention are established, with a real migration applied on startup against the configured SQL database.** — Archived 2026-06-18 → `context/archive/2026-06-18-multitenant-persistence-baseline/`. Lesson: —.
- **S-01: a signed-in user can create a team and add members to it.** — Archived 2026-06-18 → `context/archive/2026-06-18-team-and-members/`. Lesson: —.
- **S-04: a user can create a planning session from a set of selected tasks and configure it (task selection and voting scale only).** — Archived 2026-06-19 → `context/archive/2026-06-18-create-configure-session/`. Lesson: —.
- **S-02: a user can create ad-hoc tasks manually within PlanDeck.** — Archived 2026-06-23 → `context/archive/2026-06-23-adhoc-tasks/`. Lesson: —.
- **S-05: a user can assign/invite team members to a session.** — Archived 2026-06-22 → `context/archive/2026-06-22-assign-session-members/`. Lesson: —.
- **S-06: assigned members join a session and vote on each task in real time; participants see who has voted as it happens, values stay hidden until the round is revealed and then appear together, and the user manually selects the agreed estimate, which is persisted.** — Archived 2026-06-23 → `context/archive/2026-06-22-realtime-voting-round/`. Lesson: —.
- **S-03: a user can connect to Azure DevOps and import selected tasks into PlanDeck.** — Archived 2026-06-24 → `context/archive/2026-06-24-azure-devops-import/`. Lesson: —.
