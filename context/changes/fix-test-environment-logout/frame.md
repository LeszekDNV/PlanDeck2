# Frame Brief: Test environment logout

> Framing step before /10x-plan. This document captures what is actually
> at issue, separated from what was initially assumed.

## Reported Observation

On the Testing environment at
`https://plandeck-server.wittymeadow-96369440.polandcentral.azurecontainerapps.io`,
pressing Log Out reloads the page and the user remains signed in as Test Owner.

## Initial Framing (preserved)

- **User's stated cause or approach**: No cause was proposed; the report was observation-driven.
- **User's proposed direction**: Correct the broken Log Out behavior.
- **Pre-dispatch narrowing**: The issue is observed only on the Testing environment.

## Dimension Map

The observation could originate at any of these dimensions:

1. **Client navigation** — the button might not reach the server endpoint, or
   Blazor routing might intercept the request.
2. **Logout endpoint** — the Testing branch might redirect without changing
   authentication state.
3. **Test authentication handler** — a stateless handler might recreate an
   authenticated principal on every request.
4. **Deployment configuration** — the deployed environment might be using the
   deterministic test scheme rather than an ordinary sign-in session.

## Hypothesis Investigation

| Hypothesis | Evidence | Verdict |
| --- | --- | --- |
| Client navigation does not execute server logout | Both desktop and mobile actions call `Logout`; it uses `NavigateTo("/auth/logout", forceLoad: true)`, forcing a server request (`MainLayout.razor:42,85`; `MainLayout.razor.cs:17,37-41`). | NONE |
| Testing logout is a no-op | When `useTestScheme` is true, `/auth/logout` returns only `LocalRedirect("/")`; `SignOut` runs only for cookie/OIDC auth (`Program.cs:111-123`). | STRONG |
| Test auth recreates Test Owner after redirect | The handler authenticates every request and defaults to `TestMemberIdentities.Owner` when `e2e-user` is absent (`TestAuthenticationHandler.cs:44-49,83-107`). | STRONG |
| Testing deployment intentionally enables test auth | Publish-mode Testing sets `ASPNETCORE_ENVIRONMENT=Testing` and `Authentication__UseTestScheme=true`; the test deployment workflow also exports `PLANDECK_E2E_TESTAUTH=true` (`AppHost.cs:23-35,97-106`; `.github/workflows/azure-dev.yml:35-45`). | STRONG |

## Narrowing Signals

- The full reload observed by the user matches `forceLoad: true` and the local
  redirect, ruling out an SPA-only stale-state symptom.
- Direct navigation to `/auth/logout` on the deployed URL redirects to `/` and
  still presents Test Owner, reproducing the report independently of the button.
- The issue being Testing-only matches the branch guarded by `useTestScheme`.
- No logout test covers the deterministic test-auth path.

## Cross-System Convention

Production authentication has sign-out-capable cookie and OIDC schemes, and the
endpoint calls `Results.SignOut` for those schemes. The deterministic Testing
scheme is different: it synthesizes an identity from each request and has no
sign-out state. Treating its logout as a redirect therefore cannot produce the
same user-visible anonymous state.

An independent, hypothesis-blind pass reached the same chain and found no
evidence for client caching or route interception. The live deployed behavior
also matches the code's predicted redirect followed by immediate reauthentication.

## Reframed (or Confirmed) Problem Statement

> **The actual problem to plan around is**: the deterministic Testing
> authentication scheme has no representable signed-out browser state, while
> `/auth/logout` claims to support logout by performing only a redirect.

The button and navigation are functioning correctly. After the redirect,
`TestAuthenticationHandler` authenticates a request without identity-selection
state as Test Owner, so the application cannot remain anonymous. The plan must
address the contract between Testing logout behavior and deterministic test
authentication rather than changing the UI button.

## Confidence

- **HIGH** — the server branches directly encode the observed behavior, the
  deployed endpoint reproduces it, the environment-specific configuration
  explains its scope, and an independent investigation reached the same result.

## What Changes for /10x-plan

Plan the Testing authentication lifecycle so a browser can enter and preserve an
anonymous state after `/auth/logout`, while retaining deterministic identities
needed by E2E tests. Include coverage for the endpoint-to-handler behavior.

## References

- Client action: `src/PlanDeck/Web/PlanDeck.Client/Layout/MainLayout.razor:42,85`
- Client navigation: `src/PlanDeck/Web/PlanDeck.Client/Layout/MainLayout.razor.cs:17,37-41`
- Logout endpoint: `src/PlanDeck/Web/PlanDeck.Server/Program.cs:111-123`
- Test scheme registration: `src/PlanDeck/Web/PlanDeck.Server/Extensions/ServiceCollectionExtensions.cs:60-75`
- Test identity synthesis: `src/PlanDeck/Web/PlanDeck.Server/Identity/TestAuthenticationHandler.cs:44-49,83-107`
- Testing publish configuration: `src/PlanDeck/Aspire/PlanDeck.AppHost/AppHost.cs:23-35,97-106`
- Deployment flag: `.github/workflows/azure-dev.yml:35-45`
- Investigation tasks: `logout-client-route`, `logout-test-auth`,
  `logout-deployment`, `logout-independent-check`
