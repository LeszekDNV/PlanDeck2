# Frame Brief: Microsoft login failure in Testing

> Framing step before /10x-plan. This document captures what is *actually*
> at issue, separated from what was initially assumed.

## Reported Observation

Na środowisku Test po kliknięciu "Sigh In with a Microsoft account" na ekranie
logowania przekierowuje na stronę
`https://plandeck-server.wittymeadow-96369440.polandcentral.azurecontainerapps.io/account/entra/login?returnUrl=https%3A%2F%2Fplandeck-server.wittymeadow-96369440.polandcentral.azurecontainerapps.io%2F`
i wyświetla:

```
404 - Page Not Found
Sorry, the content you are looking for does not exist.
```

Logowanie za pomocą konta SSO nie działa.

## Initial Framing (preserved)

- **User's stated cause or approach**: Nie wskazano; zgłoszenie było oparte na obserwacji.
- **User's proposed direction**: Przywrócić działające logowanie kontem Microsoft bez narzucania sposobu naprawy.
- **Pre-dispatch narrowing**: Żądanie do `/account/entra/login` zwraca 404 w środowisku Test.

## Dimension Map

The observation could originate at any of these dimensions:

1. **Client navigation** — the login action could generate the wrong URL.
2. **Server route registration** — the deployed server could omit `/account/entra/login`.
3. **Testing Entra configuration** — the route could exist while its required OIDC scheme is unavailable.
4. **Deployed ACA revision** — Testing could run an artifact older than the route.

## Hypothesis Investigation

| Hypothesis | Evidence | Verdict |
| --- | --- | --- |
| Client generates the wrong URL | The client navigates to `account/entra/login` in `AccountClientService.cs:123-130`, and the observed browser URL is the expected root route rather than a duplicated path. | NONE |
| Server does not map the route | The route is defined in `AccountEndpointExtensions.cs:223-233`, registered in `Program.cs:65`, and mapped before the SPA fallback at `Program.cs:147-149`. | NONE |
| Testing lacks the OIDC scheme required by the route | OIDC is registered only with complete credentials (`ServiceCollectionExtensions.cs:69-76,95-130`), while Testing permits missing credentials (`ProductionAuthenticationConfigurationTests.cs:48-61`). The route always challenges OIDC (`AccountEndpointExtensions.cs:223-230`). Runtime returns HTTP 500. | STRONG |
| Testing runs an old ACA revision | The route was introduced in commit `a7ba02b`; later deployment failures could explain an earlier transient 404, but successful run `30403243585` deployed commit `8a5da3f`, which contains the route. | WEAK |

## Narrowing Signals

- The user confirmed the browser reaches exactly `/account/entra/login`, ruling out an incorrect client destination.
- A current request has HTTP status 500 even though the rendered body says "404 - Page Not Found".
- A configured OIDC handler would produce a 302 redirect to Microsoft; a missing route would produce HTTP 404, not HTTP 500.
- The current source and latest successful deployment both contain the endpoint.

## Cross-System Convention

An external-login endpoint normally challenges a registered authentication
scheme and returns a 302 redirect to the identity provider. An exception should
be rendered by a real server error endpoint while retaining an accurate error
message. Here, `UseExceptionHandler("/Error")` (`Program.cs:39-42`) targets no
server endpoint, so `/Error` falls through to the SPA (`Program.cs:147-149`);
the client router (`App.razor:2`) then displays the generic 404 component
(`Pages/NotFound.razor:6-7`).

## Reframed Problem Statement

> **The actual problem to plan around is**: Testing exposes the Microsoft login endpoint without a configured OpenID Connect handler, causing HTTP 500, while the exception pipeline masks that server failure as a 404 page.

The initial observation was accurate, but the visible 404 was not the HTTP
failure or its root cause. The SSO failure originates before any redirect to
Microsoft: the endpoint challenges a scheme that Testing did not register.
The missing `/Error` handler independently makes diagnosis misleading.

## Confidence

- **HIGH** — source configuration predicts the observed HTTP 500, the runtime
  status confirms it, and an independent investigation reconstructed both the
  authentication failure and the misleading 404 body.

## What Changes for /10x-plan

The plan should cover the Testing authentication contract end to end: Microsoft
login must never be exposed with an unavailable OIDC handler, and server
exceptions must not be represented as client-side 404 pages. It should not plan
around adding the already-existing `/account/entra/login` route.

## References

- Source files: `AccountClientService.cs:123-130`, `AccountEndpointExtensions.cs:223-233`, `ServiceCollectionExtensions.cs:69-130`, `Program.cs:39-65,147-149`, `AppHost.cs:84-102`, `App.razor:2`, `Pages/NotFound.razor:6-7`
- Related research: none
- Investigation tasks: `client-route-check`, `server-route-check`, `entra-config-check`, `deployment-check`, `independent-error-check`
