<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Guest-link Voting (S-07)

- **Plan**: context/changes/guest-link-voting/plan.md
- **Scope**: Phases 1–5 of 5
- **Date**: 2026-06-24
- **Verdict**: REJECTED
- **Findings**: 1 critical · 1 warning · 2 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | PASS |
| Safety & Quality | FAIL |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

## Findings

### F1 — Guest credential grants tenant-wide access to management gRPC

- **Severity**: ❌ CRITICAL
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Safety & Quality (security / privilege escalation)
- **Location**: src/PlanDeck/Web/PlanDeck.Server/Program.cs:66-78; src/PlanDeck/Core/PlanDeck.Application/Services/SessionGrpcService.cs:74-78
- **Detail**: The guest principal carries the victim's real tenant (`tid = session.TenantId`), and the fallback middleware promotes it to `HttpContext.User` for every request — not just the hub. The management gRPC services (`SessionGrpcService`, `TeamGrpcService`, `SessionMemberGrpcService`) have no per-call authorization and no `IsGuest` guard; they rely only on the tenant query filter. `GetSessionsAsync` (SessionRepository.cs:18-25) filters by tenant alone — NOT membership. A guest invited to ONE session can call `ListSessions`, `DeleteSession`, `UpdateSessionConfig`, `AddTask`, team/member APIs, etc. and read/modify/delete every session in the host's tenant. Pre-change, anonymous callers were implicitly blocked (empty tenant → no data / SaveChanges throws); the guest's real `tid` bypasses that boundary. The guest voting page legitimately needs `SessionService.GetSession` (VotingRoom.razor.cs:46), so a blanket guest block on SessionService would break it.
- **Fix A ⭐ Recommended**: Reject `currentUser.IsGuest` on the management surface (all mutating/listing methods of Session/Team/SessionMember services, throwing RpcException PermissionDenied), while still allowing the guest's own-session `GetSession` (ideally scoped to `sid`).
  - Strength: Closes the escalation at the boundary that leaks (the services); keeps the guest voting page working.
  - Tradeoff: Touches several methods; needs a shared guard helper to stay DRY.
  - Confidence: HIGH — services confirmed unauthorized beyond tenant resolution.
  - Blind spot: Need to enumerate exactly which methods guests may keep (GetSession).
- **Fix B**: Narrow the guest-promotion middleware so the guest identity is applied only for the hub + AuthService (and the guest's own GetSession), leaving the rest of the gRPC surface anonymous.
  - Strength: Restores the original implicit boundary with one middleware change.
  - Tradeoff: GetSession shares SessionService with the dangerous methods, so it can't be split by service alone — brittle.
  - Confidence: MED — depends on cleanly separating GetSession from the rest.
  - Blind spot: Other client calls a guest legitimately makes.
- **Decision**: Fixed via Fix A — added shared `GuestAccessGuard.RejectGuests` to all management methods of Session/Team/SessionMember/AzureDevOps gRPC services; scoped `GetSessionAsync` to the guest's own `SessionScope` (`sid` claim). Added 3 regression unit tests; full unit suite green.

### F2 — Share code minted without bounded uniqueness retry

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence / Reliability
- **Location**: src/PlanDeck/Core/PlanDeck.Application/Services/SessionGrpcService.cs:231-250
- **Detail**: Plan §Phase 1.4 specified retrying on the unique-index `DbUpdateException` with a fresh code. Implementation sets `ShareCode ??= Generate()` once and only catches `SessionNotFoundException`. Collision odds are negligible (~50-bit code), but a rare collision would surface as an unhandled 500 on activation instead of a transparent retry.
- **Fix**: Wrap the activation save in a small bounded loop that regenerates the code and retries on the unique-index DbUpdateException.
- **Decision**: Fixed — `ActivateSessionAsync` now mints the share code via a bounded `GenerateUniqueShareCodeAsync` loop (5 attempts, probing `ShareCodeExistsAsync`) instead of a single-shot generate; kept in the Application layer (no EF `DbUpdateException` dependency) to respect layering. Existing activation unit tests green.

### F3 — Guest set-active affordance rendered (client no-op, not hidden)

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: src/PlanDeck/Web/PlanDeck.Client/Pages/VotingRoom.razor (task list); VotingRoom.razor.cs:111
- **Detail**: Plan §Phase 4.4 says hide reveal/reset/set-active/select-estimate for guests. Reveal/reset/select-estimate are gated on `!_isGuest`, but the task list stays clickable for guests; `SetActiveTaskAsync` is a client-side no-op and the hub also rejects it, so it's safe — just a cosmetic affordance the plan wanted suppressed.
- **Fix**: Disable/skip task-item click handling for guests (or visually mark read-only).
- **Decision**: Fixed — task list `MudListItem` now `Disabled="_isGuest"`, so guests see a read-only, non-clickable list (affordance suppressed as the plan intended); the client-side `SetActiveTaskAsync` guest no-op stays as defense-in-depth. Client build green.

### F4 — Success E2E uses guest cookie + direct nav, not the /join redeem

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: src/PlanDeck/Tests/PlanDeck.E2e.Tests/GuestVotingTests.cs:37-52
- **Detail**: The happy-path test seeds `e2e-guest-sid` and navigates straight to /voting/{id} rather than submitting /join/{code}. Deliberate adaptation: under the deterministic test-auth scheme, navigating through /join re-authenticates as the member, so the guest in-room experience can't be reached via the form. The error path IS driven through the real join form. Acceptable given the constraint; noted for transparency.
- **Decision**: Skipped — accepted as a deliberate, documented test-scheme adaptation. The deterministic test-auth handler re-authenticates `/join` traffic as a member, so the guest in-room view is unreachable via the form; the error path still exercises the real join form. No code change.
