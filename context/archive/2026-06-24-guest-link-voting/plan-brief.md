# Guest-link Voting (S-07) — Plan Brief

> Full plan: `context/changes/guest-link-voting/plan.md`

## What & Why

Let a person **without an account** join an active planning-poker session's vote through a share link (`/join/{code}`) using only a temporary username, and vote like any participant. Frictionless guest voting is a deliberate product differentiator (PRD FR-013, roadmap S-07) — and the path that most stresses tenant isolation, so isolation is the central design constraint.

## Starting Point

Real-time voting (F-02 + S-06) is built and identity-bound to Entra: the SignalR hub is `[Authorize]` and reads `oid`/`tid`/`name` claims; tenant isolation is a global EF query filter keyed off the `tid` claim. A guest has none of these, so today a guest cannot connect to the hub *or* read any session. There is no share code on a session, and the WASM client doesn't even surface its own `oid`/guest flag yet.

## Desired End State

An organizer activates a session and gets a copyable join link. A guest opens it, types a name, and lands in the voting room — able to cast/observe votes live, but with no round-control affordances. The guest credential is bound to exactly one session and cannot reach any other. Unknown/inactive codes show a clear error and don't sign anyone in.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Guest credential | Dedicated `Guest` cookie scheme with synthesized principal (`tid`/`oid`/`name`/`sid`/`is_guest`) | Makes every existing hub accessor and the tenant filter "just work" without trusting guest input | Plan |
| Code scope/expiry | Stable per-session code minted on activation; valid only while session is `Active` | Simple, no TTL machinery; code dies with the session | Plan |
| Enable guest access | On by default for active sessions (link always copyable) | Lowest friction; matches the differentiator goal | Plan |
| Guest permissions | Vote-only; Reveal/Reset/SetActiveTask/SelectEstimate reserved for logged-in users | Keeps round control with the organizer; enforced server-side | Plan |
| Guest persistence | In-memory only (no DB row) | Reuses existing in-memory room; guests are ephemeral | Plan |
| Reconnect identity | Persistent guest cookie → refresh/short drop auto-rejoins same identity & name | Satisfies F-02 reconnection-without-corruption | Plan |
| Link/route shape | Dedicated anonymous `/join/{code}` → name → server redeem → redirect to room | Clean separation from the member auth flow | Plan |
| Code format | ~10-char crypto-random URL-safe base32, globally unique (filtered unique index) | Short, shareable, collision-safe | Plan |
| Name validation | Required, trimmed, 1–40 chars, duplicates allowed, no content filter | Minimal friction for MVP | Plan |
| Isolation enforcement | `sid` claim must equal requested `sessionId` on every hub method; code resolved via `IgnoreQueryFilters` | Hard guarantee a guest can't reach another session/tenant | Plan |
| Testing | Unit + integration (code resolution, guest authz/sid-scoping, tenant bypass, name validation) + Playwright E2E | Protects the isolation-critical surface | Plan |

## Scope

**In scope:** share code on sessions + generation on activation; anonymous redeem endpoint + guest cookie scheme; hub guest admission, per-session confinement, vote-only enforcement; client `/join/{code}` page, guest-aware voting room, organizer share link; `oid`/`is_guest` client identity; en/pl localization; unit/integration/E2E tests.

**Out of scope:** persisted guests, guest history/re-identification, code rotation UI, per-session enable toggle, TTL independent of status, username uniqueness/profanity filtering, QR/email invites, changes to the member voting path beyond the shared `CurrentUserReply` extension.

## Architecture / Approach

Guest signs in to a **separate cookie scheme** via an anonymous `POST /guest/join` that resolves the code with the tenant filter bypassed (read-only) and mints a principal whose `tid` is the *session's* tenant. SignalR carries the cookie automatically; the hub admits `Cookies,Guest`, confines guests by matching the `sid` claim to the requested session on every call, loads the seed via a member-check-free path, and throws on any control method when `is_guest`. The client gains an anonymous join page and renders the existing voting room vote-only for guests.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Domain & persistence | `ShareCode` + unique index + migration, generation on activate, DTO exposure, tenant-bypass resolver | Filtered-unique-index correctness on SQL Server |
| 2. Guest credential & redeem | `Guest` cookie scheme + anonymous `/guest/join`, `ICurrentUserContext` guest/participant fields | Read-only redeem must avoid `SaveChanges` (empty-tenant throw) |
| 3. Hub guest support | Dual-scheme auth, `sid` confinement, guest seed load, vote-only enforcement | Missing a `sid` check on any method = isolation leak |
| 4. Client journey | `/join/{code}`, vote-only room, organizer share link, `oid`/`is_guest` claims, en/pl | Auth-state refresh after cookie set (force reload) |
| 5. E2E & error states | Playwright guest flow + error pages | E2E flakiness / WASM boot timing |

**Prerequisites:** S-04 (sessions), F-02/S-06 (voting room) — both done. Local run needs Podman (Aspire); E2E needs Playwright chromium installed.
**Estimated effort:** ~4–5 implementation sessions, one per phase.

## Open Risks & Assumptions

- The guest cookie is a bearer credential for one session — correctness rests entirely on the per-method `sid` check; this is the highest-value test target.
- Redeem runs under an empty-tenant context and must stay strictly read-only.
- Sessions activated before this change have a null code until re-activated (acceptable for MVP).
- Forcing a full reload after `/guest/join` is required so the new cookie surfaces to the WASM auth state.

## Success Criteria (Summary)

- A logged-out user joins an active session via a link with just a name and votes in real time, hidden until the organizer reveals.
- A guest credential cannot read or act on any session other than the one it redeemed.
- Guests have no round-control ability (UI and server both enforce it); members are unaffected.
