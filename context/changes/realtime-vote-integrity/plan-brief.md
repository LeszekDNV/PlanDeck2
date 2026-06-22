# Real-time Vote-Integrity Baseline (F-02) — Plan Brief

> Full plan: `context/changes/realtime-vote-integrity/plan.md`

## What & Why

Harden PlanDeck's existing in-memory planning-room spike into the **authoritative hidden-vote/reveal contract**: vote values never observable before reveal, no lost/duplicated/reordered votes, and drop/reconnect that never corrupts room state — all bound to a validated, tenant-scoped session identity from the authenticated principal. The `quality` north-star forbids deferring vote-consistency correctness behind user-facing work; two slices (S-06 assigned-member voting, S-07 guest voting) depend on this exact contract, so it is hardened once, centrally, with tests.

## Starting Point

The spike already works end-to-end but trusts the client: `PlanningRoomService` (singleton, hides votes until reveal) keys rooms by a raw `sessionId` string and participants by a client-supplied id; `PlanningRoomHub` is anonymous, takes identity from method arguments, and never handles disconnect; the client `WithAutomaticReconnect()`s but never re-joins after the `ConnectionId` changes. No tests exist for any of it. The client service is registered but not yet used by any page.

## Desired End State

The room enforces integrity regardless of client behavior: votes stay hidden on the wire until a single synchronized reveal; concurrent casts all land with last-write-per-person and no duplicates; a participant that drops keeps its vote and shows **offline**, then re-attaches **online** with its vote intact on reconnect; rooms are keyed by `(tenant, session-Guid)` from claims; and a unit suite plus a focused hub integration test prove it.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Real-time transport | Keep SignalR (not bidirectional gRPC) | gRPC-Web from the browser supports only unary + server-streaming; foundation already locked SignalR. | Plan |
| Sequencing vs S-06 | F-02 is a separate change, done first | It's its own roadmap foundation (`realtime-vote-integrity`) that S-06/S-07 bind to. | Plan |
| Session/tenant binding | Identity from `oid` claim + room key `(tid, sessionId-Guid)`, no DB in hub | No `HttpContext` exists in hub invocations, so DB/tenant scoping there would silently break. | Plan |
| Participant identity | `oid`; one participant per person, many connections share it | Stable across reconnects and tabs; "online" = holds ≥1 live connection. | Plan |
| Disconnect semantics | Keep entry + vote; remove only on explicit LeaveRoom | A dropped participant must not lose their vote mid-round. | Plan |
| Wire contract | Add per-participant `IsOnline`; value still hidden until reveal | Accurate "who's here / who voted" despite drops. | Plan |
| Reconnection | Client auto-rejoins on `Reconnected`; server `JoinRoom` idempotent | `ConnectionId` changes on reconnect; idempotent join avoids duplicates. | Plan |
| Vote semantics | Changeable before reveal (last-write); locked after | Standard planning-poker; cast-after-reveal rejected. | Plan |
| Reveal/reset | Reveal idempotent, allowed at partial turnout; reset clears + re-hides | Predictable facilitator controls. | Plan |
| State durability | Accept loss on restart; single replica; backplane = later trigger | MVP per `infrastructure.md`; multi-replica needs Azure SignalR/Redis. | Plan |
| Test depth | Strong service unit tests + one focused hub integration test; E2E → S-06 | The service holds the logic; the hub test proves transport lifecycle. | Plan |

## Scope

**In scope:** integrity rules in `PlanningRoomService`; `(tenant, session)` room keying; claims-derived identity; `[Authorize]` + disconnect lifecycle on the hub; client auto-rejoin; `IsOnline` on the wire; unit + hub integration tests.

**Out of scope:** UI/Blazor page, per-task rounds, DB persistence / migrations, agreed-estimate write-back, who-may-join authorization (S-06/S-07), multi-replica backplane, any gRPC for the room.

## Architecture / Approach

Three inward-out phases. The integrity logic lives in the singleton in-memory `PlanningRoomService`, keyed by `RoomKey(TenantId, SessionId)`; the service owns a global `connectionId → (room, participant)` map so the hub's `OnDisconnectedAsync` can resolve the room from a connection alone. The hub is the trust boundary: `[Authorize]`, identity from `Hub.Context.User` claims, delegates all state to the service, broadcasts `RoomStateChanged` to the group `"{tid}:{sessionId}"`. The client remembers its session and re-joins on `Reconnected`.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Service + unit tests | Hardened in-memory contract (identity, connections, online/offline, reveal/reset/cast rules) with full unit suite | Getting concurrency + reconnect state model right; interface change temporarily breaks hub until Phase 2 |
| 2. Hub lifecycle & auth | `[Authorize]`, claims-derived identity, connection tracking, `OnDisconnectedAsync`, validated session Guid | Confirming `Context.User` claims populate on the WebSocket principal |
| 3. Client reconnect + integration test | Auto-rejoin on reconnect; `WebApplicationFactory` + `HubConnection` test proving lifecycle + hidden-on-wire | Test-server SignalR wiring + single fixed test identity limits transport test to single-participant scenarios |

**Prerequisites:** F-01 (tenant-scoped sessions — already in place). Podman running for any Aspire-backed test run.
**Estimated effort:** ~2-3 implementation sessions across 3 phases.

## Open Risks & Assumptions

- The test scheme provides a single fixed `oid`, so multi-participant integrity is proven by Phase 1 unit tests (where identities are controllable), and the hub test covers single-identity connect/disconnect/reconnect.
- `Hub.Context.User` is assumed populated from the negotiate request's cookie/test-scheme auth; verified in Phase 2 manual check (401 vs 200).
- In-memory state is lost on restart by design; acceptable for single-replica MVP only.

## Success Criteria (Summary)

- A participant can drop and reconnect mid-round with its vote preserved and never revealed early.
- Concurrent votes from a team all register correctly, with one vote per person.
- `dotnet test` (unit + the focused hub integration test) is green, proving the contract end-to-end.
