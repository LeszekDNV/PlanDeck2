# Test Environment Logout Implementation Plan

## Overview

Make logout produce a durable anonymous browser state when the deterministic
Testing authentication scheme is enabled. Preserve the existing owner/admin/member
E2E identities, make Testing login restore Test Owner, and close the coupled guest
cookie gap without changing the Blazor logout button or deployment configuration.

## Current State Analysis

The client correctly performs a full navigation to `/auth/logout`. In ordinary
authentication mode, the endpoint signs out the member cookie and optionally
OpenID Connect. In Testing mode it only redirects to `/`, while
`TestAuthenticationHandler` treats a missing `e2e-user` cookie as Test Owner.
Consequently, the next request immediately recreates an authenticated principal.

Guest state is coupled to the same endpoint. A real guest uses the
`PlanDeck.Guest` cookie scheme, while deterministic E2E guest contexts use the
`e2e-guest-sid` selector cookie. Neither state is currently cleared by the Testing
logout branch, and the ordinary branch does not explicitly handle a guest logout.

## Desired End State

- Logging out in Testing sets a durable anonymous marker and redirects to `/`.
- Reloading or navigating after logout remains anonymous and shows Log In.
- Testing Log In clears the anonymous marker and restores Test Owner.
- Explicit owner/admin/member E2E contexts continue to authenticate unchanged.
- Logging out as either a real guest or deterministic E2E guest clears guest state.
- Production member/OIDC logout behavior remains unchanged.

### Key Discoveries:

- `forceLoad: true` already guarantees a server request
  (`MainLayout.razor.cs:17`).
- The Testing endpoint branch is currently a redirect-only no-op
  (`Program.cs:111-123`).
- Missing `e2e-user` intentionally means Test Owner, so changing that default
  would break existing tests (`TestAuthenticationHandler.cs:102-107`;
  `E2eIdentityContextFactory.cs:45-59`).
- The handler already recognizes an anonymous request header, providing a
  consistent semantic precedent for an anonymous cookie marker
  (`TestAuthenticationHandler.cs:44-55`).
- Guest authentication has both a real cookie scheme and a separate E2E selector
  cookie (`GuestAuthentication.cs:13-29`;
  `TestAuthenticationHandler.cs:57-81`).

## What We're NOT Doing

- Changing `MainLayout.razor` or `MainLayout.razor.cs`.
- Replacing deterministic Testing authentication with Entra ID.
- Changing Testing ingress, publish flags, or Azure deployment configuration.
- Changing the default no-cookie Testing identity from Test Owner.
- Adding persistent server-side sessions or a database-backed logout state.
- Expanding E2E coverage into a role-by-role logout matrix.

## Implementation Approach

Use the existing `e2e-user` selection cookie as the single Testing browser-state
contract. Add an explicit `anonymous` value: the handler returns no identity when
it sees that value, while a missing cookie continues to mean Test Owner. Testing
logout writes the anonymous marker and clears both forms of guest state; Testing
login removes the marker, naturally restoring the existing Test Owner fallback.

Keep production member and OIDC sign-out behavior intact. Route real guest logout
through the dedicated Guest cookie scheme so a guest does not trigger an
unnecessary OpenID Connect sign-out. Prove the HTTP/cookie lifecycle with
integration tests, then add one browser-level regression using accessibility
locators and a layout Page Object.

## Critical Implementation Details

### State sequencing

The persistent anonymous marker must be evaluated after the request-header test
override but before guest and default-member selection. This prevents a stale
guest selector from undoing logout while preserving `X-Test-Identity` as the
highest-priority transport-test override.

### Timing & lifecycle

Cookie deletion must use the same root path and compatible security/SameSite
settings as cookie creation. The Testing flow must work over HTTPS in Azure and
the in-process HTTP test server.

## Phase 1: Implement the Authentication Lifecycle

### Overview

Introduce the anonymous Testing state, make login/logout transition that state,
clear guest authentication correctly, and lock the behavior with in-process
integration tests.

### Changes Required:

#### 1. Deterministic Testing identity state

**File**: `src/PlanDeck/Web/PlanDeck.Server/Identity/TestAuthenticationHandler.cs`

**Intent**: Extend the existing deterministic identity selector with an explicit
anonymous browser state without changing the no-cookie Test Owner default or the
owner/admin/member contracts.

**Contract**: Define one reusable anonymous selection value. After handling
`X-Test-Identity`, recognize `e2e-user=anonymous` as
`AuthenticateResult.NoResult()` before evaluating `e2e-guest-sid` or member
selection. Unknown selection values must continue to fail authentication.

#### 2. Login and logout transitions

**File**: `src/PlanDeck/Web/PlanDeck.Server/Program.cs`

**Intent**: Make `/auth/login` and `/auth/logout` perform real state transitions
for Testing browsers and correctly terminate guest authentication.

**Contract**:

- Testing `/auth/logout` writes the anonymous `e2e-user` marker, removes the
  deterministic `e2e-guest-sid` selector, signs out the `Guest` cookie scheme,
  and redirects locally to `/`.
- Testing `/auth/login` removes the anonymous/selection cookie and stale guest
  selectors before redirecting to the validated local return URL; the resulting
  no-cookie state authenticates as Test Owner.
- Ordinary guest logout signs out only the dedicated Guest scheme and redirects
  locally, avoiding an Entra remote-signout challenge for a guest principal.
- Ordinary member logout retains the existing member cookie plus optional OIDC
  sign-out behavior and also leaves no stale guest cookie.
- Existing local-redirect protection for `returnUrl` remains in force.

#### 3. Member lifecycle integration coverage

**File**:
`src/PlanDeck/Tests/PlanDeck.Integration.Tests/Identity/AuthenticationLifecycleTests.cs`

**Intent**: Prove that redirects and cookies produce the intended authenticated,
anonymous, and re-authenticated states in one browser-like HTTP lifecycle.

**Contract**: Reuse the Testing `WebApplicationFactory<ServerEntryPoint>` and
in-memory database pattern from `GrpcAuthenticationTests.cs:20-56` and
`E2eScenarioEndpointTests.cs:122-162`. Cover:

- default Testing request authenticates as Test Owner;
- logout returns a local redirect and transitions the same cookie-aware client
  to anonymous;
- a follow-up request and reload remain anonymous;
- login clears the anonymous state and restores Test Owner;
- explicit owner/admin/member selections remain accepted by the existing gRPC
  regression tests.

#### 4. Guest lifecycle integration coverage

**File**:
`src/PlanDeck/Tests/PlanDeck.Integration.Tests/GuestJoin/GuestJoinEndpointTests.cs`

**Intent**: Extend the existing real guest-cookie test fixture to prove that the
shared logout endpoint removes guest state rather than immediately restoring it.

**Contract**: Join an active session through `/guest/join`, call `/auth/logout`
with the same cookie-aware client, assert the root redirect and expired
`PlanDeck.Guest` cookie, and verify the subsequent auth state is anonymous.
Also cover removal of the deterministic `e2e-guest-sid` selector in the Testing
lifecycle fixture.

### Success Criteria:

#### Automated Verification:

- Authentication lifecycle tests pass:
  `dotnet test Tests/PlanDeck.Integration.Tests/PlanDeck.Integration.Tests.csproj --filter "FullyQualifiedName~AuthenticationLifecycleTests"`
- Guest join/logout tests pass:
  `dotnet test Tests/PlanDeck.Integration.Tests/PlanDeck.Integration.Tests.csproj --filter "FullyQualifiedName~GuestJoinEndpointTests"`
- Existing deterministic identity tests pass:
  `dotnet test Tests/PlanDeck.Integration.Tests/PlanDeck.Integration.Tests.csproj --filter "FullyQualifiedName~GrpcAuthenticationTests"`

#### Manual Verification:

- In a local Testing run, logout remains anonymous after a browser refresh and
  Log In restores Test Owner.

**Implementation Note**: After completing this phase and all automated
verification passes, pause for manual confirmation before proceeding.

---

## Phase 2: Add Browser Regression Coverage

### Overview

Exercise the real MudBlazor button and Blazor authentication-state refresh across
logout, reload, and login using one independent Playwright scenario.

### Changes Required:

#### 1. Layout authentication Page Object

**File**: `src/PlanDeck/Tests/PlanDeck.E2e.Tests/Pages/MainLayoutPage.cs`

**Intent**: Encapsulate shared identity text and login/logout interactions so the
test does not duplicate layout locators or depend on DOM structure.

**Contract**: Expose role/text-based locators and actions for Test Owner, Log Out,
and Log In. Use `GetByRole`/`GetByText`, support the English test culture, and wait
for visible application state rather than time delays.

#### 2. Logout lifecycle E2E scenario

**File**: `src/PlanDeck/Tests/PlanDeck.E2e.Tests/LogoutTests.cs`

**Intent**: Reproduce the reported user journey against either locally started
Aspire or the configured remote Testing URL.

**Contract**: In one standalone test:

- open the authenticated application and wait for Test Owner;
- click Log Out and wait for the root URL plus visible Log In;
- reload and confirm Log In remains visible;
- click Log In and wait for Test Owner and the authenticated route;
- rely only on the Page Object and accessibility locators, with no fixed timeout.

### Success Criteria:

#### Automated Verification:

- Targeted logout E2E passes locally:
  `dotnet test Tests/PlanDeck.E2e.Tests/PlanDeck.E2e.Tests.csproj --filter "FullyQualifiedName~LogoutTests"`
- Existing authentication-sensitive E2E smoke tests pass:
  `dotnet test Tests/PlanDeck.E2e.Tests/PlanDeck.E2e.Tests.csproj --filter "FullyQualifiedName~HomePageTests|FullyQualifiedName~SessionRoleSmokeTests"`
- The whole solution builds:
  `dotnet build PlanDeck.slnx`

#### Manual Verification:

- After deployment, the reported Testing URL shows Log In after logout and still
  shows Log In after a manual refresh.
- Clicking Log In on the Testing URL restores Test Owner.

**Implementation Note**: After completing this phase and all automated
verification passes, pause for deployment-based manual confirmation.

---

## Testing Strategy

### Unit Tests:

- No isolated unit test is required; the behavior depends on ASP.NET Core
  authentication handlers, response cookies, redirects, and middleware ordering.

### Integration Tests:

- Use a cookie-aware `WebApplicationFactory` client with automatic redirects
  disabled when asserting transition responses.
- Verify the resulting principal through the anonymous auth gRPC contract rather
  than only asserting `Set-Cookie` text.
- Cover member logout/login, deterministic guest selector cleanup, real guest
  cookie cleanup, refresh persistence, and existing role-selection regressions.

### E2E Tests:

- Keep one focused browser scenario for the user-visible lifecycle.
- Use the Page Object Pattern and accessibility locators.
- Wait for URL and visible authorization state; never use `WaitForTimeout`.
- Keep the test independent through Playwright's isolated browser context.

### Manual Testing Steps:

1. Open the deployed Testing URL and confirm Test Owner is displayed.
2. Click Log Out and confirm the root page displays Log In.
3. Refresh and confirm the browser remains anonymous.
4. Click Log In and confirm Test Owner and the authenticated application return.
5. Join as a guest in an available session, log out, and confirm guest access is
   no longer retained.

## Performance Considerations

The change adds only constant-time cookie checks and mutations. It introduces no
database access, network calls, or server-side session storage.

## Migration Notes

No data or infrastructure migration is required. Existing Testing browser
contexts without the new marker continue to authenticate as Test Owner. Rolling
back the application removes recognition of the marker; affected Testing browsers
would then return to Test Owner, which matches the pre-change behavior.

## References

- Frame brief:
  `context/changes/fix-test-environment-logout/frame.md`
- Logout endpoint:
  `src/PlanDeck/Web/PlanDeck.Server/Program.cs:103-123`
- Test handler:
  `src/PlanDeck/Web/PlanDeck.Server/Identity/TestAuthenticationHandler.cs:44-124`
- Guest cookie scheme:
  `src/PlanDeck/Web/PlanDeck.Server/Identity/GuestAuthentication.cs:13-53`
- Integration auth pattern:
  `src/PlanDeck/Tests/PlanDeck.Integration.Tests/Identity/GrpcAuthenticationTests.cs:20-164`
- Guest endpoint tests:
  `src/PlanDeck/Tests/PlanDeck.Integration.Tests/GuestJoin/GuestJoinEndpointTests.cs:19-193`
- E2E identity contexts:
  `src/PlanDeck/Tests/PlanDeck.E2e.Tests/E2eIdentityContextFactory.cs:5-73`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles.

### Phase 1: Implement the Authentication Lifecycle

#### Automated

- [x] 1.1 Authentication lifecycle tests pass
- [x] 1.2 Guest join/logout tests pass
- [x] 1.3 Existing deterministic identity tests pass

#### Manual

- [x] 1.4 Local Testing logout persists through refresh and login restores Test Owner

### Phase 2: Add Browser Regression Coverage

#### Automated

- [ ] 2.1 Targeted logout E2E passes locally
- [ ] 2.2 Existing authentication-sensitive E2E smoke tests pass
- [ ] 2.3 Whole solution builds

#### Manual

- [ ] 2.4 Deployed Testing logout persists through refresh
- [ ] 2.5 Deployed Testing login restores Test Owner
