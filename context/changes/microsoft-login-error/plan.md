# Microsoft Login Error Implementation Plan

## Overview

Restore reliable Microsoft account login in the published Testing environment and
remove the misleading SPA 404 shown for server failures. Published Testing and
Production must require complete Entra configuration, while optional environments
must expose neither Microsoft actions nor OIDC challenge routes when the provider is
unavailable.

## Current State Analysis

The server determines whether Microsoft authentication is configured from
`TenantId`, `ClientId`, and `ClientSecret`, and registers the OpenID Connect handler
only when all three values exist. Production already fails closed, but Testing is
allowed to start without them. In contrast, all Entra login, registration, and
linking endpoints are always mapped and always challenge the OIDC scheme.

The client also always renders Microsoft actions on the login, registration, and
account-security pages. This creates a split contract: the UI and HTTP routes claim
the provider is available even when the authentication scheme is absent.

In non-Development environments, `UseExceptionHandler("/Error")` re-executes failed
requests against an unmapped path. The hosted WASM fallback serves `index.html`, and
the client router renders its generic Not Found page while the underlying response
retains HTTP 500.

## Desired End State

Published Testing and Production fail during server startup unless complete Entra
credentials are present. Local and isolated test hosts may omit Entra configuration;
in that mode the server does not map OIDC challenge routes and the hosted WASM client
does not render Microsoft login, registration, or linking actions.

When Entra is available, the existing login/register/link behavior remains intact
and `/account/entra/login` produces a redirect to Microsoft. Any unrelated,
unhandled server exception returns an accurate HTTP 500: Problem Details for API
traffic or a minimal safe HTML response for browser navigation, both carrying the
same trace identifier and neither falling through to the SPA 404 page.

### Key Discoveries:

- OIDC availability is already computed from complete credentials, but only
  Production requires it (`src/PlanDeck/Web/PlanDeck.Server/Extensions/ServiceCollectionExtensions.cs:69-130`).
- Testing explicitly permits missing Entra settings
  (`src/PlanDeck/Tests/PlanDeck.Integration.Tests/Identity/ProductionAuthenticationConfigurationTests.cs:48-61`).
- Login, registration, and linking always challenge the named OIDC scheme
  (`src/PlanDeck/Web/PlanDeck.Server/Extensions/AccountEndpointExtensions.cs:223-288`).
- The three client surfaces hard-code Microsoft actions as available
  (`src/PlanDeck/Web/PlanDeck.Client/Pages/Account/Login.razor:40-44`,
  `Register.razor:71-75`, `Security.razor:60-108`).
- The existing anonymous auth gRPC service is the established typed client-server
  boundary for authentication state
  (`src/PlanDeck/Core/PlanDeck.Core.Shared/Contracts/IAuthService.cs:7-36`,
  `src/PlanDeck/Core/PlanDeck.Application/Services/AuthGrpcService.cs:7-18`).
- `/Error` has no server endpoint and can be absorbed by the SPA fallback
  (`src/PlanDeck/Web/PlanDeck.Server/Program.cs:39-42,147-149`).

## What We're NOT Doing

- Adding the already-existing `/account/entra/login` route.
- Replacing Entra ID, changing the multi-tenant `/organizations` authority, or
  redesigning account provisioning/linking.
- Making Entra mandatory for local development or isolated integration-test hosts.
- Exposing tenant IDs, client IDs, client secrets, or other sensitive configuration
  through the capability contract.
- Redirecting server exceptions to a Blazor page and thereby losing the original
  HTTP 500 status.
- Adding database schema changes, migrations, retries, or persistent error storage.
- Running automated E2E tests against the deployed `rg-test` environment.

## Implementation Approach

Introduce one typed Microsoft-authentication options contract as the server-side
source of truth. It binds the existing configuration section, derives provider
availability from complete credentials, and validates on startup that availability
is true whenever `Required` is set. AppHost sets `Required=true` for published
Testing and Production without tying the rule to `ASPNETCORE_ENVIRONMENT=Testing`,
which remains usable by local integration tests.

Use the same availability value to register OIDC, map the three challenge routes,
and answer a new capability operation on the existing anonymous `IAuthService`.
Client account pages load that typed capability and render Microsoft actions only
when it is true. Unlinking an already-linked identity remains available because it
does not initiate an OIDC challenge.

Replace path re-execution with a DI-registered `IExceptionHandler`. It logs one
structured event with a trace identifier, preserves HTTP 500, returns safe
Problem Details for API/fetch traffic, and returns minimal HTML only for browser
navigation. It never includes exception messages, stack traces, or secrets.

## Critical Implementation Details

### Timing & lifecycle

Microsoft options validation must run at startup before endpoint mapping. The same
resolved options instance must drive OIDC registration, capability reporting, and
conditional route mapping so those surfaces cannot drift.

### User experience spec

Capability loading must not briefly render Microsoft actions before hiding them.
Account pages should default to unavailable until the server reply is received;
failure to load capabilities remains fail-closed for provider visibility without
blocking local username/password forms.

### Debug & observability

The exception handler uses one value derived from `Activity.Current?.Id` or
`HttpContext.TraceIdentifier` in both the structured log and response. Problem
Details and HTML responses expose only this identifier and generic support text.

## Phase 1: Establish the Entra Configuration and Routing Contract

### Overview

Create one validated configuration model, enforce Entra for published Azure targets,
and ensure OIDC routes exist only when their handler exists.

### Changes Required:

#### 1. Typed Microsoft authentication options

**Files**:

- `src/PlanDeck/Web/PlanDeck.Server/Identity/MicrosoftAuthenticationOptions.cs` (new)
- `src/PlanDeck/Web/PlanDeck.Server/Extensions/ServiceCollectionExtensions.cs`
- `src/PlanDeck/Web/PlanDeck.Server/appsettings.json`

**Intent**: Replace repeated string-based availability checks with one validated
contract that represents credentials, callback path, requirement, and derived
availability.

**Contract**: Bind `Authentication:Microsoft`, add the non-secret `Required` flag,
derive availability only when tenant ID, client ID, and client secret are all
non-empty, and validate on startup that `Required` implies availability. Configure
the existing OIDC handler from this contract without changing authority, callback,
token validation, or callback events.

#### 2. Published target requirement

**File**: `src/PlanDeck/Aspire/PlanDeck.AppHost/AppHost.cs`

**Intent**: Make incomplete Entra configuration a deployment/startup error for both
published Testing and Production while preserving optional local hosts.

**Contract**: Set `Authentication__Microsoft__Required=true` on the published server
resource for both supported publish targets. Continue forwarding credentials from
the existing `AZURE_ENTRA_*`/configuration sources; never write secret values to
logs or generated documentation.

#### 3. Conditional Entra challenge routes

**Files**:

- `src/PlanDeck/Web/PlanDeck.Server/Extensions/AccountEndpointExtensions.cs`
- `src/PlanDeck/Web/PlanDeck.Server/Program.cs`

**Intent**: Prevent requests from reaching a named authentication scheme that was
not registered.

**Contract**: Map `/account/entra/login`, `/account/entra/register`, and
`/account/entra/link` only when Microsoft authentication is available. Keep all
local-account routes and `/account/entra/unlink` available under their current
authorization and antiforgery rules.

#### 4. Configuration and route integration tests

**Files**:

- `src/PlanDeck/Tests/PlanDeck.Integration.Tests/Identity/ProductionAuthenticationConfigurationTests.cs`
- `src/PlanDeck/Tests/PlanDeck.Integration.Tests/Account/EntraEndpointAvailabilityTests.cs` (new)

**Intent**: Lock the startup and routing contract independently of real Microsoft
network access.

**Contract**: Cover required complete/incomplete settings, optional incomplete
settings, the presence of all three challenge routes when configured, their absence
when unconfigured, and the continued availability of local account routes.

### Success Criteria:

#### Automated Verification:

- Entra configuration tests pass:
  `dotnet test Tests/PlanDeck.Integration.Tests/PlanDeck.Integration.Tests.csproj --filter "FullyQualifiedName~ProductionAuthenticationConfigurationTests"`
- Entra route availability tests pass:
  `dotnet test Tests/PlanDeck.Integration.Tests/PlanDeck.Integration.Tests.csproj --filter "FullyQualifiedName~EntraEndpointAvailabilityTests"`
- Server builds:
  `dotnet build Web/PlanDeck.Server/PlanDeck.Server.csproj`

#### Manual Verification:

- An optional local host without Entra settings starts and direct Entra challenge
  URLs return Not Found rather than HTTP 500.
- A published-target configuration without complete Entra settings fails startup
  with a sanitized, actionable configuration error.

**Implementation Note**: After automated verification passes, pause for manual
confirmation of both startup modes before proceeding.

---

## Phase 2: Align Client Provider Availability

### Overview

Expose a non-secret authentication capability through the existing gRPC boundary
and make all Microsoft UI actions follow the server route contract.

### Changes Required:

#### 1. Authentication capability contract

**Files**:

- `src/PlanDeck/Core/PlanDeck.Core.Shared/Contracts/IAuthService.cs`
- `src/PlanDeck/Core/PlanDeck.Application/Services/AuthGrpcService.cs`

**Intent**: Let the hosted WASM client discover provider availability without
receiving configuration values or inferring availability from environment names.

**Contract**: Add an anonymous `GetAuthenticationCapabilitiesAsync` operation and
reply type with an append-only protobuf field for
`MicrosoftAuthenticationAvailable`. The application implementation reads only the
derived availability abstraction supplied by the host; it must not depend directly
on ASP.NET Core configuration types.

#### 2. Client capability wrapper

**Files**:

- `src/PlanDeck/Web/PlanDeck.Client/Services/IAccountClientService.cs`
- `src/PlanDeck/Web/PlanDeck.Client/Services/AccountClientService.cs`

**Intent**: Give account components one typed, cached way to load provider
availability.

**Contract**: Add an async capability query that calls the new `IAuthService`
operation through the existing `GrpcChannel`. Default unavailable and propagate an
explicit load failure to the page so local forms remain usable while Entra controls
stay hidden.

#### 3. Login and registration presentation

**Files**:

- `src/PlanDeck/Web/PlanDeck.Client/Pages/Account/Login.razor`
- `src/PlanDeck/Web/PlanDeck.Client/Pages/Account/Login.razor.cs`
- `src/PlanDeck/Web/PlanDeck.Client/Pages/Account/Register.razor`
- `src/PlanDeck/Web/PlanDeck.Client/Pages/Account/Register.razor.cs`

**Intent**: Render Microsoft login and registration only when the corresponding
server routes are available.

**Contract**: Load capabilities during component initialization, keep
`AccountActionButtons.ShowEntra` false until availability is confirmed, and preserve
all existing return URL, invitation, local-form, localization, and busy-state
behavior.

#### 4. Account security presentation

**Files**:

- `src/PlanDeck/Web/PlanDeck.Client/Pages/Account/Security.razor`
- `src/PlanDeck/Web/PlanDeck.Client/Pages/Account/Security.razor.cs`

**Intent**: Hide the Microsoft linking flow when no OIDC challenge route exists
without hiding already-linked identities or unlinking.

**Contract**: Gate only the link button/form on the capability. Continue listing
external logins and allow unlink operations according to the existing server rules.

#### 5. Capability integration tests

**Files**:

- `src/PlanDeck/Tests/PlanDeck.Integration.Tests/Identity/AuthenticationCapabilityTests.cs` (new)
- existing auth contract tests under
  `src/PlanDeck/Tests/PlanDeck.Integration.Tests/Identity/`

**Intent**: Prove the public capability matches actual scheme/route availability for
both configured and optional hosts.

**Contract**: Call the real gRPC-Web operation anonymously with complete and empty
Entra settings. Assert only the boolean capability is returned and that current-user
authentication behavior remains unchanged.

### Success Criteria:

#### Automated Verification:

- Authentication capability tests pass:
  `dotnet test Tests/PlanDeck.Integration.Tests/PlanDeck.Integration.Tests.csproj --filter "FullyQualifiedName~AuthenticationCapabilityTests"`
- Existing Entra and local account tests pass:
  `dotnet test Tests/PlanDeck.Integration.Tests/PlanDeck.Integration.Tests.csproj --filter "FullyQualifiedName~EntraAccountTests|FullyQualifiedName~LocalAccountTests"`
- Client and shared contracts build:
  `dotnet build Web/PlanDeck.Client/PlanDeck.Client.csproj`

#### Manual Verification:

- With Entra omitted, login, registration, and security pages never display
  Microsoft login/register/link actions, including during initial rendering.
- With complete Entra settings, all three Microsoft actions are visible and local
  account forms behave unchanged.

**Implementation Note**: Pause for manual confirmation of both capability states
before proceeding.

---

## Phase 3: Preserve Accurate Server Errors and Verify Deployment

### Overview

Replace the missing `/Error` re-execution target with a global exception handler,
prove both response formats, and smoke-test the repaired Microsoft redirect in the
published Testing environment.

### Changes Required:

#### 1. Global exception handler

**Files**:

- `src/PlanDeck/Web/PlanDeck.Server/Diagnostics/GlobalExceptionHandler.cs` (new)
- `src/PlanDeck/Web/PlanDeck.Server/Extensions/ServiceCollectionExtensions.cs`
- `src/PlanDeck/Web/PlanDeck.Server/Program.cs`

**Intent**: Preserve accurate HTTP semantics and safe diagnostics for exceptions
that escape application endpoints.

**Contract**: Register `AddProblemDetails` and a DI-backed `IExceptionHandler`, then
use parameterless exception-handler middleware before the remaining production
pipeline. Return HTTP 500 with `application/problem+json` for API/fetch traffic and
minimal HTML only for browser navigation requests. Use one trace ID in response and
structured log; never include exception details. Do not rewrite a response that has
already started and do not re-execute routing.

#### 2. Exception response integration tests

**File**:
`src/PlanDeck/Tests/PlanDeck.Integration.Tests/ErrorHandling/GlobalExceptionHandlerTests.cs` (new)

**Intent**: Reproduce failures through the real server pipeline and prove they
cannot become SPA 404 responses.

**Contract**: Use a deterministic throwing dependency or test-only endpoint
available only in the factory. Cover default/API negotiation, browser navigation
headers, stable HTTP 500, Problem Details fields, safe HTML, matching trace IDs,
absence of exception text/stack/index content, and a single structured error log.
Keep gRPC errors under the existing gRPC status contract rather than converting
handled RPC failures to HTML.

#### 3. Deployment and smoke-test contract

**Files**:

- deployment configuration consumed by
  `src/PlanDeck/Aspire/PlanDeck.AppHost/AppHost.cs`
- `context/changes/microsoft-login-error/change.md`

**Intent**: Ensure `rg-test` receives real Entra credentials and validate the
end-user redirect that deterministic tests cannot exercise.

**Contract**: Supply the existing `AZURE_ENTRA_TENANT_ID`,
`AZURE_ENTRA_CLIENT_ID`, and `AZURE_ENTRA_CLIENT_SECRET` secret inputs for the
Testing publish. Do not store secret values in repository files. Record manual
verification through the plan Progress section only.

### Success Criteria:

#### Automated Verification:

- Global exception-handler tests pass:
  `dotnet test Tests/PlanDeck.Integration.Tests/PlanDeck.Integration.Tests.csproj --filter "FullyQualifiedName~GlobalExceptionHandlerTests"`
- All integration tests pass:
  `dotnet test Tests/PlanDeck.Integration.Tests/PlanDeck.Integration.Tests.csproj`
- Whole solution builds:
  `dotnet build PlanDeck.slnx`

#### Manual Verification:

- In `rg-test`, clicking "Sign in with a Microsoft account" returns a 302 redirect
  to `login.microsoftonline.com`, and a valid organizational account can complete
  sign-in.
- A controlled non-sensitive server failure returns HTTP 500 with a trace ID and is
  not rendered as the PlanDeck 404 page.
- Azure/application logs contain the same trace ID and no credential or exception
  detail is exposed to the browser.

**Implementation Note**: This phase is complete only after the deployed Testing
smoke test and error-response review are confirmed by a human.

---

## Testing Strategy

### Unit Tests:

- No isolated domain unit tests are required; the behavior depends on options
  validation, authentication scheme registration, endpoint mapping, gRPC-Web, and
  middleware negotiation.
- If capability availability is extracted behind a pure abstraction, cover only its
  complete/partial/required truth table with focused parameterized tests.

### Integration Tests:

- Validate startup with required complete, required partial, and optional empty
  configuration.
- Verify the capability, OIDC scheme, and mapped challenge routes agree in every
  configuration state.
- Exercise all three challenge routes without following redirects; no test contacts
  Microsoft.
- Exercise API and browser-navigation exception formats through the real server
  middleware pipeline.
- Preserve current local-account, Entra provisioning/linking, and anonymous auth
  contract regressions.

### Manual Testing Steps:

1. Start an optional local host without Entra settings and verify no Microsoft
   actions are rendered.
2. Configure local Entra credentials and verify Microsoft actions appear and the
   login endpoint redirects to Microsoft.
3. Attempt a published-target startup with one missing credential and verify startup
   fails before serving traffic.
4. Deploy Testing with all three secret inputs.
5. Open the Testing login page, click the Microsoft action, and confirm the Microsoft
   authorization URL and successful callback.
6. Trigger an approved controlled server failure and confirm HTTP 500, safe content,
   trace correlation, and no SPA 404.

## Performance Considerations

The provider capability is static for the process lifetime and should be loaded once
per client scope/page lifecycle rather than polled. Conditional route mapping and
startup validation add no request-path work. Exception formatting affects failure
paths only and must not perform network or database calls.

## Migration Notes

No database migration is required. Deploy configuration before or together with the
new binary because the published Testing server will fail startup without complete
Entra credentials. Rollback restores the prior binary; existing credentials remain
compatible, but the prior misleading `/Error` behavior also returns.

## References

- Frame brief: `context/changes/microsoft-login-error/frame.md`
- Related Testing authentication lifecycle:
  `context/changes/fix-test-environment-logout/plan.md`
- Historical fail-closed deployment decision:
  `context/archive/2026-07-21-secure-ado-grpc-endpoints/plan.md:215-258`
- ASP.NET Core error handling:
  `https://learn.microsoft.com/aspnet/core/fundamentals/error-handling?view=aspnetcore-10.0`
- ASP.NET Core options validation:
  `https://learn.microsoft.com/aspnet/core/fundamentals/configuration/options?view=aspnetcore-10.0`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Establish the Entra Configuration and Routing Contract

#### Automated

- [x] 1.1 Entra configuration tests pass — efa5da1
- [x] 1.2 Entra route availability tests pass — efa5da1
- [x] 1.3 Server builds — efa5da1

#### Manual

- [x] 1.4 Optional local host omits Entra routes without HTTP 500 — efa5da1
- [x] 1.5 Published target fails startup with incomplete Entra configuration — efa5da1

### Phase 2: Align Client Provider Availability

#### Automated

- [x] 2.1 Authentication capability tests pass — cbcd74d
- [x] 2.2 Existing Entra and local account tests pass — cbcd74d
- [x] 2.3 Client and shared contracts build — cbcd74d

#### Manual

- [x] 2.4 Optional host hides all Microsoft actions without render flicker — cbcd74d
- [x] 2.5 Configured host shows all Microsoft actions with unchanged local forms — cbcd74d

### Phase 3: Preserve Accurate Server Errors and Verify Deployment

#### Automated

- [x] 3.1 Global exception-handler tests pass — edabca8
- [x] 3.2 All integration tests pass — edabca8
- [x] 3.3 Whole solution builds — edabca8

#### Manual

- [x] 3.4 Testing Microsoft login redirects and completes successfully — edabca8
- [x] 3.5 Controlled server failure remains HTTP 500 with a trace ID — edabca8
- [x] 3.6 Logs correlate the trace ID without exposing sensitive details — edabca8
