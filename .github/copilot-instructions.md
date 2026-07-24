# PlanDeck

PlanDeck is an MVP for running SCRUM **planning-poker sessions** (import tasks, create sessions, real-time voting, save results). See `idea-notes.md` for the product shape. The codebase is still early — most domain logic lives in placeholder `Class1.cs` files — so expect to be **building** features into the existing layered structure rather than modifying finished code.

## Stack

- **.NET 10** (SDK `10.0.301`), `Nullable` and `ImplicitUsings` enabled across all projects; `LangVersion=latest` (C# `extension` members are already used — see `ServiceCollectionExtensions.cs`).
- **.NET Aspire 13.x** for local orchestration (`PlanDeck.AppHost` is the entrypoint).
- **Blazor Web App (hosted WebAssembly)**: `PlanDeck.Server` hosts the `PlanDeck.Client` WASM app via Razor Components with `InteractiveWebAssembly` render mode — they are **one deployed unit**, not separate sites.
- **MudBlazor 9.x** for client UI.
- **Code-first gRPC over gRPC-Web** using `protobuf-net.Grpc` (NOT classic `.proto`-first). Contracts are C# interfaces.
- **EF Core 10** with a SQL database; migrations applied on startup in Development.
- **Localization** (`en`, `pl`) and ASP.NET Core **authentication/authorization** (Entra ID per `idea-notes.md`) are wired in `Program.cs`.
- **Tests**: NUnit 4 + `Microsoft.NET.Test.Sdk`, coverage via `coverlet.collector`, E2E via `Microsoft.Playwright.NUnit`.

## Layout (`src/PlanDeck/`)

- `Aspire/PlanDeck.AppHost` — Aspire app host; orchestrates services. **Run this project**, not the server/client directly. Referenced by `aspire.config.json`.
- `Aspire/PlanDeck.ServiceDefaults` — shared telemetry/health/resilience wiring (`AddServiceDefaults`, `MapDefaultEndpoints`). Reference this from every new service project.
- `Web/PlanDeck.Server` — ASP.NET Core host: serves the Blazor WASM app, exposes gRPC-Web services, owns EF Core/migrations, localization, auth. DI registration lives in `Extensions/ServiceCollectionExtensions.cs` (`AddSqlDatabase` / `AddLocalServices` / `AddExternalServices`, `ApplyMigrationsAsync`).
- `Web/PlanDeck.Client` — Blazor WASM UI. Pages in `Pages/`, layout in `Layout/`. References `PlanDeck.Core.Shared` for gRPC contracts.
- `Core/` — layered domain (Clean Architecture style):
  - `PlanDeck.Core.Shared` — gRPC service **contracts** (in `Contracts/`) + DTOs shared by client and server; references `protobuf-net.Grpc`.
  - `PlanDeck.Application` — application/use-case logic.
  - `PlanDeck.Infrastructure` — data access, EF Core, external integrations (Azure DevOps).
  - `PlanDeck.Common` — cross-cutting helpers.
- `Tests/` — `PlanDeck.Unit.Tests`, `PlanDeck.Integration.Tests`, `PlanDeck.E2e.Tests` (Playwright).
- Solution is `PlanDeck.slnx` (XML solution format); every new project must be registered there.

## Build & run

Run all commands from `src/PlanDeck/`:

```powershell
dotnet build PlanDeck.slnx                              # build whole solution
dotnet run --project Aspire/PlanDeck.AppHost            # launch full app via Aspire dashboard
dotnet build Web/PlanDeck.Server/PlanDeck.Server.csproj # build one project
```

**Before running the app, make sure Podman is running** (Aspire uses it as the container runtime). Check with `podman info`; if it's not running, start it:

```powershell
podman machine start
```

### Tests

```powershell
dotnet test PlanDeck.slnx                                          # all tests
dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj   # one project
dotnet test --filter "FullyQualifiedName~MyNamespace.MyTestClass.MyTestMethod"  # a single test
```

Tests use **NUnit** (`[Test]`, `[TestFixture]`; `NUnit.Framework` is a global `using`). E2E tests need Playwright browsers installed once: `pwsh Tests/PlanDeck.E2e.Tests/bin/Debug/net10.0/playwright.ps1 install chromium` (after a build).

**E2E tests run only on the local developer environment.** `PlanDeck.E2e.Tests` references `PlanDeck.AppHost`, and its NUnit `[SetUpFixture]` (`AspireAppFixture`) always starts Aspire through `DistributedApplicationTestingBuilder.CreateAsync<Projects.PlanDeck_AppHost>()`, waits for `plandeck-server`, and exposes the local URL as `AspireAppFixture.BaseUrl`. Do not point automated tests at `rg-test` or any deployed environment. Test classes derive from Playwright's `PageTest` and override `ContextOptions()` to set `IgnoreHTTPSErrors = true` for the dev cert. Use the **Page Object Pattern**: page classes live in `Tests/PlanDeck.E2e.Tests/Pages/` (e.g. `HomePage`), wrap locators + actions, and account for WASM boot time by waiting for a known element to be visible before asserting.

## Azure deployment modes (Testing vs Production)

- `PlanDeck.AppHost` supports two explicit publish targets:
  - `PLANDECK_PUBLISH_TARGET=Testing` (or `Publishing__Target=Testing`)
  - `PLANDECK_PUBLISH_TARGET=Production` (or `Publishing__Target=Production`)
- `Testing` target is the publicly reachable `rg-test` environment for manual testing:
  - uses real Identity with local or Entra accounts
  - deploys Container App ingress as **external/public**
  - does not expose or require automated E2E scenario configuration
- `Production` target must stay externally reachable:
  - provide Entra credentials via `AZURE_ENTRA_TENANT_ID`, `AZURE_ENTRA_CLIENT_ID`, `AZURE_ENTRA_CLIENT_SECRET` (or equivalent `Authentication:Microsoft:*` config)
- For production rollout, use a dedicated production environment and keep it separate from `rg-test`.

## Conventions

- **Backend functionality is exposed as code-first gRPC** (`protobuf-net.Grpc`, served over gRPC-Web). The wire contract is a C# interface decorated with `[Service]`/`[Operation]` (from `ProtoBuf.Grpc.Configuration`) plus `[DataContract]`/`[DataMember(Order = n)]` request/reply types, placed in `PlanDeck.Core.Shared/Contracts/` so client and server share it. Do **not** use WCF `[ServiceContract]`/`[OperationContract]` (needs an extra `System.ServiceModel.Primitives` reference) and do **not** add `.proto`/`<Protobuf>` items — the legacy `greet.proto` and commented `MapGrpcService<GreeterService>` are leftover scaffolding, not the pattern.
- **gRPC service implementations live in `PlanDeck.Application`** (e.g. `Services/HelloGrpcService.cs`), implementing the `Core.Shared` contract interface — **not** in the Web host. `PlanDeck.Server` references `Application` and only wires the endpoint in `Program.cs` via `app.MapGrpcService<T>()`. Keep business logic out of `PlanDeck.Server`.
- **Client-side service wrappers go behind an interface** in `PlanDeck.Client/Services/` (e.g. `IHelloClientService` + `HelloClientService`), registered by interface (`AddScoped<IHelloClientService, HelloClientService>()`) and injected by interface into components. They call gRPC via an injected `GrpcChannel` (configured in `Program.cs` with `GrpcWebHandler` at the host base address) and `channel.CreateGrpcService<TContract>()`. The client needs `Grpc.Net.Client`, `Grpc.Net.Client.Web`, and `protobuf-net.Grpc` package references.
- **Server DI goes through the `extension(IServiceCollection)` blocks** in `Extensions/ServiceCollectionExtensions.cs` (`AddSqlDatabase`, `AddLocalServices`, `AddExternalServices`), composed in `Program.cs`. Add new registrations there rather than inline.
- **Layer dependencies flow inward**: `Server` → `Application` → `Infrastructure`/`Core.Shared`; `Common` is shared. Domain/application code must not reference ASP.NET Core, MudBlazor, or gRPC hosting types. Both client and server depend on `Core.Shared` for the wire contracts.
- New service/worker projects must call `builder.AddServiceDefaults()` and `app.MapDefaultEndpoints()`, then be registered in `AppHost.cs` via `builder.AddProject<Projects.X>("name")`.
- Client UI uses MudBlazor components (`MudText`, `MudAlert`, …); `AddMudServices()` is registered (server-side via `AddExternalServices`) and MudBlazor namespaces are in `_Imports.razor`. Don't hand-roll markup MudBlazor already provides.
- **Blazor views use `.razor.cs` code-behind, never `@code`**: every component (Pages, Layout, shared components) keeps its C# in a `partial class` in a sibling `<Name>.razor.cs` file — do **not** put logic in an `@code { }` block in the `.razor`. The `.razor` holds only markup and directives (`@page`, `@inject`, `@using`, `@attribute`, etc.); put `@implements`/interface declarations and `[Parameter]` properties on the code-behind partial class instead. Match the component's namespace by folder (`Pages/` → `PlanDeck.Client.Pages`, `Layout/` → `PlanDeck.Client.Layout`) and the class name to the file. **Gotcha:** `@using` in the `.razor` and `_Imports.razor` do **not** apply to the `.razor.cs`, so add explicit `using` statements there (ImplicitUsings already covers `System`/`System.Linq`/`System.Collections.Generic`/`System.Threading.Tasks`). Do **not** restate the base class (`ComponentBase`/`LayoutComponentBase`) — the generated partial already provides it.
- User-facing strings are localized (`en`/`pl`); don't hard-code display text that should be resource-driven.

## Code quality (required)

- Write **Clean Code**: follow **SOLID, KISS, DRY, and YAGNI**. Don't add abstraction or features that aren't needed yet.
- Before overriding or adding a method, **check the base class** (e.g. `ServiceDefaults` extensions, MudBlazor `ComponentBase`, EF `DbContext`) — if the base already implements the behavior, use it; **never duplicate a base-class method**.
- **ALWAYS compile after completing a task**: `dotnet build PlanDeck.slnx` (from `src/PlanDeck/`) must succeed before you consider the work done.

> Note: the `zscaler-*.pem/.cer` files at the repo root are corporate TLS certs for restricted-network NuGet/tooling access — not part of the app.

<!-- BEGIN @przeprogramowani/10x-cli -->

## 10xDevs AI Toolkit - Module 3, Lesson 4 (E2E Tests)

**For E2E tests, use the `/10x-e2e` skill.** It is the single source of truth
for the workflow — risk → seed test + rules → generate → review against the five
anti-patterns → re-prompt → verify. The skill's `references/` carry the full
rules, anti-patterns, seed pattern, and prompt-template.

A few hard rules that hold even before you invoke the skill:

- **Locators:** `getByRole` / `getByLabel` / `getByText` first; `getByTestId`
  only when accessibility attributes are ambiguous. Never CSS selectors, XPath,
  or DOM structure.
- **Never `page.waitForTimeout()`.** Wait for state: `toBeVisible()`,
  `waitForURL()`, `waitForResponse()`.
- **Test independence + cleanup.** Each test runs standalone — its own setup,
  action, assertion, and cleanup; unique ids (timestamp suffix) so parallel runs
  and re-runs don't collide.

Two boundaries to keep straight:

- **DOM (snapshot) is the default.** Vision (`--caps=vision`) is a supplement for
  visual-only risks (layout, z-index, animation); for pixel regression prefer
  deterministic tools (`toMatchSnapshot`, Argos, Lost Pixel). VLM model
  selection/cost is a debugging topic (Lesson 5), not testing.
- **Healer helps on selectors, harms on logic.** A changed selector → healer
  re-finds it (route through PR review). A changed business behavior → healer
  masks the bug; that failing-test-to-fix case is Lesson 5.

<!-- END @przeprogramowani/10x-cli -->
