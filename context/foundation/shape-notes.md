---
project: "PlanDeck"
context_type: brownfield
created: 2026-06-11
updated: 2026-06-11
timeline_budget:
  delivery_weeks: 8
  hard_deadline: null
  after_hours_only: true
product_type: web-app
target_scale:
  users: medium
  qps: low
  data_volume: small
checkpoint:
  current_phase: 8
  phases_completed: [1, 2, 3, 4, 5, 6, 7]
  gray_areas_resolved:
    - topic: "change category"
      decision: "build MVP feature set onto existing scaffolded architecture"
    - topic: "primary persona scope"
      decision: "multi-tenant — any Scrum team across many orgs"
    - topic: "auth model"
      decision: "keep Entra ID as-is; flat role model; guest voting via link+code"
    - topic: "scope vs timeline"
      decision: "commit to ~8-week full MVP rather than scope down"
    - topic: "domain rule"
      decision: "hidden-vote-then-reveal round; user manually picks final estimate"
  frs_drafted: 13
  quality_check_status: accepted
---

<!-- Shape notes for PlanDeck. Brownfield mode. Sections anticipate the 11 brownfield PRD sections. -->

## Current System

- **System purpose**: PlanDeck is a SCRUM planning-poker tool for running estimation sessions (import tasks, configure a session, vote in real-time, save results).
- **Status**: Scaffolded but not yet built — the layered solution and infrastructure are in place; domain logic is still placeholder (`Class1.cs`). The MVP feature set is being built onto this existing architecture.
- **Key architecture**: Clean Architecture / layered. Blazor Web App with hosted WebAssembly — `PlanDeck.Server` (ASP.NET Core) hosts the `PlanDeck.Client` WASM app as one deployed unit. Backend exposed as code-first gRPC over gRPC-Web (`protobuf-net.Grpc`). Orchestrated locally via .NET Aspire (`PlanDeck.AppHost`).
- **Tech stack**: .NET 10, Blazor (InteractiveWebAssembly), MudBlazor 9.x UI, EF Core 10 + SQL database (migrations on startup in Dev), localization (`en`/`pl`), ASP.NET Core auth (MS Entra ID). Tests: NUnit 4, Playwright E2E.
- **Current user base**: None yet (pre-launch). Target is any Scrum team across many organizations (multi-tenant).
- **Core functionality today**: Infrastructure/conventions only — no user-facing features built.

## Vision & Problem Statement

Existing planning-poker tools are overloaded with features and complex to configure. That friction discourages Scrum Masters from setting up proper, well-tailored planning sessions, and discourages team members from engaging with the tools at all. The change being shaped: build PlanDeck's MVP feature set so a Scrum Master can create and run a tailored planning session in a few simple steps — with Azure DevOps task import and estimate write-back — onto the existing scaffolded architecture.

Insight: incumbents win on breadth and lose on setup friction. PlanDeck's bet is radical simplicity — a session is created in a few steps, and estimates round-trip back into Azure DevOps tasks — so the tool stays out of the team's way during planning.

## User & Persona

- **Primary persona**: Scrum Master at a software team — responsible for running planning sessions. Reaches for PlanDeck when preparing/running a sprint planning or backlog refinement session and wants to estimate a set of tasks quickly without wrestling with configuration.
- **Secondary persona**: Team member (developer/QA/etc.) — joins a session they've been assigned to and votes on tasks in real-time.
- **Scope**: Multi-tenant — any Scrum team across many organizations, authenticated via MS Entra ID.

## Forward: tech-stack
<!-- Stack is already fixed by the existing scaffold (not a PRD concern); recorded here for downstream chain steps. -->
- Stack is pre-committed by the existing solution: .NET 10 / Blazor hosted WASM / MudBlazor / code-first gRPC / EF Core 10 / Aspire / Entra ID. Tech-stack selection is effectively locked — do not re-open.

## Access Control

- **Authentication**: MS Entra ID (pre-wired in the scaffold). No changes planned — current model preserved.
- **Role model**: Flat — every authenticated user can create teams, create/configure sessions, and vote. No Scrum Master vs team member privilege distinction at the MVP. "Scrum Master" is simply whoever creates a session, not a separate role.
- **Tenancy**: Multi-tenant — users from any organization can sign in via Entra ID and use the product.
- **Unauthenticated access**: Gated for management (creating teams/sessions, Azure DevOps, history requires Entra ID sign-in). **Exception — guest voting**: a user without an account can join a session's vote via a share link containing a code, providing only a temporary username. Guests can vote but cannot create or manage sessions.

## Success Criteria

### Primary
- A Scrum Master can, in a few steps, import tasks from Azure DevOps (or add ad-hoc tasks), create a planning session, have assigned team members join and vote on each task in real-time, and have the agreed estimate written back to the corresponding Azure DevOps task. The full loop working end-to-end = the MVP worked.

### Secondary
- Email/Teams notification to a user when a session they're assigned to starts.
- A session history / past-results view so teams can revisit prior estimates.

### Guardrails
- Azure DevOps write-back must never corrupt or overwrite the wrong task or field.
- Real-time votes stay consistent across all participants — no lost, duplicated, or out-of-order votes.
- Tenant isolation — a user only ever sees the teams and sessions they belong to.
- (Preserved) The existing layered architecture and gRPC/Aspire/ServiceDefaults conventions must continue to hold.

## Timeline acknowledgment
Acknowledged on 2026-06-11: 8-week MVP requires sustained dedication; user accepted the longer-timeline cost over scoping down. Driven by the full Azure DevOps round-trip + real-time voting + notifications scope.

## Functional Requirements

### Team & access
- FR-001: A user can authenticate via MS Entra ID. Priority: must-have. Change: preserved
  > Socrates: Counter-argument considered: "Entra ID excludes non-Microsoft tenants." Resolution: kept — auth is pre-wired; guest-link voting (FR-013) covers non-tenant participants.
- FR-002: A user can create a team and add members to it. Priority: must-have. Change: new
  > Socrates: Counter-argument considered: "sessions could exist standalone without teams." Resolution: kept — teams persist membership across sessions and drive assignment/notifications.

### Task sourcing
- FR-003: A user can connect to Azure DevOps and import selected tasks into PlanDeck. Priority: must-have. Change: new
  > Socrates: Counter-argument considered: "ADO is the riskiest/slowest piece; should it gate the MVP?" Resolution: kept — ADO round-trip is a stated core success criterion, the user accepted the longer timeline for it.
- FR-004: A user can create ad-hoc tasks manually. Priority: must-have. Change: new
  > Socrates: Counter-argument considered: "redundant if ADO import works." Resolution: kept — ad-hoc covers tasks not in ADO and de-risks demos when ADO is unavailable.

### Session lifecycle
- FR-005: A user can create a planning session from a set of selected tasks. Priority: must-have. Change: new
  > Socrates: Counter-argument considered: "core capability, hard to challenge." Resolution: stands as written.
- FR-006: A user can configure a session (e.g. which tasks, voting scale). Priority: must-have. Change: new
  > Socrates: Counter-argument considered: "configurability is the complexity incumbents drown in." Resolution: kept but deliberately minimal — only task selection and voting scale, nothing more.
- FR-007: A user can assign/invite team members to a session. Priority: must-have. Change: new
  > Socrates: Counter-argument considered: "overlaps with guest-link path (FR-013)." Resolution: kept — assignment drives notifications and history for known members; guest link is for ad-hoc participants.

### Voting
- FR-008: Assigned members can join a session and vote on each task in real-time. Priority: must-have. Change: new
  > Socrates: "Real-time is the product's reason to exist." Resolution: kept — core.
- FR-009: Participants see in real-time who has cast their vote; vote values stay hidden until the round is revealed, then all values appear together. Priority: must-have. Change: new
  > Socrates: Counter-argument considered: "true real-time adds infra cost; reveal-on-demand might suffice for v1." Resolution: kept as-is — live "who has voted" status plus synchronized reveal is central to the planning experience.
- FR-010: A user can save the agreed estimate back to the originating Azure DevOps task. Priority: must-have. Change: new
  > Socrates: Counter-argument considered: "write-back is the second ADO risk; manual copy could de-risk v1." Resolution: kept — round-trip to ADO is a core success criterion.
- FR-013: A user without an account can join a session's vote via a share link containing a code, providing only a temporary username. Priority: must-have. Change: new
  > Socrates: Counter-argument considered: "anonymous guests weaken tenant isolation and could pollute results." Resolution: kept — frictionless guest voting is a deliberate differentiator; isolation handled via per-session codes.

### Notifications & history (secondary)
- FR-011: A user is notified (email/Teams) when a session they're assigned to starts. Priority: nice-to-have. Change: new
  > Socrates: Counter-argument considered: "adds an external integration." Resolution: already nice-to-have, out of MVP critical path.
- FR-012: A user can view past sessions and their results. Priority: nice-to-have. Change: new
  > Socrates: Counter-argument considered: "results are written back to ADO anyway." Resolution: already nice-to-have; in-app history is convenience, not core.

## User Stories

### US-01: Run a planning session end-to-end

- **Given** a signed-in user who belongs to a team with at least one task imported from Azure DevOps or added ad-hoc
- **When** they create a session from those tasks, share/assign it to members, and start voting
- **Then** all participants (including link-based guests) vote on each task in real-time, votes update live for everyone, and the user can save the agreed estimate back to the Azure DevOps task

#### Acceptance Criteria
- Vote updates are visible to all participants within a second or two of being cast
- A guest who opens the share link can vote after entering only a temporary username — no account required
- Saving the estimate writes to the correct Azure DevOps task/field and surfaces success/failure to the user
- A user only sees sessions and teams they belong to

## Business Logic

PlanDeck runs a synchronized hidden-vote-then-reveal round for each task in a session, then lets a user lock in a chosen estimate and write it back to Azure DevOps.

For each task being estimated, participants (signed-in members and link-based guests) submit a vote privately. The app tracks who has voted in real-time but keeps every vote value hidden until the round is revealed; on reveal, all values are shown together so no participant is anchored by others' choices. The app does not compute the result automatically — once votes are revealed, a user manually selects the final agreed estimate (typically after discussion when votes diverge). That chosen estimate is what gets persisted and, for Azure DevOps tasks, written back to the originating work item.

The inputs are the per-participant votes and the user's final selection; the output is a single agreed estimate per task. The user encounters the rule as the reveal-and-decide moment of each voting round.

## Non-Functional Requirements

- A participant's cast/joined action and the reveal are reflected for all participants within ~1–2 seconds (real-time perception).
- Vote values are not observable by any participant — including via the client — before the round is revealed.
- A guest joining via a share link can reach the voting screen with only a temporary username, no account creation.
- A user only sees teams, sessions, and results belonging to them or shared with them (tenant/data isolation).
- Writing an estimate back to Azure DevOps is acknowledged with explicit success or failure; a failed write never silently drops the result.
- The UI is fully localized into six languages: English (default), Polish, German, Chinese, Norwegian, and Spanish. Initial language is chosen from the user's browser locale, falling back to English; the user's explicit language choice persists across visits (browser local storage).
- The interface presents a clean, visually consistent, uncluttered design (reinforcing the "simple, not overloaded" value proposition).
- The interface supports both light and dark display modes, and respects the user's choice.

## Constraints & Preserved Behavior

- **Architecture preservation**: the existing layered Clean Architecture, code-first gRPC-over-gRPC-Web contract pattern (`protobuf-net.Grpc`, contracts in `Core.Shared`), .NET Aspire orchestration, and `ServiceDefaults` wiring must be followed — features are built into this structure, not around it.
- **External integration — Azure DevOps**: import and write-back must respect Azure DevOps work-item APIs and never corrupt or overwrite the wrong task or field. Write-back targets the originating work item's estimate field.
- **External integration — notifications**: email / MS Teams delivery for session-start notifications (nice-to-have).
- **Auth**: MS Entra ID is the authentication mechanism and is preserved; guest voting is an additive, link-scoped exception, not a change to the Entra ID model.
- **Localization**: the current scaffold ships `en`/`pl`; the MVP expands this to six languages (en default, pl, de, zh, no, es) with browser-locale detection and a persisted user override.
- **Data**: no existing production data to migrate (pre-launch); EF Core migrations are applied on startup in Development.

## Non-Goals

- **Integration with other task platforms (JIRA, etc.)** — Azure DevOps is the only external task source for v1; other integrations are out of scope.
- **Native mobile apps** — web only for the MVP; no iOS/Android clients.
- **Formal roles & permissions / admin tiers** — the access model stays flat; no role hierarchy, admin consoles, or per-permission matrices.
- **Configurable voting workflows beyond hidden-reveal + manual pick** — the single hidden-vote-then-reveal-then-manually-select flow is the only supported workflow; no alternative voting modes.

(Note: automatic/algorithmic estimate computation is *not* listed as a non-goal — the user left the door open to add it later, though v1 ships manual selection.)

## Product framing
- product_type: web-app (no change — existing Blazor web app)
- target_scale.users: medium (dozens to ~100)
- timeline_budget: delivery_weeks 8, no hard deadline, after-hours only

## Quality cross-check

Ran 2026-06-11 (brownfield, 7 checks). All elements present, no gaps:
- Access Control: present
- Business Logic (one-sentence rule): present
- Project artifacts: present
- Timeline-cost acknowledged: present
- Non-Goals: present
- Preserved behavior: present

Status: accepted.
