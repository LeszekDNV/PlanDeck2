# Real-time Voting Round (S-06) — Plan Brief

> Full plan: `context/changes/realtime-voting-round/plan.md`

## What & Why

Build the real-time voting screen — the heart of PlanDeck. Assigned members join an Active session and vote on each task in real time; values stay hidden until any member reveals the round (then they appear together), and a member manually picks the agreed estimate, which is persisted. This is the slice the product's `quality` goal most protects (vote consistency + hidden reveal) and the prerequisite for the north-star ADO write-back (S-08).

## Starting Point

The F-02 contract (`realtime-vote-integrity`) already provides an authoritative, in-memory hidden-vote/reveal/reconnection room (SignalR hub + `PlanningRoomService` + `PlanningRoomState`), but with **one round per room, no task concept, and ephemeral votes**. Sessions, tasks, voting scale (`ScaleValues`), and assigned members (`SessionMember`, keyed by email) are persisted (F-01/S-04/S-05). There is **no voting UI** and **no field to store an agreed estimate**.

## Desired End State

An assigned member opens an Active session, clicks "Join voting" → `/voting/{sessionId}`, sees the task list, active task, scale cards, and a live roster of who-has-voted (not what). They vote (hidden); any member advances tasks, reveals (all votes together), resets to re-vote, and picks the agreed estimate — which persists on the task, broadcasts live, and survives reload.

## Key Decisions Made

| Decision | Choice | Why | Source |
| --- | --- | --- | --- |
| Per-task round model | Extend the F-02 room with an active-task pointer + task list + scale | One durable room per session; natural "next task"; minimal RoomKey change | Plan |
| Facilitator role | Any assigned member can drive (advance/reveal/reset/pick) | No separate role; authorization = "is assigned member" | Plan |
| Join authorization | Match caller `email` claim → `SessionMember.Email` (case-insensitive) | Zero schema change; works with S-05 email-based assignment | Plan |
| Persistence scope | Only the agreed estimate (votes stay in memory) | YAGNI; S-08 only needs the estimate | Plan |
| Estimate field shape | `string? AgreedEstimate` on `SessionTask` | Matches `ScaleValues` strings ("5","XL","?","☕") | Plan |
| Reveal trigger | Manual reveal only; live who-voted indicator | Exactly matches FR-009 (hidden until revealed; reveal together) | Plan |
| Entry point | "Join voting" button on Active sessions in `Sessions.razor` | Fits existing create→configure→activate→vote flow | Plan |
| Re-voting | `ResetRound` clears active-task votes; estimate kept until re-picked | Real planning-poker flow; reuses F-02 ResetRound | Plan |
| Pick persistence | Hub `SelectEstimate` persists (via app service) + broadcasts | Atomic, live for all, server stays authoritative | Plan |
| Testing depth | Unit + hub integration + two-browser E2E (Playwright) | Full coverage of the product's critical path | Plan |

## Scope

**In scope:** per-task hidden→reveal→reset→manual-pick→persist round; assigned-member authorization gate; new voting screen; entry from Sessions; en/pl localization; unit + integration + E2E tests.

**Out of scope:** ADO write-back (S-08), guest-link voting (S-07), auto-compute/consensus, persisted per-vote history, notifications, task/scale editing from the voting screen, multi-replica backplane.

## Architecture / Approach

Extend, don't re-derive. The hub (trust boundary) loads the session (tasks + scale) and assigned members from the DB, authorizes every call by email-membership, and seeds the in-memory room once. `PlanningRoomService` gains a per-task dimension (active task, per-task votes/reveal, per-task estimate) while staying DB-free and unit-testable. Two new hub ops: `SetActiveTask` and `SelectEstimate` (persists via an Application service, then broadcasts). The client gets matching wrapper methods and a new `VotingRoom.razor`; a two-browser Playwright E2E proves the realtime flow.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Domain & persistence | `AgreedEstimate` on `SessionTask` + migration + DTO + repo write | Migration correctness on startup |
| 2. Realtime contract (per-task) | Active-task room model + state DTOs + unit tests | Getting per-task scoping/idempotent seeding right |
| 3. Hub auth & persistence wiring | Assigned-member gate, room seeding, `SetActiveTask`/`SelectEstimate` + integration test | Authorization correctness; keeping logic out of Server |
| 4. Client & voting screen | `VotingRoom.razor`, Sessions entry, en/pl strings | Realtime UI state under reconnection |
| 5. E2E (Playwright) | Two-browser vote→reveal→pick test + page object | Two-context SignalR timing / Aspire boot |

**Prerequisites:** F-02, S-04, S-05 (all done). Podman for local E2E + Playwright browsers installed once.
**Estimated effort:** ~4-5 implementation sessions across 5 phases.

## Open Risks & Assumptions

- Assumes the authenticated user always carries an `email` claim matching their `SessionMember.Email` (case-insensitive) — the join gate depends on it.
- Per-task scoping of votes/reveal on top of the F-02 single-round model is the main rework risk if seeding/idempotency is mishandled.
- Two-context realtime E2E can be timing-sensitive; mitigated by Page Object waits and F-02's reconnection semantics.

## Success Criteria (Summary)

- Two assigned members vote on a task in real time with values hidden until a shared reveal, then see all votes together.
- A member manually picks the agreed estimate; it broadcasts live and persists on the task across reloads.
- The full flow is proven by unit, hub-integration, and a two-browser E2E test.
