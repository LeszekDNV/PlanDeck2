# Real-time Vote-Integrity Baseline (F-02) — Plan Brief

> Full plan: `context/changes/realtime-vote-integrity/plan.md`

## What & Why

Harden PlanDeck's existing in-memory planning-room spike into the **authoritative hidden-vote/reveal contract**: vote values never observable before reveal, no lost/duplicated/reordered votes, and drop/reconnect that never corrupts room state — all bound to a validated, tenant-scoped session identity from the authenticated principal. The `quality` north-star forbids deferring vote-consistency correctness behind user-facing work; two slices (S-06 assigned-member voting, S-07 guest voting) depend on this exact contract, so it is hardened once, centrally, with tests.

## Starting Point

The spike already works end-to-end but trusts the client: `PlanningRoomService` (singleton, hides votes until reveal) keys rooms by a raw `sessionId` string and participants by a client-supplied id; `PlanningRoomHub` is anonymous, takes identity from method arguments, and never handles disconnect; the client `WithAutomaticReconnect()`s but never re-joins after the `ConnectionId` changes. No tests exist for any of it. The client service is registered but not yet used by any page.

## Desired End State

The room enforces integrity regardless of client behavior: votes stay hidden on the wire until a single synchronized reveal; concurrent casts all land with last-write-per-person and no duplicates; clients never apply an older room snapshot after a newer one; a participant that drops keeps its vote and shows **offline**, then re-attaches **online** with its vote intact on reconnect; one connection belongs to at most one room; rooms are keyed by `(tenant, session-Guid)` from claims and require an existing Active session; and a unit suite plus focused hub integration tests prove it.

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
| Snapshot ordering | Monotonic room revision; client ignores stale snapshots | Per-room mutation locks do not order concurrent `SendAsync` completion. | Plan review |
| Connection ownership | One active room per `connectionId`; explicit leave before switching | A single-owner map cannot safely represent one connection in multiple rooms. | Plan review |
| Persisted session validation | Application service verifies an Active tenant-scoped session before join | A well-formed Guid alone does not prove a real session exists. | Plan review |
| Vote semantics | Changeable before reveal (last-write); locked after | Standard planning-poker; cast-after-reveal rejected. | Plan |
| Reveal/reset | Reveal idempotent, allowed at partial turnout; reset clears + re-hides | Predictable facilitator controls. | Plan |
| State durability | Accept loss on restart; single replica; backplane = later trigger | MVP per `infrastructure.md`; multi-replica needs Azure SignalR/Redis. | Plan |
| Test depth | Strong service unit tests + one focused hub integration test; E2E → S-06 | The service holds the logic; the hub test proves transport lifecycle. | Plan |

## Scope

**In scope:** integrity rules in `PlanningRoomService`; `(tenant, session)` room keying; claims-derived identity; Active persisted-session validation; `[Authorize]` + disconnect lifecycle on the hub; one-room-per-connection ownership; client auto-rejoin and stale-snapshot rejection; `IsOnline` + room revision on the wire; unit + hub integration tests.

**Out of scope:** UI/Blazor page, per-task rounds, DB persistence / migrations, agreed-estimate write-back, who-may-join authorization (S-06/S-07), multi-replica backplane, any gRPC for the room.

## Architecture / Approach

Six inward-out phases. The integrity logic lives in the singleton in-memory `PlanningRoomService`, keyed by `RoomKey(TenantId, SessionId)`; the service owns a global, single-owner `connectionId → (room, participant)` map so the hub's `OnDisconnectedAsync` can resolve the room from a connection alone. The hub is the trust boundary: `[Authorize]`, identity from `Hub.Context.User` claims, Active-session validation through an application service, delegates room state to the service, and broadcasts `RoomStateChanged` to the group `"{tid}:{sessionId}"`. Room snapshots carry a monotonic revision, and the client remembers its session, re-joins on `Reconnected`, and ignores stale revisions.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Service + unit tests | Hardened in-memory contract (identity, connections, online/offline, reveal/reset/cast rules) with full unit suite | Mechanical hub/client wiring must keep the solution green, then be replaced by claims-derived identity in Phase 2 |
| 2. Hub lifecycle & auth | `[Authorize]`, claims-derived identity, connection tracking, `OnDisconnectedAsync`, validated session Guid | Confirming `Context.User` claims populate on the WebSocket principal |
| 3. Client reconnect + integration test | Auto-rejoin on reconnect; `WebApplicationFactory` + `HubConnection` test proving lifecycle + hidden-on-wire | Test-server SignalR wiring + single fixed test identity limits transport test to single-participant scenarios |
| 4. Monotonic state delivery | Room revision + client stale-snapshot gate | Concurrent sends can complete outside authoritative mutation order |
| 5. Connection ownership | One room per connection + explicit client leave-before-switch | Keeping service ownership and SignalR group membership transactional |
| 6. Active session validation | Tenant-scoped Active-session lookup + behavioral anonymous rejection test | Supplying the hub principal to scoped repository access without weakening tenant isolation |

**Prerequisites:** F-01 (tenant-scoped sessions — already in place). Podman running for any Aspire-backed test run.
**Estimated effort:** ~2-3 implementation sessions across 3 phases.

## Open Risks & Assumptions

- The test scheme provides a single fixed `oid`, so multi-participant integrity is proven by Phase 1 unit tests (where identities are controllable), and the hub test covers single-identity connect/disconnect/reconnect.
- `Hub.Context.User` is assumed populated from the negotiate request's cookie/test-scheme auth; verified in Phase 2 manual check (401 vs 200).
- Room revisions reset with in-memory state after a process restart; the client resets its revision baseline when a fresh/reconnected transport joins.
- One connection intentionally cannot observe multiple rooms; switching rooms is an explicit leave-then-join transition.
- In-memory state is lost on restart by design; acceptable for single-replica MVP only.

## Success Criteria (Summary)

- A participant can drop and reconnect mid-round with its vote preserved and never revealed early.
- Concurrent votes from a team all register correctly, with one vote per person, and clients never regress to an older snapshot.
- A connection has one authoritative room owner, and only an existing Active tenant-scoped session can be joined.
- `dotnet test` (unit + the focused hub integration test) is green, proving the contract end-to-end.
