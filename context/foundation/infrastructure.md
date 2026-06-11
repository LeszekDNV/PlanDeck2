---
project: "PlanDeck"
researched_at: 2026-06-11T01:26:05+02:00
recommended_platform: "Azure Container Apps"
runner_up: "Railway"
context_type: mvp
tech_stack:
  language: "C# / .NET 10"
  framework: "ASP.NET Core + Blazor hosted WebAssembly"
  runtime: "ASP.NET Core container + WASM client; EF Core SQL database; gRPC-Web"
---

## Recommendation

**Deploy on Azure Container Apps with Azure SQL Database.**

Azure Container Apps is the strongest MVP fit for PlanDeck because the stack is already .NET/Aspire/Azure-adjacent, the app needs persistent real-time WebSocket sessions, and the team prefers Azure. The winning factors were Aspire-native `azd` deployment, documented WebSocket/gRPC ingress, co-located managed services (Azure SQL, Key Vault, App Insights), and Azure MCP support. Railway remains the best low-friction indie-PaaS runner-up, but Azure wins on managed .NET ecosystem fit and Entra/Azure DevOps alignment.

## Platform Comparison

Scoring: Pass = 2, Partial = 1, Fail = 0. Hard filters apply before shortlisting: Vercel and Netlify are dropped because they cannot host the required ASP.NET Core runtime or persistent process model.

| Platform | CLI-first | Managed/Serverless | Agent-readable docs | Stable deploy API | MCP / Integration | Total | Notes |
|---|---:|---:|---:|---:|---:|---:|---|
| Azure Container Apps | Pass | Pass | Pass | Pass | Pass | 10 | Best fit for .NET/Aspire + managed Azure services. `azd mcp` is Alpha, but Azure MCP Server is GA. |
| Railway | Pass | Partial | Pass | Pass | Pass | 9 | Excellent DX/MCP and persistent services; Postgres is a self-managed container template, not fully managed HA. |
| Render | Pass | Pass | Partial | Pass | Partial | 8 | Good Docker PaaS + managed Postgres/Valkey; gRPC-Web passthrough needs validation. |
| Fly.io | Pass | Partial | Pass | Pass | Pass | 9 | Strong container/networking fit; managed Postgres cost floor is higher and auto-stop defaults must be disabled. |
| Cloudflare Workers + Containers | Pass | Partial | Pass | Pass | Partial | 7 | Can run .NET only via Containers behind a Worker shim; no co-located managed Postgres/SQL Server. |
| Vercel | Pass | Pass | Pass | Pass | Pass | 10* | *Dropped: no .NET runtime and no persistent process/WebSocket model for this app. |
| Netlify | Pass | Pass | Pass | Pass | Pass | 10* | *Dropped: no .NET runtime and no persistent process/WebSocket model for this app. |

### Platform Notes

**Azure Container Apps** supports any .NET 10 runtime via containers and has a first-class Aspire deployment path through `Aspire.Hosting.Azure.AppContainers` and `azd up`. ACA HTTP ingress explicitly supports WebSockets and gRPC; for the MVP, a single replica with sticky sessions and `minReplicas=1` is the simplest safe configuration. Azure SQL Database, Key Vault, App Configuration, Service Bus, Blob Storage, SignalR Service, Application Insights, and Container Registry are all GA and co-located in Azure regions with managed identity support.

**Railway** is the strongest non-Azure alternative. It supports persistent services, WebSockets, HTTP/2, private networking, project-local Postgres, and a first-party MCP server. Its main drawback for PlanDeck is that Postgres is a self-managed container template rather than an Azure-style managed database service with built-in HA/PITR. Use Docker for .NET 10 to avoid Railpack version ambiguity.

**Render** is a pragmatic Docker PaaS alternative with always-on paid web services, managed Postgres/Valkey, CLI deploy/logs/rollback, and Blueprint IaC. It has no native .NET runtime, so Docker is mandatory; gRPC-Web likely works through HTTP/1.1 but should be validated with a pilot deploy.

**Fly.io** is strong technically: persistent Machines, WebSocket support, HTTP/2/gRPC-friendly proxying, and private-region Postgres. It is less attractive here because managed Postgres starts at a higher cost floor, auto-stop defaults can break live sessions, and Aspire has no native deploy integration.

**Cloudflare Workers + Containers** is not a good match despite excellent docs and CLI. Native Workers cannot run .NET; Containers require a JavaScript Worker/Durable Object shim in front of the ASP.NET Core container, gRPC-Web behavior through that shim is unproven, and the SQL story requires D1 SQLite or external Postgres via Hyperdrive.

**Vercel** and **Netlify** are hard-disqualified for this stack. They are optimized for stateless frontend/serverless workloads, do not host ASP.NET Core .NET 10 processes, and do not provide the persistent process model needed for PlanDeck's real-time planning rooms.

### Shortlisted Platforms

#### 1. Azure Container Apps (Recommended)

Azure wins because it aligns with the existing .NET 10 + Aspire architecture and the user's Azure familiarity. It gives PlanDeck a single-region container app for the ASP.NET Core host, Azure SQL for EF Core, managed identity/Key Vault for secrets, Application Insights for logs/traces, and an Aspire/`azd` path that can provision the environment consistently.

#### 2. Railway

Railway is the runner-up because it is the fastest path to a working always-on container + Postgres MVP with excellent CLI and MCP support. It loses to Azure mainly on managed-service maturity, Entra/Azure DevOps ecosystem fit, and lack of Aspire-native provisioning.

#### 3. Render

Render is third because it provides a simple Docker deployment, managed Postgres/Valkey, clear logs, and rollback operations. It is less tailored to the stack than Azure and has one must-validate item for PlanDeck: gRPC-Web behavior through Render's proxy.

## Anti-Bias Cross-Check: Azure Container Apps

### Devil's Advocate — Weaknesses

1. ACA + Aspire + `azd` can hide a lot of generated infrastructure. When deployment fails, debugging Bicep, managed identity, ACR, Log Analytics, and revisions is heavier than Railway/Render.
2. Persistent WebSockets conflict with scale-to-zero economics. PlanDeck likely needs `minReplicas=1`, which removes the cheapest serverless behavior and requires deliberate session-drain/reconnect handling.
3. Single-replica SignalR is fine for MVP, but scaling to multiple replicas requires Azure SignalR Service or Redis/backplane. Skipping this decision upfront creates a scaling cliff.
4. Azure SQL Free/serverless can auto-pause or hit vCore-second limits, creating first-user latency or confusing write-back failures.
5. Azure's permission model is powerful but verbose. Managed identity, RBAC, Key Vault references, Entra app registrations, and Azure DevOps access can slow iteration if not scripted cleanly.

### Pre-Mortem — How This Could Fail

Six months later, Azure looked like the obvious home for a .NET/Aspire app, but the team underestimated operational complexity. `azd up` worked initially, so no one documented the generated resources or ownership boundaries. Real-time sessions started dropping because ACA revisions restarted containers without a reconnection story, and the team delayed Azure SignalR/backplane decisions until real users depended on live rooms. Azure SQL Free looked attractive, but auto-pause and quota behavior caused intermittent first-use latency and confusing write-back failures. Debugging required jumping between ACA logs, Application Insights, Key Vault, managed identities, and Azure DevOps permissions. The team was familiar with Azure generally, but not with this specific ACA + Aspire deployment surface, so the platform felt heavier than the MVP deserved.

### Unknown Unknowns

- Aspire ACA deployment uses generated infrastructure; decide whether `azd`-generated Bicep is the source of truth or whether the team will take ownership of it.
- ACA sticky sessions have revision-mode constraints; blue/green deploys can interact badly with active planning rooms if not tested.
- gRPC-Web over ACA is likely compatible, but PlanDeck should validate this exact Blazor WASM + ASP.NET Core gRPC-Web middleware setup in a pilot deploy.
- Managed identity reduces secrets but increases RBAC setup complexity; bad role scoping can block Azure SQL/Key Vault at runtime.
- App rollback and database migrations are separate; ACA revision rollback does not undo EF Core migrations or Azure DevOps write-backs.

## Operational Story

- **Infrastructure operations**: Use Azure CLI (`az`) as the primary interface for Azure infrastructure operations (resource groups, Container Apps, revisions, logs, RBAC, Key Vault, Azure SQL, and diagnostics). Use `azd` only for Aspire-oriented environment provisioning/deployment workflows where explicitly called out; when inspecting or changing live Azure resources, prefer explicit `az ...` commands.
- **Preview deploys**: Use `azd` environments or ACA revisions per branch/PR once CI is added. For MVP, create a separate `plandeck-dev` environment and map production manually; PR previews should be protected because the app uses Entra/Azure DevOps credentials.
- **Secrets**: Store production secrets in Azure Key Vault and reference them via managed identity from ACA. Store CI credentials in GitHub Actions secrets only when CI is added. Humans rotate primary secrets; agents may read secret names/configuration but not secret values.
- **Rollback**: ACA creates revisions. Roll back app code by shifting 100% traffic to the previous known-good revision with `az containerapp ingress traffic set ... --revision-weight <revision>=100`. EF Core migrations and Azure DevOps write-backs do not roll back automatically; risky migrations need separate rollback scripts.
- **Approval**: Humans approve production environment creation, production traffic shifts, RBAC/managed identity changes, Key Vault secret rotation, database deletion, and schema migrations that can lose data. Agents may deploy to dev, read logs, propose Bicep/azd changes, and run non-destructive diagnostics.
- **Logs**: Agents can read runtime logs with `az containerapp logs show --name <app> --resource-group <rg> --follow`, inspect revisions with `az containerapp revision list`, and query Application Insights/Log Analytics read-only once configured.

## Risk Register

| Risk | Source | Likelihood | Impact | Mitigation |
|---|---|---:|---:|---|
| ACA generated infrastructure becomes opaque | Devil's advocate | M | M | Commit generated Bicep/azure.yaml, document ownership, and keep `azd` as the source of truth until there is a deliberate IaC handoff. |
| WebSocket sessions drop during scale/revision changes | Devil's advocate / Pre-mortem | M | H | Use single replica + sticky sessions for MVP, set `minReplicas=1`, test revision traffic changes with active rooms, and implement reconnect/session recovery. |
| Scaling beyond one replica breaks SignalR room state | Unknown unknowns | M | H | Defer multi-replica scaling until needed, but document the trigger: add Azure SignalR Service or Redis backplane before `maxReplicas > 1`. |
| Azure SQL Free/serverless latency or quota surprises | Devil's advocate | M | M | Monitor vCore seconds and first-query latency; upgrade to paid serverless/basic tier before real customer use if auto-pause affects sessions. |
| Managed identity/RBAC misconfiguration blocks runtime access | Unknown unknowns | M | M | Provision SQL/Key Vault roles through `azd`/Bicep; add startup health checks that fail clearly when identity lacks access. |
| gRPC-Web behavior differs from docs in PlanDeck's exact middleware setup | Research finding / Unknown unknowns | L | H | Pilot deploy a minimal gRPC-Web endpoint before committing final deploy plan; keep gRPC-Web (not native browser gRPC) as the browser contract. |
| App rollback does not undo data changes | Pre-mortem | M | H | Treat EF Core migrations as separate deploy gates; require human approval for destructive migrations and write migration rollback notes. |
| Azure platform complexity slows MVP iteration | Pre-mortem | M | M | Start with the smallest ACA + Azure SQL architecture; defer Service Bus, Azure SignalR, and multi-environment complexity until the PRD requires them. |
| `azd mcp` command surface changes | Research finding | M | L | Use Azure MCP Server (GA) for agent visibility, but do not script production deploys around `azd mcp` while it is Alpha. |

## Getting Started

1. Confirm the app builds locally from the solution root:

   ```powershell
   Set-Location C:\Projects\10xKurs\src\PlanDeck
   dotnet build PlanDeck.slnx
   ```

2. Keep Aspire as the deployment model and add Azure Container Apps support to the AppHost path. Use `azd init` from `src\PlanDeck` after the AppHost is ready to describe the ACA environment:

   ```powershell
   Set-Location C:\Projects\10xKurs\src\PlanDeck
   azd auth login
   azd init
   ```

3. Configure ACA for an interactive single-region MVP: `minReplicas=1`, single revision mode, sticky sessions enabled, HTTP ingress on the ASP.NET Core container port, and Azure SQL in the same region. Keep Azure SignalR Service out of v1 unless scaling beyond one replica.

4. Deploy a pilot environment with `azd up`, then validate the exact real-time stack before production: hosted Blazor loads, gRPC-Web unary calls work, WebSocket/SignalR voting stays connected through a full hidden-vote/reveal round, and Azure SQL access works via managed identity.

5. Before production traffic, document the rollback command for ACA revisions and the migration policy for EF Core changes. App revision rollback is fast; database rollback is a separate human-approved action.

## Out of Scope

The following were not evaluated in this research:

- Docker image configuration
- CI/CD pipeline setup
- Production-scale architecture (multi-region, HA, DR)
