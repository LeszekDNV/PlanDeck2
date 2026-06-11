---
project: "PlanDeck"
version: 1
status: draft
created: 2026-06-11
context_type: brownfield
product_type: web-app
target_scale:
  users: medium
  qps: low
  data_volume: small
timeline_budget:
  delivery_weeks: 8
  hard_deadline: null
  after_hours_only: true
---

## Current System Overview

- **System purpose**: PlanDeck is a SCRUM planning-poker tool for running estimation sessions — import tasks, configure a session, vote in real-time, and save results.
- **Status**: Scaffolded but not yet built. The layered solution and infrastructure are in place; domain logic is still placeholder. The MVP feature set is being built onto this existing architecture.
- **Key architecture**: Clean Architecture / layered. Blazor Web App with hosted WebAssembly — `PlanDeck.Server` (ASP.NET Core) hosts the `PlanDeck.Client` WASM app as a single deployed unit. Backend exposed as code-first gRPC over gRPC-Web (`protobuf-net.Grpc`). Orchestrated locally via .NET Aspire (`PlanDeck.AppHost`).
- **Tech stack**: .NET 10; Blazor (InteractiveWebAssembly); MudBlazor 9.x UI; EF Core 10 with a SQL database (migrations applied on startup in Development); localization (`en`/`pl`); ASP.NET Core authentication via MS Entra ID. Tests: NUnit 4, Playwright E2E.
- **Current user base**: None yet (pre-launch). Target is any Scrum team across many organizations (multi-tenant).
- **Core functionality today**: Infrastructure and conventions only — no user-facing features built.

## Problem Statement & Motivation

Existing planning-poker tools are overloaded with features and complex to configure. That friction discourages Scrum Masters from setting up proper, well-tailored planning sessions, and discourages team members from engaging with the tools at all. The gap: there is no fast, low-configuration way for a Scrum Master to spin up a tailored estimation session and have the results flow straight back into the team's task tracker.

Why now: PlanDeck's architecture is scaffolded and ready; the MVP feature set is what's missing. The motivating insight is that incumbents win on breadth and lose on setup friction — so PlanDeck's bet is radical simplicity. A session is created in a few steps, and agreed estimates round-trip back into Azure DevOps tasks, so the tool stays out of the team's way during planning. The current workaround (heavyweight incumbents or manual spreadsheets) costs setup time and engagement.

## User & Persona

- **Primary persona**: Scrum Master at a software team, responsible for running planning sessions. Reaches for PlanDeck when preparing or running a sprint planning or backlog-refinement session and wants to estimate a set of tasks quickly without wrestling with configuration. This is the user whose experience the change most directly creates.
- **Secondary persona**: Team member (developer, QA, etc.) who joins a session they have been assigned to — or follows a share link — and votes on tasks in real-time.
- **New users enabled by this change**: link-based guests with no account, who can participate in a vote with only a temporary username.
- **Scope**: Multi-tenant — any Scrum team across many organizations, authenticated via MS Entra ID.

## Success Criteria

### Primary
- A user can, in a few steps, import tasks from Azure DevOps (or add ad-hoc tasks), create a planning session, have assigned members and link-based guests join and vote on each task in real-time, and write the agreed estimate back to the corresponding Azure DevOps task. The full loop working end-to-end means the MVP worked.

### Secondary
- A user is notified (email/MS Teams) when a session they're assigned to starts.
- A session history / past-results view lets teams revisit prior estimates.

### Guardrails
- Writing an estimate back to Azure DevOps never corrupts or overwrites the wrong task or field; a failed write is surfaced explicitly and never silently drops the result.
- Real-time votes stay consistent across all participants — no lost, duplicated, or out-of-order votes.
- Vote values are not observable by any participant before the round is revealed.
- Tenant/data isolation: a user only ever sees the teams, sessions, and results they belong to or that are shared with them.
- (Preserved) The existing layered architecture and its established conventions continue to hold; the change builds into the structure, not around it.

## User Stories

### US-01: Run a planning session end-to-end

- **Given** a signed-in user who belongs to a team with at least one task imported from Azure DevOps or added ad-hoc
- **When** they create a session from those tasks, share or assign it to members, and start voting
- **Then** all participants (including link-based guests) vote on each task, participants see who has voted in real-time, the round reveals all values together, and the user can save the agreed estimate back to the Azure DevOps task

#### Acceptance Criteria
- A participant's cast/joined action and the reveal are reflected for all participants within roughly one to two seconds.
- Vote values stay hidden from every participant until the round is revealed, then appear together.
- A guest who opens the share link can vote after entering only a temporary username — no account required.
- Saving the estimate writes to the correct Azure DevOps task and field, and surfaces success or failure to the user.
- A user only sees sessions and teams they belong to.

> Note (delta): before this change, none of this behavior exists — the session, voting, and write-back flows are all new on top of the scaffold.

## Scope of Change

### Team & access
- [preserved] A user can authenticate via MS Entra ID. (FR-001 — existing auth, preserved unchanged.)
  > Socrates: Counter-argument considered: "Entra ID excludes non-Microsoft tenants." Resolution: kept — auth is pre-wired; guest-link voting covers non-tenant participants.
- [new] A user can create a team and add members to it. (FR-002)
  > Socrates: Counter-argument considered: "sessions could exist standalone without teams." Resolution: kept — teams persist membership across sessions and drive assignment/notifications.

### Task sourcing
- [new] A user can connect to Azure DevOps and import selected tasks into PlanDeck. (FR-003)
  > Socrates: Counter-argument considered: "ADO is the riskiest/slowest piece; should it gate the MVP?" Resolution: kept — the round-trip is a stated core success criterion; the user accepted the longer timeline for it.
- [new] A user can create ad-hoc tasks manually. (FR-004)
  > Socrates: Counter-argument considered: "redundant if import works." Resolution: kept — ad-hoc covers tasks not in the tracker and de-risks demos when the integration is unavailable.

### Session lifecycle
- [new] A user can create a planning session from a set of selected tasks. (FR-005)
- [new] A user can configure a session — task selection and voting scale only, deliberately minimal. (FR-006)
  > Socrates: Counter-argument considered: "configurability is the complexity incumbents drown in." Resolution: kept but minimal — only task selection and voting scale.
- [new] A user can assign/invite team members to a session. (FR-007)
  > Socrates: Counter-argument considered: "overlaps with the guest-link path." Resolution: kept — assignment drives notifications and history for known members; the guest link is for ad-hoc participants.

### Voting
- [new] Assigned members can join a session and vote on each task in real-time. (FR-008)
- [new] Participants see in real-time who has cast their vote; values stay hidden until the round is revealed, then appear together. (FR-009)
  > Socrates: Counter-argument considered: "true real-time adds cost; reveal-on-demand might suffice." Resolution: kept — live "who has voted" status plus synchronized reveal is central to the experience.
- [new] A user can save the agreed estimate back to the originating Azure DevOps task. (FR-010)
  > Socrates: Counter-argument considered: "write-back is the second integration risk; a manual copy could de-risk v1." Resolution: kept — the round-trip is a core success criterion.
- [new] A user without an account can join a session's vote via a share link containing a code, providing only a temporary username. (FR-013)
  > Socrates: Counter-argument considered: "anonymous guests weaken isolation and could pollute results." Resolution: kept — frictionless guest voting is a deliberate differentiator; isolation handled via per-session codes.

### Notifications & history (secondary, nice-to-have)
- [new] A user is notified (email/MS Teams) when a session they're assigned to starts. (FR-011, nice-to-have — out of the MVP critical path.)
- [new] A user can view past sessions and their results. (FR-012, nice-to-have — convenience, not core.)

### Cross-cutting changes
- [modified] Localization expands from the current two languages (`en`/`pl`) to six: English (default), Polish, German, Chinese, Norwegian, and Spanish. Initial language is chosen from the user's browser locale, falling back to English; the user's explicit choice persists across visits.
- [new] The interface supports both light and dark display modes and respects the user's choice.
- [new] The interface presents a clean, visually consistent, uncluttered design, reinforcing the "simple, not overloaded" value proposition.

## Constraints & Compatibility

- **Architecture preservation**: the existing layered architecture and its established contract, orchestration, and shared-defaults conventions must be followed — features are built into this structure, not around it.
- **Existing integration — Azure DevOps**: task import and estimate write-back must respect Azure DevOps work-item behavior and never corrupt or overwrite the wrong task or field. Write-back targets the originating work item's estimate field.
- **Existing integration — notifications**: email / MS Teams delivery is used for session-start notifications (nice-to-have).
- **Authentication compatibility**: MS Entra ID is preserved as the authentication mechanism. Guest voting is an additive, link-scoped exception, not a change to the Entra ID model.
- **Localization compatibility**: the current `en`/`pl` resources are extended, not replaced — existing localized strings remain valid.
- **Data migration**: no existing production data to migrate (pre-launch). Schema evolution is handled by the existing migration-on-startup flow in Development.

## Business Logic Changes

This change introduces a new domain rule (the system currently has none built).

**New rule (one sentence):** PlanDeck runs a synchronized hidden-vote-then-reveal round for each task in a session, then lets a user lock in a chosen estimate and write it back to Azure DevOps.

For each task being estimated, participants (signed-in members and link-based guests) submit a vote privately. The app tracks who has voted in real-time but keeps every vote value hidden until the round is revealed; on reveal, all values are shown together so no participant is anchored by others' choices. The app does not compute the result automatically — once votes are revealed, a user manually selects the final agreed estimate (typically after discussion when votes diverge). That chosen estimate is persisted and, for Azure DevOps tasks, written back to the originating work item.

The inputs are the per-participant votes and the user's final selection; the output is a single agreed estimate per task. The user encounters the rule as the reveal-and-decide moment of each voting round.

## Access Control Changes

The authentication model is preserved (MS Entra ID, unchanged). The change adds the access-control behavior the new features require:

- **Role model**: flat. Every authenticated user can create teams, create and configure sessions, and vote. There is no Scrum Master vs team-member privilege distinction — "Scrum Master" is simply whoever creates a session, not a separate role.
- **Tenancy**: multi-tenant — users from any organization can sign in and use the product.
- **Management actions** (creating teams/sessions, connecting Azure DevOps, viewing history) require Entra ID sign-in.
- **New — guest voting**: a user without an account can join a session's vote via a share link containing a code, providing only a temporary username. Guests can vote but cannot create or manage sessions.

## Non-Goals

- **Integration with other task platforms (JIRA, etc.)** — Azure DevOps is the only external task source for v1; other integrations are out of scope.
- **Native mobile apps** — web only for the MVP; no iOS/Android clients.
- **Formal roles & permissions / admin tiers** — the access model stays flat; no role hierarchy, admin consoles, or per-permission matrices.
- **Configurable voting workflows beyond hidden-reveal + manual pick** — the single hidden-vote-then-reveal-then-manually-select flow is the only supported workflow; no alternative voting modes.

## Open Questions

1. **Automatic/algorithmic estimate computation** — deliberately NOT a non-goal; v1 ships manual selection, but the user left the door open to add computed suggestions (e.g. mode/median) later. Owner: user. By: post-MVP.
2. **Voting scale options** — FR-006 allows configuring a "voting scale," but the specific scales offered (Fibonacci, T-shirt sizes, custom?) are not yet specified. Owner: user. By: before session-configuration implementation.
