# Assign Session Members (S-05) — Plan Brief

> Full plan: `context/changes/assign-session-members/plan.md`

## What & Why

Let a user assign team members to a planning session (FR-007). Sessions need a persistent roster of who is taking part so that (later) real-time voting and history have known members to attach to. This slice delivers the assignment list itself — distinct from the ad-hoc guest-link path (S-07).

## Starting Point

Teams (S-01) and sessions (S-04) are done. `PlanningSession` already has an optional `TeamId`, `Draft`/`Active` status, and cascade-deleted tasks; the `DbContext` enforces tenant isolation automatically for any `TenantEntity`. There is **no** persistent session-membership concept yet — only ephemeral SignalR participant state. The Team-member slice is a complete, mirror-able template.

## Desired End State

The session config card has a "Members" section: type an email (+ optional display name), assign, see it listed, remove it. Works in any session status. Duplicates and invalid emails are rejected; all data is tenant-scoped. Covered by integration, unit, and E2E tests.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Membership model | Flat assignment list (no accept/decline) | MVP; notifications (FR-011) are out of scope | Plan |
| Source of members | Ad-hoc email, not bound to team roster | Simpler; a session needs no team to have members | Plan |
| When assignable | Any status (also Active) | Members may be added after a session starts | Plan |
| Contract placement | New `ISessionMemberService` | Keep membership concern separate from `ISessionService` | Plan |
| Member list delivery | Dedicated `ListSessionMembers` call | `SessionDto` stays unchanged | Plan |
| Validation/duplicates | Email must contain `@`; unique per session → `AlreadyExists` | Mirrors team `AddMember` behavior | Plan |
| Test scope | Integration + unit + E2E | Full confidence across the slice | Plan |

## Scope

**In scope:** `SessionMember` entity + migration, `ISessionMemberRepository`/impl, `ISessionMemberService` contract + DTOs + app service, server DI/endpoint wiring, client wrapper, Members UI in `Sessions.razor`, `en`/`pl` strings, integration/unit/E2E tests.

**Out of scope:** invitation/acceptance workflow, team-roster binding, notifications, per-session roles, planning-room pre-population (S-06), guest links (S-07), `SessionDto` changes.

## Architecture / Approach

A new vertical slice mirroring the Team-member slice, keyed on `SessionId` + `Email`:
`SessionMember : TenantEntity` → `SessionMemberConfiguration` (unique `(TenantId, SessionId, Email)`, cascade FK) → `SessionMemberRepository` → `ISessionMemberService` (code-first gRPC) → `SessionMemberGrpcService` → `SessionMemberClientService` → "Members" section in `Sessions.razor`. Tenant isolation and audit stamping come for free from `PlanDeckDbContext`.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Domain & Persistence | Entity, config, migration, repository + integration tests | Migration must produce the unique index + cascade FK correctly |
| 2. Contract & Service | gRPC contract/DTOs, app service, DI/endpoint + unit tests | Correct exception→status mapping |
| 3. Client, UI, Localization & E2E | Client wrapper, Members UI, resx, E2E test | E2E needs local Aspire (Podman) + Playwright; WASM timing |

**Prerequisites:** S-01 + S-04 done (both are). For E2E: Podman running, Playwright chromium installed.
**Estimated effort:** ~2–3 sessions across 3 phases.

## Open Risks & Assumptions

- Assumes ad-hoc email assignment is acceptable even though FR-007 says "team members" — confirmed with the user.
- E2E reliability depends on the local Aspire boot + WASM render waits (existing pattern handles this).

## Success Criteria (Summary)

- A user can assign a member by email, see them listed, and remove them — persisting across reloads.
- Duplicate/invalid assignments are rejected with clear messages; data is tenant-isolated.
- Assignment works in both Draft and Active status, in `en` and `pl`.
