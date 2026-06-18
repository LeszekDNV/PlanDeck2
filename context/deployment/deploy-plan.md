# Azure integration and deployment plan for PlanDeck

## Problem and approach

PlanDeck needs an Azure-aligned implementation path that turns the current scaffold into a deployable MVP on Azure Container Apps, while preserving the product-critical external integrations: Microsoft Entra ID authentication and Azure DevOps work-item import/write-back.

The plan follows `context\foundation\infrastructure.md` and `context\foundation\stack-assessment.md`: deploy the hosted Blazor WebAssembly + ASP.NET Core app as one containerized unit on Azure Container Apps, use Azure SQL Database for EF Core, use managed identity and Key Vault for production secrets, keep `azd`/Aspire as the initial source of truth, and treat gRPC-Web/WebSockets, Azure DevOps write-back, revisions, migrations, and stack-specific agent pitfalls as explicit validation gates.

## Current state observed

| Area | Current state | Planning implication |
| --- | --- | --- |
| Application host | `Aspire\PlanDeck.AppHost\AppHost.cs` runs local SQL Server and MailPit, then starts `PlanDeck.Server`. | AppHost is the right place to introduce Azure Container Apps, Azure SQL, Key Vault, and deployment metadata. |
| Server | `Program.cs` serves hosted WASM, localization, gRPC-Web, and a sample code-first gRPC service. Auth is commented out. | Deployment work must not bypass the server host; Entra ID and auth middleware need to be restored before protected features ship. |
| Data | `AddSqlDatabase` and `ApplyMigrationsAsync` are placeholders. Local Aspire provides a SQL connection reference named `DefaultConnection`. | Azure SQL deployment requires a real DbContext, connection binding, migrations, health checks, and a production migration policy. |
| External integrations | Azure DevOps integration is a PRD requirement but no implementation exists. | Integration should be isolated behind application/infrastructure abstractions and tested against API failure modes before production. |
| Deployment | No `azure.yaml`, Dockerfile, CI/CD, or Azure provisioning assets detected. | Start with `azd` + Aspire-generated Azure assets, then add CI/CD only after the first repeatable dev deploy works. |
| Reliability guardrails | Health check notes missing `.gitignore`, lock files, SDK pin, CI, and meaningful unit/integration tests. | Fix reproducibility guardrails before wiring cloud deployment to avoid noisy or unsafe deploy changes. |
| Agent readiness | Stack assessment verdict is `ready-with-compensation`: typed/layered stack is strong, but code-first `protobuf-net.Grpc`, recent Aspire APIs, missing SDK/tooling pins, and no CI need explicit safeguards. | Add compensation gates before deployment work and keep future implementation aligned with the documented architecture instead of generic .NET defaults. |

## External references checked

| Topic | Finding applied to plan |
| --- | --- |
| Aspire Azure Container Apps integration | Use `Aspire.Hosting.Azure.AppContainers`, add an ACA environment before customizing project publish behavior, and decide whether generated Azure assets remain the source of truth. |
| Azure Container Apps ingress | HTTP ingress supports HTTP/1.1, HTTP/2, WebSockets, gRPC, TLS termination, traffic splitting, and session affinity. PlanDeck must validate its exact gRPC-Web middleware and real-time voting path. |
| Azure Container Apps revisions | Single revision mode is default and supports zero-downtime replacement once the new revision is ready; multiple revisions enable manual traffic splitting but add operational complexity. MVP should start with single revision mode unless preview testing needs labels. |
| Azure Container Apps managed identity | Managed identity removes secrets from the app container but requires explicit RBAC and can be system-assigned or user-assigned. Plan should include identity/RBAC smoke tests. |
| Azure DevOps rate limits | API clients must honor `Retry-After`, monitor rate-limit headers, handle 429 explicitly, and surface delayed/blocked operations to users rather than silently dropping write-backs. |
| Stack assessment | Preserve the code-first gRPC pattern, remove stale `.proto` scaffolding, pin .NET/Aspire expectations, and add CI/tooling guardrails so agents do not drift into unsupported patterns during deployment work. |
| Azure-native CI/CD | `azd pipeline config` supports Azure Pipelines, and Azure Container Apps supports Azure Pipelines branch-triggered revision deployment. Use this Azure-native route instead of GitHub Actions. |

## Assumptions and boundaries

| Indicator | Assumption |
| --- | --- |
| Planned | "Azure integration" means both Azure platform deployment and Azure DevOps task import/write-back. |
| Planned | First deploy target is a test Azure environment, not production traffic. |
| Planned | Azure Container Apps runs a single public `PlanDeck.Server` container that hosts the Blazor WASM client and backend APIs. |
| Planned | Azure SQL is the database for production-like environments; local Aspire SQL Server remains for development. |
| Planned | Azure resource location preference is `polandcentral` (Europe, Poland Central) for all supported Azure resources. If any required service/SKU is unavailable there, fall back to a West Europe Azure region and document the exception per resource. |
| Planned | `azd` and Aspire-generated infrastructure are the initial deployment source of truth. Hand-authored Bicep/Terraform is out of scope unless generated assets become too opaque. |
| Planned | Code-first `protobuf-net.Grpc` remains the service-contract pattern. No `.proto` files, WCF attributes, or generated gRPC base classes are introduced for new backend endpoints. |
| Planned | Clean Architecture dependencies remain intact: Server hosts/wires endpoints, Application owns use cases/service implementations, Infrastructure owns persistence and external Azure DevOps access, Core.Shared owns contracts/DTOs. |
| Planned | CI/CD is Azure-native: no GitHub Actions. After repeatable `azd up`, configure Azure Pipelines so pushes to `master` trigger build/test/deploy to the agreed Azure Container Apps environment. |
| Gate | If `master` represents production, approval happens before merge to `master`; destructive migrations, Key Vault secret rotation, RBAC changes, database deletion, and Azure DevOps write-back scope changes still require human approval before the merge/deploy path is allowed to run. |

## Phase plan

### Phase 0 - Repository and deployment guardrails

| Indicator | Work |
| --- | --- |
| Done | Added/confirmed `.gitignore`, `global.json`, NuGet lock files, and `.editorconfig` so deploy changes are reproducible. |
| Done | Removed stale `.proto` scaffolding from `PlanDeck.Server.csproj` and deleted leftover `Protos\greet.proto`, preserving the code-first `protobuf-net.Grpc` convention. |
| Done | Agent-facing deployment note: future deployment/integration work must keep code-first gRPC contracts in `Core\PlanDeck.Core.Shared\Contracts`, implementations in `Core\PlanDeck.Application\Services`, host-only endpoint wiring in `Web\PlanDeck.Server`, and Azure/AppHost orchestration in `Aspire\PlanDeck.AppHost`; do not add `.proto` files, WCF attributes, generated gRPC base classes, or bypass the layered boundaries. |
| Done | Added safe `.env.example` configuration documentation with names only for Entra ID, Azure SQL, Azure DevOps, email/Teams, and deployment settings. |
| Passed | `dotnet build PlanDeck.slnx` succeeds from `src\PlanDeck`. |
| Passed | A search confirms no new `.proto`, `<Protobuf>`, WCF `[ServiceContract]`, or generated gRPC service-base pattern was introduced. |
| Support | If restore/build fails because of network TLS inspection, use the existing corporate cert notes at the repository root and avoid committing local NuGet credential material. |

### Phase 1 - Azure-ready Aspire AppHost

| Indicator | Work |
| --- | --- |
| Done | Add Azure Container Apps support to `PlanDeck.AppHost` with the Aspire Azure hosting integration. |
| Done | Model the Azure environment in AppHost: ACA environment, Azure SQL Database, Key Vault, Application Insights/Log Analytics, and ACR as generated or referenced resources. |
| Done | Configure generated `azd`/Azure provisioning to use `polandcentral` as the default location. Before provisioning, verify ACA, Azure SQL, Key Vault, Application Insights/Log Analytics, and ACR availability in `polandcentral`; if a resource cannot be created there, place only that resource in West Europe and record the reason in deployment notes/source configuration. |
| Done | Keep local SQL Server/MailPit paths separate from Azure resources so local development still works via `dotnet run --project Aspire\PlanDeck.AppHost`. |
| Passed | Verify Aspire 13.x API names against current docs before coding; do not rely on generic or older Aspire examples when adding `AddAzureContainerAppEnvironment`, Azure SQL, or publish customization. |
| Done | Generate and review `azure.yaml`/`azd` assets from `src\PlanDeck`; document which generated assets are source of truth. |
| Passed test | A dev Azure environment can be provisioned with `azd up` in `polandcentral`, or with documented per-resource fallback to West Europe when `polandcentral` does not support the required service/SKU, without manual portal-only steps beyond authentication/subscription selection. Environment `test` is deployed in `rg-test`. |
| Support | If Aspire-generated assets are opaque or fail during provisioning, capture the generated Bicep, resource names, and failed operation IDs before changing the model; do not hand-edit portal resources without reflecting the decision in source. |
| Support | If generated Aspire/Azure APIs differ from examples, pause and verify against the exact package version and generated build output before adding compatibility shims. |

### Phase 2 - Azure SQL and EF Core production path

| Indicator | Work |
| --- | --- |
| Done | Create the real EF Core DbContext in `PlanDeck.Infrastructure` and register it through `AddSqlDatabase` in `PlanDeck.Server\Extensions\ServiceCollectionExtensions.cs`. |
| Done | Bind `DefaultConnection` from Aspire locally and Azure SQL in cloud without hard-coded connection strings. |
| Partial | Add initial migrations for the first domain slice, then keep `ApplyMigrationsAsync` development-only unless a deliberate production migration runner is introduced. The migration hook is implemented; the first domain migration remains tied to the first persisted domain slice. |
| Done | Keep EF Core and external integration code in Infrastructure; avoid referencing ASP.NET Core, Blazor, or MudBlazor from Application/Core projects. |
| Done | Add health checks that fail clearly when SQL connectivity, migration state, or managed identity authorization is broken. |
| Pending Azure | Local SQL and Azure SQL paths both pass a smoke test that creates, reads, and rolls back/deletes non-production test data. |
| Support | For Azure SQL Free/serverless latency or quota surprises, record first-query latency and vCore/quota metrics; upgrade tier before user demos if session startup or save operations are visibly delayed. |

### Phase 3 - Entra ID authentication and secret handling

| Indicator | Work |
| --- | --- |
| Done | Re-enable ASP.NET Core authentication/authorization in `Program.cs` and configure Microsoft Entra ID for authenticated management actions. |
| Done | Keep guest voting as an explicit link-scoped exception: guests can join/vote with a temporary name but cannot manage teams, sessions, integrations, or history. |
| Done | Store cloud secrets in Key Vault and access them from ACA through managed identity. Keep secret values out of appsettings, docs, commits, logs, and chat. |
| Done | Keep authentication wiring in Server and authorization decisions at application boundaries; do not leak Entra-specific SDK types into Core.Shared contracts. |
| Done | Define required Entra app registrations, redirect URIs, scopes, and local development overrides. |
| Pending Azure | Authenticated and guest paths are tested separately: signed-in users can manage protected resources; guests can only access valid session links. |
| Support | If redirect URI, SameSite cookie, or reverse-proxy HTTPS behavior fails behind ACA, inspect forwarded headers, Entra app redirect URIs, and auth cookie settings before changing auth flow. |

### Phase 4 - Azure DevOps integration boundary

| Indicator | Work |
| --- | --- |
| Done | Add an application-level Azure DevOps port/interface for importing work items and writing estimates, with implementation in `PlanDeck.Infrastructure`. |
| Partial | Define storage for connected organization/project, work item ID, work item revision, imported title/state/type, estimate field reference name, and last sync/write status. DTOs and write-back metadata are defined; persistence awaits the first domain storage slice. |
| Done | Expose Azure DevOps import/write-back through code-first gRPC contracts in `PlanDeck.Core.Shared\Contracts` and service implementations in `PlanDeck.Application\Services`; client wrappers stay behind interfaces in `PlanDeck.Client\Services`. |
| Done | Implement read/import first, then write-back second. Write-back must target the originating work item and configured estimate field only. |
| Done | Handle Azure DevOps API rate limiting and transient failures by honoring `Retry-After`, surfacing 429/delay messages, and never marking write-back as successful unless Azure DevOps confirms it. |
| Done | Use optimistic concurrency where available, or at minimum compare stored work item revision/current state before write-back to reduce wrong-field/wrong-task risk. |
| Pending sandbox | A test or sandbox Azure DevOps project validates import, write-back success, changed work item revision, missing permissions, invalid field mapping, deleted work item, and rate-limit/retry behavior. |
| Support | Provide a manual recovery path for failed write-backs: show the selected final estimate, target work item URL, target field, error detail, and a retry/copy option. |

### Phase 5 - Real-time voting and ACA runtime behavior

| Indicator | Work |
| --- | --- |
| Done | Introduce the real-time transport for session voting, preserving gRPC-Web for browser-service calls and using WebSockets/SignalR-style behavior for live room state where appropriate. |
| Done | Configure ACA for MVP real-time safety: `minReplicas=1`, single revision mode, HTTP ingress, sticky sessions/session affinity, and one active replica unless state is externalized. |
| Passed local | Validate the hosted WASM + gRPC-Web path exactly as deployed through ACA, because stack assessment flags this RPC pattern as less common and easy for agents to accidentally replace with `.proto`-first examples. |
| Partial | Implement reconnect/session recovery so a hidden-vote round can survive browser refresh, transient disconnect, or ACA revision activation without leaking votes. SignalR automatic reconnect is wired; active-room revision behavior remains a pilot-deploy gate. |
| Done | Document the scaling trigger: before `maxReplicas > 1`, add Azure SignalR Service or another explicit backplane/external room-state strategy. |
| Partial test | Pilot deploy validates hosted Blazor load, gRPC-Web call, join/vote/reveal flow, WebSocket reconnect, and an active-room deployment/revision scenario. Hosted Blazor and gRPC-Web passed against `test`; live planning-room flow and revision scenario remain to be exercised. |
| Support | If sessions drop during revision changes, keep traffic on the known-good revision, collect ACA revision logs, and delay multi-replica or blue/green rollout until reconnect behavior is proven. |

### Phase 6 - Azure-native auto deployment after push to master

| Indicator | Work |
| --- | --- |
| Done | Establish a repeatable local deployment workflow: `azd auth login`, `azd env new`, `azd up`, smoke test, log inspection, and teardown guidance for dev environments. |
| Done | Configure Azure Pipelines, not GitHub Actions, as the automatic deployment mechanism. Use `azd pipeline config` with Azure Pipelines support when it fits the generated Aspire/`azd` workflow, or define an Azure DevOps `azure-pipelines.yml` that uses Azure Container Apps deployment tasks. |
| Done | Trigger the Azure Pipeline on pushes to `master`. The pipeline restores, builds `PlanDeck.slnx`, runs unit/integration tests, optionally runs E2E against a supplied `BaseUrl`, builds/publishes the container image, deploys a new ACA revision, and runs post-deploy smoke checks. |
| Done | Keep the pipeline definition under an Azure DevOps path such as `.azuredevops\pipelines\azure-dev.yml` or `azure-pipelines.yml`; do not create `.github\workflows` for this plan. |
| Done | CI must enforce the stack-assessment compensation checks: build the XML `PlanDeck.slnx`, run tests, reject stale `.proto`/`<Protobuf>` reintroduction, and publish E2E `BaseUrl` instructions for non-local environments. |
| Done | Store Azure deployment credentials in Azure DevOps service connections/secure pipeline variables and app secrets in Key Vault; never put secret values in repository files. |
| Done | Keep Azure DevOps API credentials for PlanDeck's work-item integration separate from Azure deployment credentials used by the pipeline. |
| Pending Azure | A push to `master` in Azure Repos triggers an Azure Pipeline run that deploys the latest commit to the agreed ACA environment without GitHub Actions. |
| Pending Azure | A clean Azure Pipeline runner can reproduce deployment from source and documented variables, including post-deploy smoke checks against the deployed ACA URL. |
| Support | If the Azure Pipeline deploys a bad revision, stop further runs, use ACA revision traffic/rollback commands to restore the previous revision, preserve pipeline logs, and only then patch the branch. |
| Support | If Azure Pipelines service connection or PAT setup fails, verify Azure DevOps project permissions, service connection scope, `AcrPush`/`AcrPull` roles, and `azd pipeline config` generated variables before manually creating credentials. |

### Phase 7 - Observability, rollback, and support runbooks

| Indicator | Work |
| --- | --- |
| Partial | Wire logs/traces/health into Application Insights and Log Analytics, including correlation IDs for Azure DevOps write-back attempts. Aspire/service-default health and Azure resources are wired; custom ADO correlation IDs remain for the first persisted workflow. |
| Done | Document ACA log, revision, and traffic commands for support: inspect revisions, follow logs, check replica state, and shift traffic to a previous revision. |
| Done | Treat application rollback and database rollback separately. ACA revision rollback does not undo EF Core migrations or Azure DevOps write-backs. |
| Done | Include diagnostic steps that distinguish app bugs from stack-wiring bugs: wrong gRPC pattern, wrong service layer, missing Aspire reference, missing ServiceDefaults, or missing managed identity/RBAC. |
| Done | Add runbooks for common incidents: failed ACA provisioning, failed managed identity/RBAC, SQL unavailable, migration failed, Azure DevOps 401/403/404/409/429, active session disconnected, and bad deployment revision. |
| Pending Azure | A rehearsal demonstrates rollback to a previous ACA revision and confirms the database migration state is still understood after rollback. |
| Support | For destructive or irreversible data changes, stop automation, preserve logs and database backups, and require human review before retrying. |

### Phase 8 - Production readiness decision

| Indicator | Work |
| --- | --- |
| Pending Azure | Review cost posture: ACA min replica, Azure SQL tier, Log Analytics retention, Key Vault, ACR, and any Azure SignalR/backplane addition. |
| Pending Azure | Confirm backup/PITR settings for Azure SQL and retention expectations for logs. |
| Pending Azure | Confirm least-privilege RBAC for deployment principal, ACA managed identity, Key Vault, SQL, and Azure DevOps integration identity. |
| Pending Azure | Confirm tenant isolation and guest-link access behavior before public release. |
| Passed local | Confirm agent-readiness compensation is complete: SDK pinned, dependency graph reproducible, code-first gRPC scaffold cleaned, Aspire deployment documented, CI present, and tests cover the first deployed integration path. |
| Pending Azure | Production launch is allowed only after deployment, auth, SQL, Azure DevOps write-back, rollback, and support runbooks pass in a production-like environment. |
| Support | If any external dependency remains unverified, release with a visible degraded-mode behavior: ad-hoc tasks instead of ADO import, manual estimate copy instead of write-back, or dev-only deployment until the gate is passed. |

## Edge-case support checklist

| Area | Edge case | Support step |
| --- | --- | --- |
| ACA provisioning | `azd up` fails while creating ACA/ACR/identity/resources. | Capture operation ID, generated Bicep, resource group deployment errors, and `azd` logs; do not patch resources manually without updating source. |
| ACA runtime | New revision fails readiness or active session drops. | Keep/restore traffic to prior revision, inspect `az containerapp revision list` and logs, verify probes and `minReplicas=1`. |
| Ingress | Blazor loads but gRPC-Web or WebSockets fail. | Validate ACA HTTP ingress, target port, HTTP protocol settings, TLS termination, CORS/origin assumptions, and app middleware ordering. |
| SQL | App starts but DB access fails in Azure. | Check connection name binding, SQL firewall/private access, managed identity role/user mapping, migration state, and health check output. |
| Key Vault | Secret lookup fails. | Verify managed identity assignment, Key Vault access/RBAC, secret names, and whether a new ACA revision/restart is required for config changes. |
| Entra ID | Login callback fails in cloud. | Check app registration redirect URIs, forwarded HTTPS headers, cookie settings, and environment-specific authority/client ID values. |
| Azure Pipeline trigger | Push to `master` does not deploy. | Check branch trigger syntax, Azure Repos branch policies, pipeline YAML path, disabled pipeline state, service connection authorization, and whether `master` vs `main` naming matches the repository. |
| Azure Pipeline deploy | Pipeline builds but cannot push/pull images or update ACA. | Verify service connection scope, ACR `AcrPush`, ACA managed identity `AcrPull`, registry binding on the container app, and unique image tags instead of `latest`. |
| Code-first gRPC | A new endpoint fails build or client generation expectations after an agent adds `.proto`-first code. | Revert the pattern change, define the contract as a C# interface in Core.Shared, implement it in Application, map it in Server, and add a CI guard preventing `<Protobuf>` items. |
| Aspire wiring | Deployment code compiles locally but `azd` publish/provision fails because an Aspire API was guessed from stale docs. | Check the exact Aspire package version, generated source, and current Aspire docs; prefer the AppHost model over ad-hoc Bicep edits unless a deliberate IaC handoff is approved. |
| Azure DevOps import | Query returns too many/missing work items. | Add pagination, query limits, explicit project/team filters, and user-facing selection; do not import broad organization-wide data silently. |
| Azure DevOps write-back | 401/403/404/409/429 or invalid estimate field. | Surface exact failure category, retain PlanDeck result, allow retry after correction, honor `Retry-After`, and never mark write-back complete on failure. |
| Migrations | App rollback required after schema change. | Stop deployment, identify migration applied, use documented rollback script/backup restore path, and require human approval. |
| Cost/quota | SQL auto-pause/quota or Log Analytics costs surprise the team. | Add alerts, review tier/retention, and upgrade or cap before real user sessions. |

## Execution todos

| ID | Indicator | Title |
| --- | --- | --- |
| repo-guardrails | Planned | Add repository and deployment guardrails |
| azure-apphost | Planned | Add Azure-ready Aspire AppHost |
| sql-production-path | Planned | Implement Azure SQL and EF Core production path |
| entra-secrets | Planned | Configure Entra ID and Key Vault secret handling |
| azure-devops-boundary | Planned | Build Azure DevOps integration boundary |
| realtime-runtime | Planned | Validate real-time voting on ACA runtime |
| deployment-workflow | Planned | Establish Azure-native master auto deployment |
| observability-runbooks | Planned | Add observability, rollback, and support runbooks |
| production-readiness | Planned | Complete production readiness decision |

## Notes and open decisions

| Decision | Status |
| --- | --- |
| Azure DevOps auth model: delegated user OAuth vs service identity/PAT for MVP | Open; must be decided before implementation because it affects Entra scopes, storage, tenant isolation, and write-back ownership. |
| Real-time transport implementation details | Open; ACA supports WebSockets/gRPC, but PlanDeck still needs a concrete voting transport and reconnect model. |
| Production migration strategy | Open; development can auto-apply migrations, production needs a deliberate gated runner or manual step. |
| Azure SignalR/backplane timing | Deferred until scaling beyond one replica; document as a hard trigger before changing ACA replica count. |
| Aspire-generated infrastructure ownership | Open; start with `azd`/Aspire as source of truth, then decide later whether generated Bicep is committed as owned IaC or treated as generated output. |
| Azure Pipeline source repo | Planned assumption: use Azure Repos and Azure Pipelines for push-to-`master` deployment. If the source stays outside Azure Repos, choose an Azure-supported repository connection without introducing GitHub Actions. |
