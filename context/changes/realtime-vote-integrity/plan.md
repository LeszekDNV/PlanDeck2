# Real-time Vote-Integrity Baseline (F-02) Implementation Plan

## Overview

Harden the existing in-memory planning-room spike into the **authoritative hidden-vote/reveal contract**. After this change, the real-time room guarantees: vote values are never observable before reveal, votes are never lost/duplicated/reordered under concurrency, a participant can drop and reconnect without corrupting room state, and the room is keyed by a validated, tenant-scoped session identity derived from the authenticated principal rather than client-supplied strings. The contract is proven by a strong unit-test suite plus a focused hub integration test.

This is a **foundation** change: no UI, no database schema, no per-task rounds, and no agreed-estimate persistence. Those belong to the consuming slices (S-06 assigned-member voting, S-07 guest voting), which wire this contract to real sessions and screens.

## Current State Analysis

The spike is already wired end-to-end but is a thin, trust-the-client prototype:

- **`PlanningRoomService`** (`src/PlanDeck/Core/PlanDeck.Application/Planning/PlanningRoomService.cs`) — singleton, `ConcurrentDictionary<string, PlanningRoom>` keyed by raw `sessionId` string, `lock (room)` per mutation. Participants keyed by a **client-supplied** `participantId`. Hidden-until-reveal is implemented in `ToState` (line 91: `room.IsRevealed ? participant.Value.Vote : null`). No concept of connection, online/offline, or reconnection. `CastVote` accepts a vote at any time, including after reveal.
- **`IPlanningRoomService`** (`Core/PlanDeck.Application/Planning/IPlanningRoomService.cs`) — `Join/Leave/CastVote/RevealVotes/ResetRound`, all `(string sessionId, …)`.
- **`PlanningRoomState` / `PlanningParticipantState`** (`Core/PlanDeck.Core.Shared/Realtime/PlanningRoomState.cs`) — wire contract shared by client and server. Participant exposes `ParticipantId, DisplayName, HasVoted, Vote?`. No online/offline flag.
- **`PlanningRoomHub`** (`Web/PlanDeck.Server/Hubs/PlanningRoomHub.cs`) — anonymous (no `[Authorize]`). Hub methods take `sessionId, participantId, displayName` **from the client**. Group name = raw `sessionId`. Does **not** override `OnDisconnectedAsync`. Mapped at `Program.cs:97` (`app.MapHub<PlanningRoomHub>("/hubs/planning-room")`).
- **`PlanningRoomClientService` / `IPlanningRoomClientService`** (`Web/PlanDeck.Client/Services/`) — `HubConnection` with `.WithAutomaticReconnect()`, registered in client `Program.cs`, but **not used by any page** (confirmed: no `.razor` references it). On reconnect the `ConnectionId` changes and the client never re-invokes `JoinRoom`, so a reconnected participant silently stops receiving updates.
- **DI** — `services.AddSingleton<IPlanningRoomService, PlanningRoomService>()` (`Web/PlanDeck.Server/Extensions/ServiceCollectionExtensions.cs:114`).

### Key Discoveries

- **`ICurrentUserContext` is HttpContext-based** (`Web/PlanDeck.Server/Identity/HttpContextCurrentUserContext.cs`) — during a SignalR hub method invocation there is **no reliable ambient `HttpContext`**, so the existing repository/tenant machinery returns `Guid.Empty` unless the hub principal is bridged into the invocation scope. Identity at the hub boundary **must** be read from `Hub.Context.User`; tenant-scoped data access is allowed only through an application service after explicitly supplying that principal to the scoped current-user context.
- **Claims available** (`Web/PlanDeck.Server/Identity/TestAuthenticationHandler.cs`): `tid` (tenant), `oid` (user object id), `name`, `email`. The OIDC path maps the same claim names (`HttpContextCurrentUserContext` reads `tid`/`oid`/`name`/`email`). So `oid` is a stable per-user identity and `tid` is the tenant — both present on the authenticated WebSocket principal.
- **Identity model decision** (from planning): participant identity = `oid`. One participant per person; multiple connections of the same person share one participant entry; the participant is **online** while it holds ≥1 live connection. Reconnection = a new connection for the same `oid`, which re-attaches to the existing entry and its preserved vote.
- **Test wiring**: `PlanDeck.Unit.Tests` already references `PlanDeck.Application`, so `PlanningRoomService` is directly unit-testable. `PlanDeck.Integration.Tests` references `PlanDeck.AppHost` + `PlanDeck.Infrastructure` (Aspire) but **not** `PlanDeck.Server`, and lacks `Microsoft.AspNetCore.Mvc.Testing` / `Microsoft.AspNetCore.SignalR.Client` — these must be added for the hub integration test, along with a `public partial class Program` marker in the Server so `WebApplicationFactory<Program>` can target it. The Server runs with `Authentication:UseTestScheme=true` in test config, giving an authenticated principal automatically.
- **Single-replica MVP** (`context/foundation/infrastructure.md:67,98,103,123`): in-memory room state is acceptable; a server restart drops rooms by design. Multi-replica requires an Azure SignalR / Redis backplane — documented as a later trigger, **out of scope** here.
- **Transport is locked**: SignalR/WebSockets, not bidirectional gRPC. gRPC-Web from the browser supports only unary + server-streaming (`context/foundation/infrastructure.md:101`).

## Desired End State

The planning room enforces the integrity contract regardless of client behavior:

1. **Hidden-until-reveal** — no `PlanningRoomState` emitted over the wire carries any non-null `Vote` while `IsRevealed == false`, for any participant, at any time.
2. **No lost/duplicated/reordered votes** — concurrent casts from distinct participants all land; repeated casts from the same participant before reveal overwrite (last-write) without creating duplicate entries; a participant is never double-listed; clients never apply an older room snapshot after a newer one.
3. **Drop/reconnect safe** — when a connection drops, the participant entry and its vote are retained and the participant is shown **offline**; on reconnect (same `oid`) the participant re-attaches, regains **online**, and its vote is intact. Only an explicit `LeaveRoom` removes the participant. One live `connectionId` belongs to at most one room, so disconnect always updates the room that owns it.
4. **Tenant-scoped, validated identity** — rooms are keyed by `(tenantId, sessionId)` from the authenticated principal + an existing Active persisted session identified by a well-formed `Guid`; `participantId`/`displayName` come from claims, never from client arguments. The hub requires authentication.
5. **Proven** — a unit suite over `PlanningRoomService` and a focused hub integration test (connect → cast → disconnect → reconnect → reveal) pass in CI via `dotnet test`.

Verify by running the unit + integration tests and by a local smoke (two authenticated connections, drop one mid-round, confirm vote survives and stays hidden until reveal).

## What We're NOT Doing

- **No UI / Blazor page** for voting — S-06 owns the voting-round screen.
- **No per-task rounds** — the room remains a single round abstraction; "which task is being voted" and advancing task-by-task is S-06.
- **No database persistence** of votes or an agreed estimate, and **no new migration** — S-06 persists the agreed estimate via gRPC.
- **No DB-backed membership authorization** of *who may join* (assigned-member vs guest) — that membership check is S-06 (assigned members) and S-07 (guest links). F-02 does verify that the tenant-scoped session exists and is Active, but any authenticated principal in that tenant may join it.
- **No role-based authorization of *who may reveal/reset*** — `RevealVotes`/`ResetRound` accept any authenticated participant in F-02. The facilitator/role model that gates these moderator actions is **S-06**. F-02 guarantees votes are hidden *until someone reveals*, not *who* is allowed to reveal.
- **No multi-replica backplane** (Azure SignalR / Redis) — single-replica MVP; documented as a later trigger only.
- **No gRPC contract** for the room — real-time stays on SignalR.

## Implementation Approach

Work inward-out in six phases. Phase 1 rebuilds the integrity logic where it actually lives — the in-memory service — together with its full unit suite, so correctness is provable without any transport. Phase 2 hardens the hub: authentication, claims-derived identity, connection tracking, and disconnect lifecycle, delegating all state to the Phase 1 service. Phase 3 makes the client resilient to reconnects and adds the transport-level integration test that proves the end-to-end lifecycle and the hidden-on-the-wire guarantee. Phase 4 closes the transport-ordering gap by versioning room snapshots and preventing clients from applying stale state. Phase 5 enforces the one-room-per-connection invariant so disconnect cannot leave ghost presence in a previously joined room. Phase 6 validates that the tenant-scoped session exists and is Active before creating or joining its in-memory room.

The room key becomes a small value type `RoomKey(Guid TenantId, Guid SessionId)`; the SignalR group name is its stable string form `"{TenantId}:{SessionId}"`. The service owns a global `connectionId → (RoomKey, participantId)` map so the hub's `OnDisconnectedAsync` (which only knows the `ConnectionId`) can resolve and update the right room without the client re-sending the key.

## Critical Implementation Details

- **No reliable ambient `HttpContext` in hub invocations** — read `tid`/`oid`/`name`/`email` from `Hub.Context.User` directly. Do not inject a repository into the hub. Before calling an application service that uses tenant-scoped repositories, bridge `Context.User` into the invocation's scoped current-user accessor so the EF query filter resolves the same `tid`.
- **SignalR auto-removes connections from groups on disconnect** — do not attempt manual group removal in `OnDisconnectedAsync`; only update the in-memory room (mark offline) and broadcast the new state to the remaining group members.
- **Same-user multiple connections** — `OnDisconnectedAsync` must mark the participant offline **only when its last connection drops**, not on the first; the service tracks a per-participant connection set.

## Phase 1: Service-layer integrity contract + unit tests

### Overview

Redesign the in-memory room model and its public interface to carry stable identity, connection tracking, online/offline, and the full reveal/reset/cast rules; key rooms by `(tenantId, sessionId)`. Cover every contract rule with unit tests. No hub or client changes yet — the service and its wire contract are the deliverable.

### Changes Required

#### 1. Room key value type

**File**: `src/PlanDeck/Core/PlanDeck.Application/Planning/RoomKey.cs` (new)

**Intent**: Give rooms a tenant-scoped composite identity so two tenants can never share a room and the SignalR group name is derived consistently.

**Contract**: `public readonly record struct RoomKey(Guid TenantId, Guid SessionId)` with a `GroupName` accessor returning `"{TenantId}:{SessionId}"`. Used as the `ConcurrentDictionary` key and as the SignalR group name source.

#### 2. Wire contract — online/offline

**File**: `src/PlanDeck/Core/PlanDeck.Core.Shared/Realtime/PlanningRoomState.cs`

**Intent**: Let consumers render an accurate "who is in the room / who has voted" even while some participants are temporarily disconnected, without ever leaking vote values.

**Contract**: Add `bool IsOnline` to `PlanningParticipantState` (record positional parameter, appended after `Vote`). `PlanningRoomState` keeps `SessionId`, `IsRevealed`, `Participants`. `Vote` stays `null` for every participant whenever `IsRevealed` is false. `ParticipantId` carries the `oid` so a client can identify itself.

#### 3. Service interface

**File**: `src/PlanDeck/Core/PlanDeck.Application/Planning/IPlanningRoomService.cs`

**Intent**: Express the hardened operations in terms of a `RoomKey`, a stable `participantId`, and connections, plus a connection-driven disconnect path.

**Contract**: New surface (all return `PlanningRoomState`):
- `Join(RoomKey key, string participantId, string displayName, string connectionId)`
- `Leave(RoomKey key, string participantId)` — explicit removal of the participant.
- `Disconnect(string connectionId)` → returns `(RoomKey, PlanningRoomState)?` (null when the connection is unknown) so the hub can broadcast to the right group; drops the connection and marks the participant offline if it was the last one.
- `CastVote(RoomKey key, string participantId, string vote)`
- `RevealVotes(RoomKey key)`
- `ResetRound(RoomKey key)`
- `GetState(RoomKey key)` — for re-broadcast on idempotent rejoin.

#### 4. Service implementation

**File**: `src/PlanDeck/Core/PlanDeck.Application/Planning/PlanningRoomService.cs`

**Intent**: Implement the integrity rules decided in planning. All mutations stay under the existing per-room `lock`.

**Contract**: Behavior rules to enforce:
- `Join` is **idempotent** per `participantId`: first join creates the entry; a repeat (e.g. reconnect or second tab) adds the `connectionId` to the participant's connection set and marks it online, preserving any existing vote — never duplicates the participant.
- A participant is **online** iff its connection set is non-empty.
- `Disconnect` removes the `connectionId` from whichever participant holds it; the participant goes offline only when its set becomes empty; the entry and its vote are retained. Maintains a global `ConcurrentDictionary<string, (RoomKey, string participantId)>` connection map.
- `CastVote` requires the participant to have joined; allowed only while `!IsRevealed`; overwrites the prior value (last-write); throws/`no-ops` a clear domain signal if the round is already revealed.
- `RevealVotes` is idempotent (revealing twice is a no-op returning revealed state); allowed regardless of how many have voted.
- `ResetRound` clears `IsRevealed` and every participant's vote, returning to hidden state; keeps participants and their online/offline status.
- `Leave` removes the participant entirely and its connections.
- `ToState` continues to null out `Vote` unless `IsRevealed`, now also projecting `IsOnline`, ordered by display name.

#### 5. Unit tests

**File**: `src/PlanDeck/Tests/PlanDeck.Unit.Tests/Planning/PlanningRoomServiceTests.cs` (new)

**Intent**: Prove every contract rule; this suite is the heart of F-02.

**Contract**: NUnit `[TestFixture]` covering at least: hidden-until-reveal (no vote in state pre-reveal, all votes present post-reveal); reveal is idempotent and works with partial turnout; cast rejected after reveal; vote change before reveal (last-write, no duplicate); reset clears votes and re-hides; join is idempotent (same `oid` twice → one participant, vote preserved); disconnect keeps the vote and flips online→offline only on last connection; reconnect (join after disconnect) restores online with vote intact; explicit leave removes the participant; two tenants with the same `SessionId` get isolated rooms; concurrency — N parallel casts from N participants all land with no lost/duplicate entries (e.g. `Parallel.For` then assert count + values).

#### 6. Keep the solution compiling — minimal call-site re-wiring

**File**: `src/PlanDeck/Web/PlanDeck.Server/Hubs/PlanningRoomHub.cs`, `src/PlanDeck/Web/PlanDeck.Client/Services/PlanningRoomClientService.cs` + `IPlanningRoomClientService.cs`

**Intent**: The Phase 1 interface change breaks the only two consumers (grep-confirmed: hub + client service). Re-wire them mechanically in the same phase so `dotnet build PlanDeck.slnx` stays green at the phase boundary. This is **mechanical adaptation only** — the real authorization/lifecycle work (Phase 2) and reconnection work (Phase 3) layer on top of a compiling baseline.

**Contract**: Update the hub to build a `RoomKey` and call the new `Join/Leave/CastVote/RevealVotes/ResetRound/Disconnect` signatures (identity may stay client-supplied *for now* — Phase 2 replaces it with `Context.User` claims). Update the client service/interface to the trimmed method signatures (Phase 3 adds the `Reconnected` auto-rejoin). Do not add `[Authorize]`, `OnDisconnectedAsync`, or claims handling yet — those are Phase 2.

### Success Criteria

#### Automated Verification

- Solution builds: `dotnet build PlanDeck.slnx` (from `src/PlanDeck/`)
- Unit tests pass: `dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj`
- New `PlanningRoomServiceTests` fixture is present and green (analyzers clean — build treats NUnit analyzers as part of compile)

#### Manual Verification

- Reading `PlanningRoomServiceTests` confirms each contract rule from "Desired End State" maps to a named test.
- Hub + client service compile against the new interface via the mechanical re-wiring (no behavior added yet beyond the new signatures).

**Implementation Note**: Phase 1 keeps the whole solution building by re-wiring the hub/client call sites mechanically (change #6); auth, disconnect lifecycle, and reconnection are deliberately deferred to Phases 2–3. Pause for manual confirmation before proceeding.

---

## Phase 2: Hub lifecycle & authorization

### Overview

Make `PlanningRoomHub` the trust boundary: require authentication, derive identity and tenant from `Hub.Context.User` claims, track connections, and handle disconnect. The hub delegates all state to the Phase 1 service and never touches the database.

### Changes Required

#### 1. Authorize and re-key the hub

**File**: `src/PlanDeck/Web/PlanDeck.Server/Hubs/PlanningRoomHub.cs`

**Intent**: Stop trusting client-supplied identity; bind every operation to the authenticated principal and a validated session id.

**Contract**:
- Annotate the hub with `[Authorize]`. **Note**: this is intentionally the *only* `[Authorize]` gate in the codebase today — existing gRPC services and the current hub are anonymous, relying on the always-present principal plus client-side `AuthorizeView`. Scoping the attribute to the hub (not a global fallback policy) keeps the blast radius to F-02; broader endpoint authorization is out of scope.
- Hub methods take only the data the client legitimately supplies: `JoinRoom(string sessionId)`, `CastVote(string sessionId, string vote)`, `RevealVotes(string sessionId)`, `ResetRound(string sessionId)`, `LeaveRoom(string sessionId)`. `participantId` and `displayName` are read from `Context.User` (`oid`, `name`/`email`) — **removed from the parameter lists**.
- Parse `sessionId` to `Guid` and read `tid` to build `RoomKey`; reject (throw `HubException`) on a missing/blank/non-Guid session id or missing tenant claim.
- `JoinRoom` adds the connection to the group `key.GroupName`, calls `service.Join(key, oid, displayName, Context.ConnectionId)`, broadcasts `RoomStateChanged` to the group.
- Override `OnDisconnectedAsync` to call `service.Disconnect(Context.ConnectionId)` and, if it resolves a room, broadcast the updated state to that group (no manual group removal — SignalR handles it).
- All broadcasts target `Clients.Group(key.GroupName)`.

#### 2. Confirm DI + endpoint unchanged

**File**: `src/PlanDeck/Web/PlanDeck.Server/Extensions/ServiceCollectionExtensions.cs`, `Program.cs`

**Intent**: The singleton registration and hub mapping already exist; ensure they still satisfy the new service (singleton is correct — the connection map and rooms are process-global).

**Contract**: No change expected to `AddSingleton<IPlanningRoomService, PlanningRoomService>()` (line 114) or `MapHub<PlanningRoomHub>("/hubs/planning-room")` (`Program.cs:97`). Verify the hub endpoint still maps after `[Authorize]` (auth/authorization middleware already present, `Program.cs:57-58`).

### Success Criteria

#### Automated Verification

- Solution builds: `dotnet build PlanDeck.slnx`
- Existing unit + persistence tests still pass: `dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj`

#### Manual Verification

- App boots via Aspire (`dotnet run --project Aspire/PlanDeck.AppHost`, Podman running); an authenticated client completes the `/hubs/planning-room` negotiate and `JoinRoom` succeeds. The `[Authorize]` attribute is a quick structural guard; Phase 6 adds a transport-level negative test with a non-authenticating test handler.
- Hub method signatures no longer accept `participantId`/`displayName` from the client.

**Implementation Note**: Pause for manual confirmation that the hub authorizes and binds identity correctly before proceeding.

---

## Phase 3: Client reconnection + hub integration test

### Overview

Make the client auto-rejoin on reconnect and prove the full transport lifecycle with a focused integration test that also asserts the hidden-on-the-wire guarantee. Still no UI — the client service stays page-agnostic.

### Changes Required

#### 1. Client service reconnection + trimmed signatures

**File**: `src/PlanDeck/Web/PlanDeck.Client/Services/PlanningRoomClientService.cs`, `IPlanningRoomClientService.cs`

**Intent**: Survive the `ConnectionId` change on automatic reconnect by re-joining the last room, and align method signatures with the Phase 2 hub.

**Contract**:
- Drop `participantId`/`displayName` from `JoinRoomAsync`/`CastVoteAsync`/`LeaveRoomAsync` (server derives them); signatures become `JoinRoomAsync(string sessionId)`, `CastVoteAsync(string sessionId, string vote)`, `LeaveRoomAsync(string sessionId)`, `RevealVotesAsync(string sessionId)`, `ResetRoundAsync(string sessionId)`.
- Remember the joined `sessionId`; subscribe to `HubConnection.Reconnected` to re-invoke `JoinRoom(sessionId)` automatically.
- Register the `RoomStateChanged` handler exactly once (guard against double registration if `ConnectAsync` is called more than once).
- Keep `WithAutomaticReconnect()` and `IAsyncDisposable`.

#### 2. Hub integration test wiring

**File**: `src/PlanDeck/Tests/PlanDeck.Integration.Tests/PlanDeck.Integration.Tests.csproj`, `src/PlanDeck/Web/PlanDeck.Server/Program.cs`

**Intent**: Enable a real `HubConnection` against an in-memory test server.

**Contract**: Add `PackageReference` to `Microsoft.AspNetCore.Mvc.Testing` and `Microsoft.AspNetCore.SignalR.Client`, and a `ProjectReference` to `PlanDeck.Server`. Add `public partial class Program;` at the end of `Program.cs` so `WebApplicationFactory<Program>` can target the host. The factory runs with `Authentication:UseTestScheme=true` (default test config) → authenticated principal with fixed `tid`/`oid`.

#### 3. Hub integration test

**File**: `src/PlanDeck/Tests/PlanDeck.Integration.Tests/Realtime/PlanningRoomHubTests.cs` (new)

**Intent**: Prove the connect → cast → disconnect → reconnect → reveal lifecycle and that vote values never cross the wire before reveal — the single-identity transport scenario (multi-participant integrity is already covered by Phase 1 unit tests, since the test scheme is one fixed user).

**Contract**: Using `WebApplicationFactory<Program>` and `new HubConnectionBuilder().WithUrl(testServerUrl + "/hubs/planning-room", o => o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler())`: connect, `JoinRoom`, `CastVote`, capture each `RoomStateChanged` payload and assert `Vote` is null on all participants while `IsRevealed == false`; stop the connection (or simulate drop) and reconnect with a fresh `HubConnection` for the same identity, assert the participant is present with its vote preserved and back online after rejoin; `RevealVotes` and assert the vote value now appears. Keep the `typeof(PlanningRoomHub)` `[Authorize]` assertion as a structural guard; Phase 6 adds the behavioral anonymous-negotiate rejection test.

### Success Criteria

#### Automated Verification

- Solution builds: `dotnet build PlanDeck.slnx`
- Unit tests pass: `dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj`
- Hub integration test passes: `dotnet test Tests/PlanDeck.Integration.Tests/PlanDeck.Integration.Tests.csproj --filter "FullyQualifiedName~PlanningRoomHubTests"`

#### Manual Verification

- Local smoke: run the app, open two authenticated browser tabs against the hub (via dev-tools/SignalR console or a throwaway snippet), cast a vote in one, kill its network briefly; on reconnect the participant reappears online with the vote intact and still hidden until reveal.
- Confirm no `.razor` regressions — the client service change is source-compatible with the (currently zero) callers.

**Implementation Note**: Aspire-based integration tests require Podman running; the `PlanningRoomHubTests` (`WebApplicationFactory`) does not boot Aspire, but it lives in the same project — run the filtered command above to avoid the Aspire fixture. Pause for final manual confirmation.

---

## Phase 4: Monotonic room-state delivery

### Overview

Prevent a client from applying an older `RoomStateChanged` snapshot after a newer one when concurrent hub invocations complete their `SendAsync` calls out of order. Preserve the existing per-room mutation lock, but make snapshot order explicit and enforce it at the client boundary.

### Changes Required

#### 1. Version the wire state

**File**: `src/PlanDeck/Core/PlanDeck.Core.Shared/Realtime/PlanningRoomState.cs`

**Intent**: Give every emitted room snapshot an ordering token that survives concurrent transport sends.

**Contract**: Append `long Revision` to `PlanningRoomState`. The revision is scoped to one in-memory room instance, starts at zero, and never decreases while that room instance exists.

#### 2. Assign revisions under the room lock

**File**: `src/PlanDeck/Core/PlanDeck.Application/Planning/PlanningRoomService.cs`

**Intent**: Make revision order identical to authoritative mutation order.

**Contract**: Increment the room revision inside the existing per-room lock whenever an operation changes observable room state, before calling `ToState`. Idempotent no-ops may return the current revision without incrementing it. `GetState` returns the current revision. Add unit coverage proving that concurrent successful mutations produce unique, increasing revisions and that no returned snapshot has a revision lower than the authoritative state already observed.

#### 3. Reject stale snapshots in the client

**File**: `src/PlanDeck/Web/PlanDeck.Client/Services/PlanningRoomClientService.cs`

**Intent**: Ensure consumers of `RoomStateChanged` observe monotonic state even if SignalR delivers concurrent sends out of order.

**Contract**: Track the greatest applied revision for the currently joined session and raise the public `RoomStateChanged` event only when the incoming revision is greater than or equal to that value. Reset the tracked baseline before joining a different session and when a fresh/reconnected transport re-joins, so a server restart with revisions starting from zero cannot permanently suppress valid state.

#### 4. Ordering regression tests

**File**: `src/PlanDeck/Tests/PlanDeck.Unit.Tests/Planning/PlanningRoomServiceTests.cs`, `src/PlanDeck/Tests/PlanDeck.Unit.Tests/Realtime/PlanningRoomStateOrderingTests.cs` (new)

**Intent**: Prove both halves of the ordering contract deterministically without relying on a race reproducing during a test run.

**Contract**: Add a service test for revision assignment under concurrent mutations. Extract the client's revision-acceptance rule into a small testable helper and feed it synthetic snapshots in revision order `2, 1, 3`; assert that consumers observe only `2, 3`. Add the minimal project reference needed for the unit test only if the existing test project cannot access the client helper.

### Success Criteria

#### Automated Verification

- Solution builds: `dotnet build PlanDeck.slnx`
- Unit tests pass: `dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj`
- Ordering regression proves synthetic revisions `2, 1, 3` are observed as `2, 3`.

#### Manual Verification

- Reading every `RoomStateChanged` consumer confirms stale snapshots cannot bypass the revision gate.

**Implementation Note**: Do not hold the room lock across `SendAsync`; transport latency must not block room mutations. The revision gate makes ordering explicit without coupling the application service to SignalR.

---

## Phase 5: Single-room connection ownership

### Overview

Make the global connection map authoritative: one live `connectionId` can own membership in exactly one `(RoomKey, participantId)` pair. A second join for a different room or participant is rejected before SignalR group membership changes, preventing ghost-online participants and disconnect broadcasts to the wrong room.

### Changes Required

#### 1. Enforce atomic connection ownership

**File**: `src/PlanDeck/Core/PlanDeck.Application/Planning/PlanningRoomService.cs`

**Intent**: Keep `_connections` and each participant's connection set consistent.

**Contract**: `Join` atomically reserves `connectionId` with `TryAdd`. Rejoining the same `(RoomKey, participantId)` is idempotent; an existing different owner produces a clear domain error before mutating either room. Never overwrite an existing connection owner with the indexer. If room mutation fails after a new reservation, remove that reservation before propagating the error.

#### 2. Keep hub group membership transactional

**File**: `src/PlanDeck/Web/PlanDeck.Server/Hubs/PlanningRoomHub.cs`

**Intent**: Do not add a rejected connection to a second SignalR group or leave service ownership behind when group registration fails.

**Contract**: Call the ownership-enforcing service join before adding the connection to the SignalR group. If `Groups.AddToGroupAsync` fails, compensate by removing that connection from the room through the service disconnect/leave path, then propagate the failure. Broadcast only after both service ownership and group membership succeed.

#### 3. Make client room switching explicit

**File**: `src/PlanDeck/Web/PlanDeck.Client/Services/PlanningRoomClientService.cs`

**Intent**: Preserve normal navigation between rooms without relying on server-side implicit transfer.

**Contract**: When `JoinRoomAsync` targets a different session than `_joinedSessionId`, invoke `LeaveRoom` for the previous session first. Set `_joinedSessionId` only after the new join succeeds; a rejected join must not corrupt reconnect state.

#### 4. Ownership regression tests

**File**: `src/PlanDeck/Tests/PlanDeck.Unit.Tests/Planning/PlanningRoomServiceTests.cs`, `src/PlanDeck/Tests/PlanDeck.Integration.Tests/Realtime/PlanningRoomHubTests.cs`

**Intent**: Prove that a connection cannot silently move between rooms and that disconnect updates its sole owner.

**Contract**: Add a service test for join(A) → attempted join(B) → disconnect, asserting that B was never mutated and A becomes offline. Add a hub integration test asserting that a second-room join is rejected and does not subscribe the connection to B's SignalR group.

### Success Criteria

#### Automated Verification

- Solution builds: `dotnet build PlanDeck.slnx`
- Unit tests pass: `dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj`
- Focused hub integration tests pass: `dotnet test Tests/PlanDeck.Integration.Tests/PlanDeck.Integration.Tests.csproj --filter "FullyQualifiedName~PlanningRoomHubTests"`

#### Manual Verification

- Reviewing `Join`, `JoinRoom`, and `JoinRoomAsync` confirms one connection cannot be retained by two rooms and reconnect remembers only a successfully joined room.

**Implementation Note**: Do not implement implicit server-side transfer in `Join`; it would require broadcasting the old and new room states from one service call. The client performs an explicit leave-then-join transition instead.

---

## Phase 6: Active persisted-session validation

### Overview

Require `JoinRoom` to resolve an existing Active planning session within the authenticated tenant before creating in-memory room state or adding the connection to a SignalR group. This validates real session identity without taking on assigned-member or guest-link authorization.

### Changes Required

#### 1. Bridge the hub principal into tenant-scoped data access

**File**: `src/PlanDeck/Web/PlanDeck.Server/Identity/RequestPrincipalAccessor.cs` (new), `HttpContextCurrentUserContext.cs`, `Extensions/ServiceCollectionExtensions.cs`

**Intent**: Let application services called during a hub invocation reuse the existing `ICurrentUserContext` and EF tenant query filters without depending on an ambient `HttpContext`.

**Contract**: Register a scoped `RequestPrincipalAccessor` holding a `ClaimsPrincipal?`. Update `HttpContextCurrentUserContext` to prefer its explicitly supplied principal and fall back to `IHttpContextAccessor.HttpContext?.User` for HTTP/gRPC requests. The hub sets the accessor from `Context.User` before invoking tenant-scoped application logic. An absent or invalid tenant remains an explicit failure; never fall back to `Guid.Empty`.

#### 2. Add the active-session validation use case

**File**: `src/PlanDeck/Core/PlanDeck.Application/Planning/IActivePlanningSessionValidator.cs` (new), `ActivePlanningSessionValidator.cs` (new)

**Intent**: Keep persistence and status rules out of the SignalR hub.

**Contract**: Add `Task<bool> ExistsActiveAsync(Guid sessionId, CancellationToken cancellationToken)`. The implementation loads the session through the existing tenant-scoped `ISessionRepository` and returns true only when it exists and `Status == SessionStatus.Active`. It performs no membership or role check.

#### 3. Validate before joining the room

**File**: `src/PlanDeck/Web/PlanDeck.Server/Hubs/PlanningRoomHub.cs`

**Intent**: Prevent arbitrary or stale Guid values from creating authoritative in-memory rooms.

**Contract**: In `JoinRoom`, set the scoped principal accessor, build the `RoomKey`, and call `ExistsActiveAsync` before service ownership reservation or `Groups.AddToGroupAsync`. Throw a clear `HubException` when the session is missing or inactive. Other room operations still require prior successful join through the existing service rules.

#### 4. Validation tests

**File**: `src/PlanDeck/Tests/PlanDeck.Unit.Tests/Planning/ActivePlanningSessionValidatorTests.cs` (new), `src/PlanDeck/Tests/PlanDeck.Integration.Tests/Realtime/PlanningRoomHubTests.cs`

**Intent**: Prove tenant-scoped existence/status validation at the application and transport boundaries.

**Contract**: Unit-test missing, inactive, and active sessions. In the hub integration fixture, seed an Active session in the authenticated tenant and use it for the positive lifecycle; assert that an unknown Guid and an inactive session are rejected before any `RoomStateChanged` broadcast.

#### 5. Behavioral anonymous-connection test

**File**: `src/PlanDeck/Tests/PlanDeck.Integration.Tests/Realtime/PlanningRoomHubTests.cs`

**Intent**: Prove that endpoint mapping, authentication middleware, authorization middleware, and the hub attribute jointly reject an unauthenticated transport connection.

**Contract**: Create a dedicated `WebApplicationFactory` configuration that replaces the always-successful test authentication scheme with a test handler returning `AuthenticateResult.NoResult()` (or an equivalent unauthenticated result). Build a `HubConnection` against that server and assert `StartAsync`/negotiate is rejected with 401 or 403. Do not treat reflection over `[Authorize]` as the behavioral oracle.

### Success Criteria

#### Automated Verification

- Solution builds: `dotnet build PlanDeck.slnx`
- Unit tests pass: `dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj`
- Focused hub integration tests pass: `dotnet test Tests/PlanDeck.Integration.Tests/PlanDeck.Integration.Tests.csproj --filter "FullyQualifiedName~PlanningRoomHubTests"`
- Anonymous hub connection is rejected by the real test-server pipeline.

#### Manual Verification

- Reviewing `PlanningRoomHub` confirms no repository is injected directly and no room/group mutation occurs before active-session validation succeeds.

**Implementation Note**: This phase validates existence and Active status only. Assigned-member, creator, guest-link, facilitator, and moderator authorization remain owned by S-06/S-07.

---

## Testing Strategy

### Unit Tests (`PlanDeck.Unit.Tests/Planning/PlanningRoomServiceTests.cs`)

- Hidden-until-reveal; reveal idempotency + partial turnout; cast-after-reveal rejected.
- Vote change before reveal (last-write, no duplicate); reset clears + re-hides.
- Join idempotency per `oid`; disconnect keeps vote, online→offline only on last connection; reconnect restores online + vote.
- Explicit leave removes participant; tenant isolation for identical `SessionId`.
- Concurrency: N parallel casts → all land, no lost/duplicated entries.
- Revision assignment follows authoritative mutation order under concurrency.
- One connection cannot join two rooms; a rejected second join leaves the original room as the sole disconnect owner.

### Integration Tests (`PlanDeck.Integration.Tests/Realtime/PlanningRoomHubTests.cs`)

- End-to-end connect → cast → disconnect → reconnect → reveal over a real `HubConnection`.
- Hidden-on-the-wire assertion on every `RoomStateChanged` payload before reveal.
- Unauthenticated connection rejected.
- Client ordering regression feeds revisions `2, 1, 3` and exposes only `2, 3`.
- Second-room join is rejected without subscribing the connection to the second SignalR group.
- Unknown and inactive session IDs are rejected before room/group mutation; an Active tenant-scoped session can join.

### Manual Testing Steps

1. Run `dotnet run --project Aspire/PlanDeck.AppHost` (Podman up).
2. Open two authenticated connections to `/hubs/planning-room`; join the same session id.
3. Cast a vote in connection A; confirm B sees A as "voted" but no value.
4. Drop A's network briefly; confirm B sees A go offline, vote retained.
5. Restore A; confirm it auto-rejoins, online, vote intact.
6. Reveal; confirm both values appear together.

## Performance Considerations

In-memory, lock-per-room mutations are O(participants) for state projection and well within MVP scale (a planning team). The global connection map is O(1) per connect/disconnect. No persistence or network I/O on the hot path. Single-replica only; multi-replica needs a backplane (out of scope, documented trigger in `infrastructure.md:98`).

## Migration Notes

None — no schema or data changes. Wire-contract change (`PlanningParticipantState.IsOnline`, trimmed hub/client method signatures) is internal; the only consumer of the client service is not yet wired to a page, so there is no deployed client to break.

## References

- Roadmap slice: `context/foundation/roadmap.md` → F-02 "Real-time vote-integrity baseline" (lines 85-96)
- Transport decision: `context/foundation/infrastructure.md:101,125`
- Existing spike: `src/PlanDeck/Core/PlanDeck.Application/Planning/PlanningRoomService.cs`, `src/PlanDeck/Web/PlanDeck.Server/Hubs/PlanningRoomHub.cs`, `src/PlanDeck/Web/PlanDeck.Client/Services/PlanningRoomClientService.cs`
- Identity/claims: `src/PlanDeck/Web/PlanDeck.Server/Identity/HttpContextCurrentUserContext.cs`, `TestAuthenticationHandler.cs`
- Consuming slice: S-06 `realtime-voting-round` (this contract's first user)

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles.

### Phase 1: Service-layer integrity contract + unit tests

#### Automated

- [x] 1.1 Solution builds: `dotnet build PlanDeck.slnx` — a2bac2e
- [x] 1.2 Unit tests pass: `dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj` — a2bac2e
- [x] 1.3 `PlanningRoomServiceTests` fixture present and green — a2bac2e

#### Manual

- [x] 1.4 Each "Desired End State" contract rule maps to a named test — a2bac2e
- [x] 1.5 Hub + client service compile against the new interface (mechanical re-wiring, no new behavior) — a2bac2e

### Phase 2: Hub lifecycle & authorization

#### Automated

- [x] 2.1 Solution builds: `dotnet build PlanDeck.slnx` — 17ffbf1
- [x] 2.2 Existing unit + persistence tests still pass — 17ffbf1

#### Manual

- [x] 2.3 Authenticated client completes negotiate + `JoinRoom`; hub carries `[Authorize]` (anonymous 401 is prod-OIDC-only, not reproducible under test scheme) — 17ffbf1
- [x] 2.4 Hub methods no longer accept client-supplied `participantId`/`displayName` — 17ffbf1

### Phase 3: Client reconnection + hub integration test

#### Automated

- [x] 3.1 Solution builds: `dotnet build PlanDeck.slnx` — 6b28768
- [x] 3.2 Unit tests pass — 6b28768
- [x] 3.3 Hub integration test passes: `dotnet test Tests/PlanDeck.Integration.Tests/PlanDeck.Integration.Tests.csproj --filter "FullyQualifiedName~PlanningRoomHubTests"` — 6b28768

#### Manual

- [x] 3.4 Local smoke: drop + reconnect preserves vote, stays hidden until reveal — 6b28768
- [x] 3.5 No `.razor` regressions from the client service change — 6b28768

### Phase 4: Monotonic room-state delivery

#### Automated

- [x] 4.1 Solution builds: `dotnet build PlanDeck.slnx`
- [x] 4.2 Unit tests pass: `dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj`
- [x] 4.3 Ordering regression proves synthetic revisions `2, 1, 3` are observed as `2, 3`

#### Manual

- [x] 4.4 Every `RoomStateChanged` consumer routes incoming snapshots through the revision gate

### Phase 5: Single-room connection ownership

#### Automated

- [ ] 5.1 Solution builds: `dotnet build PlanDeck.slnx`
- [ ] 5.2 Unit tests pass: `dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj`
- [ ] 5.3 Focused hub integration tests pass: `dotnet test Tests/PlanDeck.Integration.Tests/PlanDeck.Integration.Tests.csproj --filter "FullyQualifiedName~PlanningRoomHubTests"`

#### Manual

- [ ] 5.4 `Join`, `JoinRoom`, and `JoinRoomAsync` enforce one room per connection and retain only successful reconnect state

### Phase 6: Active persisted-session validation

#### Automated

- [ ] 6.1 Solution builds: `dotnet build PlanDeck.slnx`
- [ ] 6.2 Unit tests pass: `dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj`
- [ ] 6.3 Focused hub integration tests pass: `dotnet test Tests/PlanDeck.Integration.Tests/PlanDeck.Integration.Tests.csproj --filter "FullyQualifiedName~PlanningRoomHubTests"`
- [ ] 6.4 Anonymous hub connection is rejected by the real test-server pipeline

#### Manual

- [ ] 6.5 Hub performs no room/group mutation before tenant-scoped Active-session validation succeeds
