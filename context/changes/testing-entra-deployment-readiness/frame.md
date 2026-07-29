# Frame Brief: Testing Entra deployment readiness

> Follow-up framing extracted from the runtime investigation originally recorded
> under `context/changes/microsoft-login-error/frame.md`. The completed
> `microsoft-login-error` implementation remains unchanged.

## Reported Observation

Na srodowisku testowym akcje "Sign in with a Microsoft account" oraz
"Create account with Microsoft" zwracaja blad aplikacji 4xx/5xx. Lokalnie w
srodowisku Development oba przeplywy dzialaja poprawnie.

## Initial Framing

- **User's stated cause or approach**: W konfiguracji srodowiska testowego moze brakowac wartosci wymaganych przez Microsoft Entra.
- **User's proposed direction**: Zbadac konfiguracje srodowiska testowego i ustalic, czego brakuje.
- **Pre-dispatch narrowing**: Po kliknieciu aplikacja zwraca blad 4xx/5xx, a nie blad wyswietlany przez Microsoft.

## Hypothesis Investigation

| Hypothesis | Evidence | Verdict |
| --- | --- | --- |
| Client or server route is wrong | The completed `microsoft-login-error` change made provider UI and challenge routes conditional and verified them with integration and browser tests. | NONE |
| Deployment lacks Entra application inputs | Both GitHub workflows provide only the pipeline identity's `AZURE_CLIENT_ID` and `AZURE_TENANT_ID`. AppHost separately expects `AZURE_ENTRA_TENANT_ID`, `AZURE_ENTRA_CLIENT_ID`, and `AZURE_ENTRA_CLIENT_SECRET`, then forwards missing values as empty strings (`.github/workflows/azure-dev.yml:41-50`, `.github/workflows/azure-develop.yml:45-54`, `src/PlanDeck/Aspire/PlanDeck.AppHost/AppHost.cs:89-103`). | STRONG |
| Active ACA revision starts with invalid configuration | Published targets set `Authentication__Microsoft__Required=true`; server startup rejects incomplete settings through `MicrosoftAuthenticationOptions.Validate()` (`src/PlanDeck/Aspire/PlanDeck.AppHost/AppHost.cs:99-103`, `src/PlanDeck/Web/PlanDeck.Server/Identity/MicrosoftAuthenticationOptions.cs:26-34`). The observed active revision was unhealthy and received traffic. | STRONG |
| Testing Entra registration or redirect URI is missing | The available development registration contained only the localhost callback. No dedicated web registration was found for the public Testing callback URL. | STRONG |
| Deployment detects an unhealthy revision | Both GitHub workflows end after `azd deploy` and do not inspect the new ACA revision or the public application endpoint (`.github/workflows/azure-dev.yml:170-172`, `.github/workflows/azure-develop.yml:174-176`). | NONE |

## Narrowing Signals

- The failure occurs before redirecting the browser to `login.microsoftonline.com`.
- Server startup is intentionally fail-fast when Microsoft authentication is required
  but incomplete.
- The pipeline deployment identity and the user-facing Entra web application are
  separate security principals with different responsibilities.
- `azd provision` applies application configuration and `azd deploy` applies the
  image; each can create an ACA revision, so readiness must be checked after the
  final deploy step.
- PlanDeck already exposes `/health`, including the SQL health check, and maps it
  outside the SPA fallback.

## Reframed Problem Statement

> **The actual problem to plan around is**: Testing has no complete, dedicated
> Microsoft Entra web-application contract or public callback registration, while
> the deployment workflows neither supply the required values nor fail when the
> final Azure Container Apps revision is unhealthy.

The application-side fail-fast behavior is correct. The missing work is at the
environment and delivery boundary: provision a dedicated Testing registration,
deliver its values securely, validate them before deployment, and gate deployment
success on both the final ACA revision and the public readiness endpoint.

## Confidence

- **HIGH** - workflow configuration, AppHost propagation, startup validation,
  observed revision state, and Entra registration inspection all support the same
  causal chain.

## Planning Constraints

- Use a dedicated `PlanDeck Testing` Entra web application.
- Store tenant ID and client ID as GitHub Environment variables and the client
  secret as a GitHub Environment secret.
- Apply the same contract to both Testing GitHub workflows.
- Require a healthy final ACA revision and a successful public HTTPS `/health`
  request.
- On failure, fail the workflow without automatically changing traffic weights or
  deactivating revisions.
- Do not change client controls, account routes, or server authentication behavior.

## References

- `.github/workflows/azure-dev.yml`
- `.github/workflows/azure-develop.yml`
- `src/PlanDeck/Aspire/PlanDeck.AppHost/AppHost.cs`
- `src/PlanDeck/Web/PlanDeck.Server/Identity/MicrosoftAuthenticationOptions.cs`
- `src/PlanDeck/Aspire/PlanDeck.ServiceDefaults/Extensions.cs`
- `context/changes/microsoft-login-error/plan.md`
