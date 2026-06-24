# Guest-link Voting (S-07) Implementation Plan

## Overview

Let a person **without an account** join an active planning-poker session's vote through a share link of the form `/join/{code}`, providing only a temporary username, and vote like any participant. Guests are **vote-only**: they may cast and change votes and see room state, but cannot drive the round (reveal, reset, set active task, select estimate). The feature reuses the existing F-02 real-time vote-integrity contract and the S-04/S-06 session + voting-room machinery; the only genuinely new surface is the **share code**, the **guest credential**, and the **per-session isolation** that keeps a guest confined to exactly one session.

## Current State Analysis

- **Hub identity is Entra-only.** `PlanningRoomHub` is `[Authorize]` and derives everything from claims: `ParticipantId = "oid"`, tenant `= "tid"`, `RoomKey = (TenantId, SessionId)`, display name `= "name"` (`PlanningRoomHub.cs:135-194`). A guest has none of these.
- **Tenant isolation is a global EF query filter** `e.TenantId == CurrentTenantId`, where `CurrentTenantId` comes from `ICurrentUserContext.TenantId` (the `"tid"` claim) — `PlanDeckDbContext.cs:59-63`. With no `tid`, the tenant resolves to `Guid.Empty` and **every tenant-scoped query returns nothing**, so a guest cannot read any session through the normal repository path. Resolving a session by share code must therefore use `IgnoreQueryFilters()`.
- **Participant authorization** is creator-or-member-by-email (`VotingRoundService.cs:43-58`). A guest matches neither path; a third, code-based path is required.
- **No share-code field** exists on `PlanningSession` (`PlanningSession.cs`); `Sessions` table config has no code column/index (`PlanningSessionConfiguration.cs`).
- **The hub trusts the principal for the DI scope** via `principalAccessor.Principal = Context.User` (`PlanningRoomHub.cs:18,120`), which feeds `ICurrentUserContext` → tenant-scoped repositories. A synthesized guest principal carrying `tid = session.TenantId` makes the existing tenant-scoped reads "just work" inside the guest's connection.
- **The WASM client does not currently know its own `oid` or any guest flag.** `IAuthService.GetCurrentUserAsync` returns only `IsAuthenticated/DisplayName/Email` (`AuthGrpcService.cs`, `IAuthService.cs`), and `GrpcAuthenticationStateProvider` adds only `Name`+`Email` claims. As a result `VotingRoom._myParticipantId = authState.User.FindFirst("oid")` is already `null` today (`VotingRoom.razor.cs:40`) — a latent gap this change must close to render guest (and member) self-state correctly.
- **SignalR carries cookies automatically** for the same-origin `/hubs/planning-room` connection (`PlanningRoomClientService.cs:9-12`), so a guest cookie set before the connection starts is transmitted with no transport change.
- **Auth wiring**: cookie default scheme + optional OIDC challenge, configured in `ServiceCollectionExtensions.AddExternalServices` (`:83-108`); a Testing-only `TestAuthenticationHandler` scheme exists (`:63-74`). Login/logout are minimal-API endpoints in `Program.cs:66-86`.
- **Code is generated nowhere yet**; `ActivateSessionAsync` (`SessionGrpcService.cs`) flips `Status` to `Active` and is the natural place to mint a code.

### Key Discoveries:

- Tenant filter forces a bypass-on-read for code resolution: `db.Sessions.IgnoreQueryFilters().FirstOrDefault(s => s.ShareCode == code)` — `PlanDeckDbContext.cs:62`.
- A guest principal with claims `tid=session.TenantId, oid=<new guid>, name=<temp name>, sid=<sessionId>, is_guest=true` satisfies every existing hub accessor unchanged (`PlanningRoomHub.cs:135-205`).
- `IPlanningRoomService.Join(key, participantId, displayName, connectionId)` already accepts an arbitrary string `participantId` and `displayName` (`IPlanningRoomService.cs`), so guests need **no** new in-memory model — votes live in `PlanningRoomService` keyed by the guest's synthesized `oid`.
- `SaveChanges` throws if `CurrentTenantId == Guid.Empty` (`PlanDeckDbContext.cs:104-110`); the redeem path must be **read-only** (no writes) when run under the anonymous/empty-tenant context.
- `[Authorize(AuthenticationSchemes = "Cookies,Guest")]` lets the hub admit both members and guests while still rejecting the fully anonymous.

## Desired End State

- An organizer activates a session; the session gets a stable, URL-safe `ShareCode`. The sessions UI shows a copyable join link `…/join/{code}` for every active session.
- A guest opens `/join/{code}`, types a name, and is dropped into the voting room as a participant. They can cast/observe votes in real time. Control affordances are absent for them; control hub methods reject them server-side.
- A guest credential is bound to exactly one session: any attempt (crafted client, replayed cookie) to act on a different `sessionId` is rejected by the hub.
- Refresh / brief disconnect auto-rejoins the guest with the same identity and name (cookie persists `oid`+`sid`+`name`).
- If the code is unknown or the session is not `Active`, the guest sees a clear error page and is not signed in.
- Verification: `dotnet build PlanDeck.slnx` succeeds; unit + integration tests for code resolution, guest authz/sid-scoping, tenant-bypass read, and name validation pass; a Playwright E2E drives link → name → vote → organizer reveal.

## What We're NOT Doing

- No persisted guest rows (no `SessionGuest`/`SessionMember` for guests) — guests are in-memory only.
- No guest re-identification across sessions, no guest history, no profiles.
- No code rotation / regeneration UI, no per-session "enable guest access" toggle, no TTL independent of session status (code lives as long as the session).
- No content/profanity filtering or uniqueness enforcement on the temporary username.
- No new control permissions for guests; no configurable guest permission matrix.
- No change to the member (Entra) voting path beyond the shared `CurrentUserReply` extension.
- No QR codes, email invites, or notification surfaces.

## Implementation Approach

Layered, inward-flowing per the repo conventions. Phase 1 establishes the data (code + resolution) with no behavior change to existing flows. Phase 2 adds the guest credential + anonymous redeem endpoint on the server. Phase 3 teaches the hub to admit and confine guests and to enforce vote-only. Phase 4 builds the client guest journey and the organizer share-link surface, and closes the `oid`/`is_guest` client-identity gap. Phase 5 adds E2E coverage and the guest-facing error pages. Each phase is independently buildable and testable; guest voting becomes reachable end-to-end only after Phase 4.

## Critical Implementation Details

- **Read-only redeem under empty tenant.** The `/guest/join` resolution runs before any tenant is known. It must only *read* (via `IgnoreQueryFilters`) and must not trigger `SaveChanges`, which throws on an empty `CurrentTenantId` (`PlanDeckDbContext.cs:104-123`). Sign-in writes a cookie, not a DB row.
- **Tenant is established from the session row, not the caller.** The guest principal's `tid` claim is set to the resolved `session.TenantId`. Inside the hub scope this flows through `ICurrentUserContext` so the normal tenant-scoped seed read (`AuthorizeAndLoadSeedAsync`/seed load) returns the correct session's tasks — without ever trusting a guest-supplied tenant.
- **`sid` must be checked on every hub method, not just join.** A guest cookie is a bearer credential for one `sessionId`; `BuildKey`/authorization must reject any call whose `sessionId` argument ≠ the `sid` claim, closing cross-session reach.
- **Vote-only is enforced server-side.** Hiding buttons in the client is cosmetic; `RevealVotes`, `ResetRound`, `SetActiveTask`, `SelectEstimate` must throw `HubException` when the caller carries `is_guest=true`.
- **Re-validate `Active` at guest join.** The code only exists on active sessions, but a session can deactivate/delete between link issuance and join; guest `JoinRoom` must reject when the session is no longer `Active`.

## Phase 1: Domain & persistence — share code

### Overview

Add the share code to the session domain, generate it on activation, expose it in the DTO, and provide a tenant-bypassing resolution + active-seed load. No existing behavior changes.

### Changes Required:

#### 1. Session domain entity

**File**: `src/PlanDeck/Core/PlanDeck.Application/Domain/PlanningSession.cs`

**Intent**: Add a nullable `ShareCode` that is populated when the session becomes active and is null while Draft.

**Contract**: New property `string? ShareCode { get; set; }` on `PlanningSession`.

#### 2. EF Core configuration + migration

**File**: `src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/Configurations/PlanningSessionConfiguration.cs`

**Intent**: Map `ShareCode` with a bounded length and a **filtered unique index** (unique across non-null values) so codes never collide and lookups are indexed.

**Contract**: `ShareCode` column (max length ~16); `HasIndex(s => s.ShareCode).IsUnique()` with a filter excluding nulls (SQL Server filtered index). Then generate a migration into `PlanDeck.Infrastructure/Persistence/Migrations/` via `dotnet ef migrations add AddSessionShareCode` and confirm it applies on startup (Development) / via `ApplyMigrationsAsync`.

#### 3. Share-code generator

**File**: `src/PlanDeck/Core/PlanDeck.Application/Planning/ShareCodeGenerator.cs` (new) + interface `IShareCodeGenerator`

**Intent**: Produce a cryptographically-random, URL-safe, human-readable code (~10 chars, Crockford-style base32, ambiguous characters excluded).

**Contract**: `IShareCodeGenerator.Generate() => string`. Implementation uses `RandomNumberGenerator`. Registered in `AddLocalServices` (`ServiceCollectionExtensions.cs:117-135`).

#### 4. Generate code on activation

**File**: `src/PlanDeck/Core/PlanDeck.Application/Services/SessionGrpcService.cs` (`ActivateSessionAsync`)

**Intent**: On the first activation, assign a `ShareCode` (if still null) before persisting; collisions on the unique index are retried with a fresh code.

**Contract**: In `ActivateSessionAsync`, when transitioning to `Active`, set `session.ShareCode ??= generator.Generate()`; on `DbUpdateException` from the unique index, regenerate and retry (small bounded loop). Inject `IShareCodeGenerator`.

#### 5. Expose code in DTO

**File**: `src/PlanDeck/Core/PlanDeck.Core.Shared/Contracts/ISessionService.cs` (`SessionDto`) and `SessionGrpcService.ToDto`

**Intent**: Surface `ShareCode` so the organizer UI can render the join link.

**Contract**: `SessionDto.ShareCode` as `[DataMember(Order = 9)] string? ShareCode`; map it in `ToDto`.

#### 6. Repository: resolve by code + load active seed

**File**: `src/PlanDeck/Core/PlanDeck.Application/Abstractions/ISessionRepository.cs` and `src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/SessionRepository.cs`

**Intent**: Provide a tenant-agnostic lookup of an active session by share code (for redeem) returning the minimal data needed to mint the guest principal.

**Contract**: New method e.g. `Task<GuestSessionRef?> GetActiveSessionByShareCodeAsync(string shareCode, CancellationToken ct)` returning `(SessionId, TenantId)` (or null when not found / not Active). Implemented with `db.Sessions.IgnoreQueryFilters().AsNoTracking().FirstOrDefault(s => s.ShareCode == code && s.Status == Active)`. Read-only; performs no `SaveChanges`.

### Success Criteria:

#### Automated Verification:

- Build passes: `dotnet build PlanDeck.slnx`
- Migration applies cleanly on startup / `dotnet ef database update` against the dev DB
- Unit tests pass for `ShareCodeGenerator` (charset, length, uniqueness over many draws): `dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj`
- Unit/integration test: activation assigns a non-null unique `ShareCode`; re-activation does not change it
- Integration test: `GetActiveSessionByShareCodeAsync` returns a Draft session as null and an Active session correctly, ignoring tenant filter

#### Manual Verification:

- Activating a session in the UI yields a code; the column is populated in the DB
- A Draft session has a null code

**Implementation Note**: After completing this phase and all automated verification passes, pause for human confirmation of the manual checks before proceeding.

---

## Phase 2: Guest credential & anonymous redeem endpoint

### Overview

Introduce a dedicated `Guest` cookie authentication scheme and an anonymous endpoint that exchanges a `{code, displayName}` for a session-scoped guest cookie. Extend the current-user context to expose participant id and guest flag.

### Changes Required:

#### 1. Guest cookie scheme

**File**: `src/PlanDeck/Web/PlanDeck.Server/Extensions/ServiceCollectionExtensions.cs` (`AddExternalServices`)

**Intent**: Register a second cookie scheme `"Guest"` distinct from the member cookie, so guest sign-in cannot be mistaken for an Entra session and can be challenged/authorized independently.

**Contract**: `authenticationBuilder.AddCookie("Guest", o => { o.Cookie.Name = "PlanDeck.Guest"; o.Cookie.SameSite = Lax; o.Cookie.HttpOnly = true; o.Cookie.SecurePolicy = Always; /* no redirect on 401 — return 401/JSON */ })`. Applied in both the test-scheme and normal branches as appropriate (guest scheme is independent of OIDC config).

#### 2. Anonymous redeem endpoint

**File**: `src/PlanDeck/Web/PlanDeck.Server/Program.cs` (new minimal-API endpoint) — keep logic thin; resolution lives in Application/Infrastructure.

**Intent**: `POST /guest/join` (anonymous) validates the temp name, resolves the active session by code (Phase 1 repo method), and on success signs in a guest `ClaimsPrincipal` into the `Guest` scheme, returning the `sessionId`. Unknown code / inactive session → 404/409 (no sign-in).

**Contract**: Request `{ code, displayName }`. Name normalization: trim, require length 1–40 (reject empty/too-long). On success: claims `oid = Guid.NewGuid()`, `tid = session.TenantId`, `name = <normalized>`, `sid = session.Id`, `is_guest = "true"`; `HttpContext.SignInAsync("Guest", principal, new AuthenticationProperties { IsPersistent = true })`; respond `{ sessionId }`. Endpoint is `.AllowAnonymous()` and does not require antiforgery beyond standard same-origin. No DB writes.

#### 3. Current-user context: participant id + guest flag

**File**: `src/PlanDeck/Core/PlanDeck.Application/Abstractions/ICurrentUserContext.cs` and `src/PlanDeck/Web/PlanDeck.Server/Identity/HttpContextCurrentUserContext.cs`

**Intent**: Expose the participant id (`oid`) and an `IsGuest` flag so both the gRPC auth surface (Phase 4) and any server logic can reason about guests uniformly.

**Contract**: Add `string? ParticipantId { get; }` (reads `oid`) and `bool IsGuest { get; }` (reads `is_guest == "true"`) to `ICurrentUserContext`; implement in `HttpContextCurrentUserContext` reading from the same principal source already used (works for both HTTP and the hub's `RequestPrincipalAccessor`).

### Success Criteria:

#### Automated Verification:

- Build passes: `dotnet build PlanDeck.slnx`
- Integration test: `POST /guest/join` with a valid active code sets a `PlanDeck.Guest` cookie and returns the session id
- Integration test: unknown code → 404, no cookie; Draft/ended session code → 409, no cookie
- Integration test: empty / >40-char name → 400, no cookie; name is trimmed in the issued `name` claim
- Unit test: a guest principal resolves `ICurrentUserContext.IsGuest == true` and `ParticipantId == oid`

#### Manual Verification:

- Hitting `/guest/join` with a real active code in the browser sets the guest cookie (visible in dev tools) and the response carries the session id

**Implementation Note**: Pause for human confirmation of manual checks before proceeding.

---

## Phase 3: Hub guest support, isolation & vote-only enforcement

### Overview

Teach `PlanningRoomHub` to admit guests (both cookie schemes), confine each guest to its `sid` session on every method, load the room seed for guests without the member check, and reject all control actions from guests.

### Changes Required:

#### 1. Accept both auth schemes

**File**: `src/PlanDeck/Web/PlanDeck.Server/Hubs/PlanningRoomHub.cs`

**Intent**: Allow members and guests to connect while still rejecting the fully anonymous.

**Contract**: Change `[Authorize]` → `[Authorize(AuthenticationSchemes = $"{CookieAuthenticationDefaults.AuthenticationScheme},Guest")]` (and include the test scheme name when running under the Testing configuration, mirroring how the app already toggles schemes).

#### 2. Per-session `sid` confinement

**File**: `src/PlanDeck/Web/PlanDeck.Server/Hubs/PlanningRoomHub.cs` (`BuildKey` / new guard)

**Intent**: For guest callers, require the requested `sessionId` to equal the `sid` claim on every invocation; reject otherwise.

**Contract**: A `private void EnsureSessionScope(Guid sessionId)` invoked by `BuildKey` (or at the top of each public method): when `IsGuest`, parse `sid` and throw `HubException` if `sid != sessionId`. Members are unaffected.

#### 3. Guest-aware authorization + seed load on join

**File**: `src/PlanDeck/Web/PlanDeck.Server/Hubs/PlanningRoomHub.cs` (`JoinRoom`) and `src/PlanDeck/Core/PlanDeck.Application/Planning/VotingRoundService.cs` (+ `IVotingRoundService`)

**Intent**: Guests are authorized by the validated guest cookie + `sid` match (not by member/creator lookup), but still need the room seed and a fresh `Active` check. Provide a seed-only load that does not perform member authorization.

**Contract**: Add `IVotingRoundService.LoadActiveSessionSeedAsync(Guid sessionId, CancellationToken ct) -> RoomSeed?` returning the seed only when the session exists and is `Active` (tenant-scoped read; the guest's `tid` claim makes this resolve to the right tenant). In `JoinRoom`: branch on `IsGuest` — guests call `EnsureSessionScope` + `LoadActiveSessionSeedAsync`; members keep `AuthorizeAndLoadSeedAsync`. Null seed → `HubException` (not active / missing).

#### 4. Vote-only enforcement

**File**: `src/PlanDeck/Web/PlanDeck.Server/Hubs/PlanningRoomHub.cs`

**Intent**: Reject round-control actions from guests at the hub boundary.

**Contract**: A `private void EnsureNotGuest()` (throws `HubException` when `IsGuest`) called at the start of `RevealVotes`, `ResetRound`, `SetActiveTask`, and `SelectEstimate`. `CastVote`, `JoinRoom`, `LeaveRoom` remain available to guests (subject to `EnsureSessionScope`). `IsGuest`/`sid` read via the same claim helpers already in the hub.

#### 5. CastVote authorization for guests

**File**: `src/PlanDeck/Web/PlanDeck.Server/Hubs/PlanningRoomHub.cs` (`AuthorizeAsync`)

**Intent**: `AuthorizeAsync` currently calls member-only `IsAuthorizedParticipantAsync`; guests must pass via `sid` confinement instead of the member check.

**Contract**: In `AuthorizeAsync`, when `IsGuest`, authorize via `EnsureSessionScope(sessionId)` and skip the member lookup; otherwise keep the existing member/creator check.

### Success Criteria:

#### Automated Verification:

- Build passes: `dotnet build PlanDeck.slnx`
- Unit tests: a guest principal whose `sid` ≠ requested `sessionId` is rejected by the scope guard
- Unit tests: guest calls to `RevealVotes`/`ResetRound`/`SetActiveTask`/`SelectEstimate` throw; `CastVote` succeeds
- Unit/integration test: `LoadActiveSessionSeedAsync` returns null for a non-active session and a seed for an active one
- Integration test: a guest join to an active session yields room state including the guest participant

#### Manual Verification:

- With two browsers (one member, one guest) the guest's vote appears live and is hidden until the member reveals
- A guest cannot trigger reveal/reset (no UI affordance and a forced hub call is rejected)

**Implementation Note**: Pause for human confirmation of manual checks before proceeding.

---

## Phase 4: Client — guest journey, identity, organizer share link

### Overview

Close the client identity gap (`oid`/`is_guest`), build the `/join/{code}` guest landing page, render the voting room vote-only for guests, and add the organizer's copyable share link.

### Changes Required:

#### 1. Extend current-user contract

**File**: `src/PlanDeck/Core/PlanDeck.Core.Shared/Contracts/IAuthService.cs` (`CurrentUserReply`) and `src/PlanDeck/Core/PlanDeck.Application/Services/AuthGrpcService.cs`

**Intent**: Return the participant id and guest flag so the client can identify itself and adapt the UI; this also fixes the existing `_myParticipantId == null` gap for members.

**Contract**: `CurrentUserReply.ParticipantId` (`[DataMember(Order=4)] string?`) and `IsGuest` (`[DataMember(Order=5)] bool`); populate from `ICurrentUserContext.ParticipantId`/`IsGuest` in `AuthGrpcService`.

#### 2. Surface claims in the WASM auth state

**File**: `src/PlanDeck/Web/PlanDeck.Client/Services/GrpcAuthenticationStateProvider.cs`

**Intent**: Add `oid` and `is_guest` claims to the client principal so pages can read them.

**Contract**: When `reply.ParticipantId` is present add `new Claim("oid", reply.ParticipantId)`; when `reply.IsGuest` add `new Claim("is_guest", "true")`.

#### 3. Guest landing page `/join/{code}`

**File**: `src/PlanDeck/Web/PlanDeck.Client/Pages/JoinSession.razor` + `JoinSession.razor.cs` (new, namespace `PlanDeck.Client.Pages`, code-behind per convention)

**Intent**: Anonymous page that collects a temp username and posts to `/guest/join`; on success force-navigates to `/voting/{sessionId}` (full reload so the new guest cookie is picked up and auth state refreshes); on failure shows a localized error (unknown code / inactive session).

**Contract**: `@page "/join/{Code}"`; `[Parameter] string Code`. MudBlazor form (`MudTextField` name, max length 40, `MudButton`). Submit via injected `HttpClient` `POST /guest/join` with `{ code, displayName }`; read `{ sessionId }`; `Navigation.NavigateTo($"/voting/{sessionId}", forceLoad: true)`. Map non-2xx to error keys. The page itself does not require auth.

#### 4. Voting room: vote-only for guests

**File**: `src/PlanDeck/Web/PlanDeck.Client/Pages/VotingRoom.razor` + `VotingRoom.razor.cs`

**Intent**: Read the guest flag from auth state; hide reveal/reset/set-active/select-estimate affordances and the moderator controls for guests, leaving voting and live state intact. Set `_myParticipantId` from the now-present `oid` claim.

**Contract**: In `OnInitializedAsync`, set `_isGuest = authState.User.FindFirst("is_guest")?.Value == "true"`; gate control buttons in the markup on `!_isGuest`. No change to the connect/join flow (guest cookie already authenticates the hub).

#### 5. Organizer share link UI

**File**: `src/PlanDeck/Web/PlanDeck.Client/Pages/` — the sessions list/detail page that renders an active session (and `SessionDto.ShareCode` from Phase 1)

**Intent**: For each active session, show the join link `{origin}/join/{ShareCode}` with a copy-to-clipboard button so the organizer can share it.

**Contract**: Render link + `MudIconButton` copy (JS `navigator.clipboard` via existing interop pattern or `IJSRuntime`). Only shown when `Status == Active` and `ShareCode` is non-empty.

#### 6. Localization

**Files**: client resource files (`.resx`) for `en` and `pl` already used by the app (e.g. under `PlanDeck.Client` Resources)

**Intent**: Add localized strings for the join page (prompt, name label, join button, error messages) and the share-link UI (label, copy, copied toast).

**Contract**: New resource keys in both `en` and `pl`; no hard-coded display text.

### Success Criteria:

#### Automated Verification:

- Build passes: `dotnet build PlanDeck.slnx`
- Existing unit/integration tests still pass: `dotnet test PlanDeck.slnx`
- (If component tests exist) `_myParticipantId` is populated from `oid` for an authenticated member

#### Manual Verification:

- Organizer copies the join link from an active session
- Opening the link in a logged-out browser shows the name prompt; entering a name enters the room
- The guest can vote and see live state; control buttons are absent
- Member view is unchanged and still shows controls; member self-vote highlight now works
- `en`/`pl` strings render correctly

**Implementation Note**: Pause for human confirmation of manual checks before proceeding.

---

## Phase 5: E2E coverage & guest-facing error states

### Overview

Add a Playwright E2E for the full guest path and verify the error pages, completing the slice.

### Changes Required:

#### 1. Guest join page object + E2E

**Files**: `src/PlanDeck/Tests/PlanDeck.E2e.Tests/Pages/JoinSessionPage.cs` (new) and a test fixture under `PlanDeck.E2e.Tests`

**Intent**: Drive link → name → vote, with a second (member/organizer) context revealing, asserting the guest sees the revealed values. Follow the existing Page Object Pattern and WASM-boot waiting used by `HomePage`.

**Contract**: Page object wrapping the join form + voting controls; test derives from `PageTest`, overrides `ContextOptions` for `IgnoreHTTPSErrors`. Uses the local Aspire-started base URL (Testing config / test auth scheme for the organizer) or the `BaseUrl` run parameter, per the E2E setup convention.

#### 2. Error-state coverage

**Files**: same E2E project

**Intent**: Assert `/join/{unknownCode}` and a code for a non-active session render the localized error and do not enter the room.

**Contract**: Tests asserting the error UI is shown and no hub connection/room is reached.

### Success Criteria:

#### Automated Verification:

- Build passes: `dotnet build PlanDeck.slnx`
- E2E guest-flow test passes locally (Aspire + Podman running, Playwright chromium installed): `dotnet test Tests/PlanDeck.E2e.Tests/PlanDeck.E2e.Tests.csproj`
- Full suite green: `dotnet test PlanDeck.slnx`

#### Manual Verification:

- E2E run observed end-to-end; no flakiness across a couple of runs
- Error pages match copy in `en`/`pl`

**Implementation Note**: Final phase — confirm the whole guest journey manually once more before marking the change done.

---

## Testing Strategy

### Unit Tests:

- `ShareCodeGenerator`: charset/length, no ambiguous chars, statistical uniqueness over many draws.
- Guest principal → `ICurrentUserContext.IsGuest`/`ParticipantId`.
- Hub guards: `sid` mismatch rejected; control methods reject guests; `CastVote` allowed.
- Name normalization/validation (trim, 1–40, reject empty/oversized).

### Integration Tests:

- `GetActiveSessionByShareCodeAsync` ignores the tenant filter and respects `Active` status.
- `POST /guest/join`: success sets cookie + returns session id; unknown code / inactive / bad name rejected with no cookie and no DB write.
- Activation assigns a unique code; re-activation keeps it.

### Manual / E2E Testing Steps:

1. Activate a session as organizer; copy the join link.
2. In a logged-out browser, open the link, enter a name, join.
3. Cast a vote; confirm it appears live to the organizer and stays hidden until reveal.
4. Organizer reveals; guest sees values together.
5. Confirm the guest has no control buttons and a forced control call is rejected.
6. Refresh the guest tab; confirm auto-rejoin with the same identity/name.
7. Open `/join/{unknownCode}` and a code for a deactivated session; confirm error pages.

## Performance Considerations

- Share-code lookups are indexed (filtered unique index); redeem is a single `AsNoTracking` read.
- Guests add no persistence load; they reuse the existing in-memory room. No backplane is introduced (consistent with F-03's single-replica posture).

## Migration Notes

- One additive EF migration (`AddSessionShareCode`): nullable column + filtered unique index; safe on existing rows (all start null). Applied via `ApplyMigrationsAsync` in Development and the standard pipeline elsewhere.
- Existing active sessions created before this change will have a null code until re-activated; acceptable for the MVP (the UI shows the link only when a code exists).

## References

- Roadmap slice: `context/foundation/roadmap.md` (S-07, lines 190-201) and F-02 (lines 85-96).
- Hub & identity: `src/PlanDeck/Web/PlanDeck.Server/Hubs/PlanningRoomHub.cs:135-205`.
- Tenant filter: `src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/PlanDeckDbContext.cs:59-123`.
- Member authorization: `src/PlanDeck/Core/PlanDeck.Application/Planning/VotingRoundService.cs:43-58`.
- Client identity gap: `src/PlanDeck/Web/PlanDeck.Client/Services/GrpcAuthenticationStateProvider.cs`, `VotingRoom.razor.cs:40`.
- Auth wiring: `src/PlanDeck/Web/PlanDeck.Server/Extensions/ServiceCollectionExtensions.cs:83-108`, `Program.cs:66-86`.

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles.

### Phase 1: Domain & persistence — share code

#### Automated

- [x] 1.1 Build passes: `dotnet build PlanDeck.slnx` — 9818eea
- [x] 1.2 Migration applies cleanly on startup / `dotnet ef database update` — 9818eea
- [x] 1.3 `ShareCodeGenerator` unit tests pass (charset, length, uniqueness) — 9818eea
- [x] 1.4 Activation assigns a non-null unique `ShareCode`; re-activation does not change it — 9818eea
- [x] 1.5 `GetActiveSessionByShareCodeAsync` returns null for Draft, the session for Active, ignoring tenant filter — 9818eea

#### Manual

- [x] 1.6 Activating a session yields a code populated in the DB — 9818eea
- [x] 1.7 A Draft session has a null code — 9818eea

### Phase 2: Guest credential & anonymous redeem endpoint

#### Automated

- [x] 2.1 Build passes: `dotnet build PlanDeck.slnx` — 7b30384
- [x] 2.2 `POST /guest/join` with valid active code sets `PlanDeck.Guest` cookie and returns session id — 7b30384
- [x] 2.3 Unknown code → 404 no cookie; Draft/ended code → 409 no cookie — 7b30384
- [x] 2.4 Empty / >40-char name → 400 no cookie; name trimmed in the `name` claim — 7b30384
- [x] 2.5 Guest principal resolves `IsGuest == true` and `ParticipantId == oid` — 7b30384

#### Manual

- [x] 2.6 `/guest/join` with a real active code sets the guest cookie and returns the session id — 7b30384

### Phase 3: Hub guest support, isolation & vote-only enforcement

#### Automated

- [x] 3.1 Build passes: `dotnet build PlanDeck.slnx` — 6a314fa
- [x] 3.2 Guest with `sid` ≠ requested `sessionId` is rejected by the scope guard — 6a314fa
- [x] 3.3 Guest `RevealVotes`/`ResetRound`/`SetActiveTask`/`SelectEstimate` throw; `CastVote` succeeds — 6a314fa
- [x] 3.4 `LoadActiveSessionSeedAsync` returns null for non-active and a seed for active — 6a314fa
- [x] 3.5 Guest join to an active session yields room state including the guest participant — 6a314fa

#### Manual

- [ ] 3.6 Guest vote appears live and is hidden until member reveal (two browsers)
- [ ] 3.7 Guest cannot trigger reveal/reset (no UI affordance and forced hub call rejected)

### Phase 4: Client — guest journey, identity, organizer share link

#### Automated

- [x] 4.1 Build passes: `dotnet build PlanDeck.slnx`
- [x] 4.2 Full suite still green: `dotnet test PlanDeck.slnx`
- [x] 4.3 `_myParticipantId` populated from `oid` for an authenticated member (if component tests exist)

#### Manual

- [x] 4.4 Organizer copies the join link from an active session
- [x] 4.5 Logged-out link opens name prompt; entering a name enters the room
- [x] 4.6 Guest can vote and see live state; control buttons absent
- [x] 4.7 Member view unchanged with controls; member self-vote highlight works
- [x] 4.8 `en`/`pl` strings render correctly

### Phase 5: E2E coverage & guest-facing error states

#### Automated

- [ ] 5.1 Build passes: `dotnet build PlanDeck.slnx`
- [ ] 5.2 E2E guest-flow test passes locally (Aspire + Podman, Playwright chromium)
- [ ] 5.3 Full suite green: `dotnet test PlanDeck.slnx`

#### Manual

- [ ] 5.4 E2E run observed end-to-end; no flakiness across runs
- [ ] 5.5 Error pages match copy in `en`/`pl`
