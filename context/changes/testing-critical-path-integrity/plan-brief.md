# Critical-Path Integrity Tests — Plan Brief

> Full plan: `context/changes/testing-critical-path-integrity/plan.md`
> Research: `context/changes/testing-critical-path-integrity/research.md`

## What & Why

Write integration + targeted e2e tests covering Phase 1 of the test-plan rollout: risks #1 (session disconnect/reveal consistency), #2 (estimate save/write-back reliability), and #5 (session configuration correctness). These are the highest-risk scenarios (High×High and Medium×Medium) that currently have no dedicated test coverage beyond happy-path unit tests.

## Starting Point

The codebase already has:
- `PlanningRoomHubTests` — fixture with `WebApplicationFactory`, in-memory DB, connection helpers, one multi-step per-task flow test
- `VotingRoomTests` — one two-browser-context e2e (happy-path vote→reveal→pick)
- Unit tests for voting logic (reveal, vote hiding, scale validation) and session config (validation, scale resolution)
- `FakeAzureDevOpsWorkItemClient` configurable for success/failure responses
- `VotingRoomPage` and `SessionsPage` page objects with `data-testid` locators

## Desired End State

Integration tests prove: reconnect yields consistent state, full disconnect marks the participant offline while preserving vote state until explicit leave/reset, reveal is atomic, persist-first estimate never broadcasts on failure, last-write-wins is clean, ADO write-back signals errors explicitly, and session config propagates into the voting room. Four e2e tests prove these critical paths in real browsers, including user-visible ADO conflict signaling. Test-plan §6.1 and §6.4 cookbook entries are filled with reusable patterns.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
|----------|--------|-------------------|--------|
| Vote state on disconnect | Preserve on disconnect; clear on explicit leave/reset | A transient disconnect must not lose a submitted vote; explicit leave/reset remains the clearing boundary. | Research |
| Concurrent organizer | Add one last-write-wins test | Cheap to write and documents the behavior even though single-organizer is the current model. | Plan |
| E2E granularity | One comprehensive voting-room test per risk plus one ADO failure-signaling test (4 total) | Covers the browser-visible critical paths while keeping each scenario focused. | Plan |
| Test class organization | Extend existing PlanningRoomHubTests | Reuses fixture infrastructure with zero overhead; avoids premature abstraction. | Plan |
| Scale propagation testing | Test config→seed→vote-validation pipeline | Catches boundary bugs between SessionGrpcService and PlanningRoomService that unit tests miss. | Plan |

## Scope

**In scope:**
- Hub integration tests for disconnect/reconnect, reveal, persist-first estimate, ADO error mapping
- Config-to-voting pipeline integration test (scale propagation, task ordering)
- 4 targeted e2e tests (vote/reveal, estimate persistence, config-in-voting, ADO failure signaling)
- Cookbook §6.1 and §6.4 updates

**Out of scope:**
- ADO payload/contract testing (Phase 2)
- Authorization/tenant isolation (Phase 3)
- New test infrastructure or base classes
- Multi-replica/distributed scenarios
- UI styling/layout testing

## Architecture / Approach

Tests are layered by cost×signal: hub integration tests (fast, in-memory DB, 1-5s each) cover most invariants. E2E tests (Aspire + Playwright, 30-60s each) cover only the cross-cutting path where integration between WASM client, SignalR, and DB must be proven in a real browser. Unit tests fill ADO precondition validation. No new fixtures created — existing infrastructure extended.

## Phases at a Glance

| Phase | What it delivers | Key risk |
|-------|-----------------|----------|
| 1. Hub integration tests (Risk #1 + #2) | 11 new hub test methods + validation of existing ADO gRPC unit coverage | Disconnect simulation reliability with LongPolling transport |
| 2. Config-to-voting integration (Risk #5) | 3-5 config pipeline tests + persistence round-trip | Complex seed→activate→join→vote setup per test |
| 3. Targeted e2e tests | 4 comprehensive browser tests | WASM boot + SignalR timing; deterministic ADO failure injection; test determinism across runs |
| 4. Cookbook update (§6) | Filled §6.1 and §6.4 in test-plan.md | Keeping recipes concise yet actionable |

**Prerequisites:** Podman running (for e2e/Aspire tests). Existing test suite passes (`dotnet test PlanDeck.slnx`).
**Estimated effort:** ~2-3 sessions across 4 phases.

## Open Risks & Assumptions

- Disconnect simulation via `HubConnection.DisposeAsync()` should trigger `OnDisconnectedAsync` — verify in Phase 1 first test
- E2E reconnect test simulates disconnect via navigation (not actual network drop) — acceptable for MVP
- `FakeAzureDevOpsWorkItemClient` may need extension to throw typed exceptions if not already supported

## Success Criteria (Summary)

- `dotnet test PlanDeck.slnx` passes with all new tests green (no regressions)
- Each risk's critical invariant is provable by running its test in isolation
- Cookbook §6.1/§6.4 lets a developer add a new test without reading the full test file
