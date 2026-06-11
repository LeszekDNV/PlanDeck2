---
project: "PlanDeck"
assessed_at: 2026-06-11
agent_readiness: ready-with-compensation
context_type: brownfield
stack_components:
  language: "C# (.NET 10)"
  framework: "ASP.NET Core + Blazor Web App (hosted WebAssembly)"
  build_tool: "dotnet / MSBuild"
  test_runner: "NUnit 4 (+ Playwright for E2E)"
  package_manager: "NuGet"
  ci_provider: null
  deployment_target: null
gates_passed: 3
gates_failed: 1
---

## Stack Components

- **Language — C# on .NET 10**: Typed by the language. Every project enables `Nullable` and `ImplicitUsings`, with `LangVersion=latest` (C# `extension` members are already in use). Strong, source-readable contracts throughout.
- **Web framework — ASP.NET Core + Blazor Web App**: `PlanDeck.Server` (`Microsoft.NET.Sdk.Web`) hosts the `PlanDeck.Client` WebAssembly app via Razor Components with the `InteractiveWebAssembly` render mode — one deployed unit. Mainstream, heavily-conventioned Microsoft stack.
- **UI — MudBlazor 9.x**: The dominant component library in the Blazor ecosystem; registered via `AddMudServices()`, namespaces in `_Imports.razor`.
- **RPC layer — code-first gRPC over gRPC-Web (`protobuf-net.Grpc`)**: Contracts are C# interfaces decorated with `[Service]`/`[Operation]` plus `[DataContract]`/`[DataMember(Order=n)]` types in `PlanDeck.Core.Shared`. This is deliberately **not** the `.proto`-first `Grpc.AspNetCore` pattern that dominates .NET gRPC examples. (A stale `greet.proto` + `<Protobuf>` item remains in `PlanDeck.Server.csproj` as leftover scaffolding — it contradicts the chosen pattern and should be removed.)
- **Data — EF Core 10 + SQL database**: Migrations applied on startup in Development. Registered through `AddSqlDatabase` in `ServiceCollectionExtensions`.
- **Orchestration — .NET Aspire 13.x**: `PlanDeck.AppHost` is the run entrypoint; `PlanDeck.ServiceDefaults` provides shared telemetry/health/resilience. Requires a container runtime (Podman) locally.
- **Tests — NUnit 4**: `Microsoft.NET.Test.Sdk` 18.x, `coverlet.collector`, `NUnit.Analyzers`, `NUnit3TestAdapter`; E2E via `Microsoft.Playwright.NUnit` with an Aspire-aware `[SetUpFixture]`.
- **Build / packaging**: `dotnet`/MSBuild over the XML solution format `PlanDeck.slnx`; packages via NuGet.
- **CI/CD & deployment**: none detected — no `.github/workflows`, `Dockerfile`, or platform manifests. Aspire's AppHost orchestrates locally only.
- **Instruction file**: `.github/copilot-instructions.md` exists and is detailed — it already documents the gRPC pattern, layer boundaries, and run/test commands.

## Quality Gate Assessment

| Component                       | Typed | Convention | Training Data | Documented | Verdict              |
|---------------------------------|-------|------------|---------------|------------|----------------------|
| Language (C# / .NET 10)         | ✓     | —          | —             | —          | pass                 |
| Framework (ASP.NET Core/Blazor) | —     | ✓          | ✓             | ✓          | pass                 |
| UI (MudBlazor 9.x)              | —     | ✓          | ✓             | ✓          | pass                 |
| RPC (code-first protobuf-net.Grpc) | —  | ~          | ✗             | ~          | pass-with-compensation |
| Data (EF Core 10)               | ✓     | ✓          | ✓             | ✓          | pass                 |
| Orchestration (.NET Aspire 13.x)| —     | ✓          | ~             | ✓          | pass-with-note       |
| Build tool (dotnet/MSBuild)     | —     | ✓          | ✓             | ✓          | pass                 |
| Test runner (NUnit 4)           | —     | —          | ✓             | ✓          | pass                 |

Legend: ✓ = pass, ✗ = fail, ~ = partial, — = not applicable

### Gate Details

**Typed — PASS.** Evidence: every `.csproj` sets `<Nullable>enable</Nullable>` and `<ImplicitUsings>enable</ImplicitUsings>` (e.g. `Web/PlanDeck.Server/PlanDeck.Server.csproj`, `Tests/PlanDeck.Unit.Tests/...csproj`). C# is statically typed by the language; gRPC contracts are typed C# interfaces + `[DataContract]` records. An agent can reason about input/output shapes from source alone.

**Convention-based — PASS (with one partial).** Evidence: ASP.NET Core + Blazor impose strong conventions (SDK-style projects, Razor component model, render modes, DI via `IServiceCollection` extensions in `Extensions/ServiceCollectionExtensions.cs`, EF `DbContext`). The layered Clean-Architecture layout (`Aspire/`, `Web/`, `Core/`, `Tests/`) is consistent and predictable. The **partial** is the RPC layer: code-first `protobuf-net.Grpc` is internally consistent here but diverges from the conventional `.proto`-first layout most .NET examples assume — an agent's default instinct will be wrong unless steered.

**Popular in training data — one FAIL, one partial (assessed within the C#/.NET family).** Evidence:
- ASP.NET Core, Blazor, EF Core, MudBlazor, NUnit are all mainstream in .NET training data — PASS.
- **Code-first `protobuf-net.Grpc` — FAIL.** Within the C# family, the dominant gRPC idiom in the training corpus is `.proto`-first `Grpc.AspNetCore` with generated stubs. Code-first protobuf-net.Grpc is a real but niche minority pattern; an agent will tend to confabulate `.proto`/`MapGrpcService<GeneratedBase>` code unless explicitly redirected.
- **.NET Aspire 13.x — partial.** Aspire is recent and fast-moving; its APIs (`AddProject<>`, `DistributedApplicationTestingBuilder`) are under-represented in older training cutoffs relative to the rest of the stack.

**Well-documented — PASS (with one partial).** Evidence: ASP.NET Core, Blazor, EF Core, MudBlazor, NUnit, and Aspire all have current, versioned official docs (Microsoft Learn / vendor sites). The **partial** is `protobuf-net.Grpc`, whose documentation is lighter and more community-driven than first-party Microsoft docs — version-specific behavior is less thoroughly covered.

## Gaps & Compensation

### Gap 1 — Code-first gRPC is niche in training data (and partially unconventional)

**Why it matters for agents**: an agent asked to add a new backend endpoint will, by default, reach for the `.proto`-first pattern (define a `.proto`, add `<Protobuf>`, generate a base class, `MapGrpcService<GeneratedBase>`). That is wrong here and will produce code that doesn't compile against the project's contracts. The leftover `greet.proto` actively reinforces the wrong instinct.

**Compensation**: the existing `copilot-instructions.md` already documents this pattern well — keep it authoritative and make the rule explicit + remove the misleading scaffolding. (See ready-to-paste additions below.)

### Gap 2 — .NET Aspire recency

**Why it matters**: agents may generate stale or hallucinated Aspire wiring. The project already encodes the correct pattern (`AddServiceDefaults()`, `MapDefaultEndpoints()`, `builder.AddProject<Projects.X>("name")`).

**Compensation**: pin the Aspire major version and state the registration ritual for new projects in the instruction file.

### Gap 3 — Missing repo-level tooling guardrails (supporting, not a gate)

No `global.json` (SDK version unpinned), no `.editorconfig` (style not codified for the agent), no central package management (`Directory.Packages.props`), and no CI workflow. None of these are agent-friendliness *gates*, but each makes the agent's output less predictable and is cheap to add. CI/dependency depth is `/10x-health-check` territory.

### Recommended Instruction File Additions

Paste into `.github/copilot-instructions.md` (most of the gRPC guidance already exists there — these tighten and de-risk it):

```markdown
## gRPC — code-first only (do NOT use .proto-first)
- Backend services are code-first gRPC over gRPC-Web via `protobuf-net.Grpc`.
  The wire contract is a C# interface in `Core/PlanDeck.Core.Shared/Contracts/`
  decorated with `[Service]`/`[Operation]` (ProtoBuf.Grpc.Configuration) plus
  `[DataContract]`/`[DataMember(Order = n)]` request/reply types.
- NEVER add `.proto` files or `<Protobuf>` MSBuild items. NEVER use
  `Grpc.AspNetCore` generated bases or WCF `[ServiceContract]`/`[OperationContract]`.
- Service implementations live in `Core/PlanDeck.Application/Services/` and
  implement the Core.Shared interface. The Server only wires the endpoint in
  `Program.cs` via `app.MapGrpcService<TImpl>()`.
- Client wrappers live behind an interface in `Web/PlanDeck.Client/Services/`,
  call `channel.CreateGrpcService<TContract>()`, and are injected by interface.
- TODO: delete the leftover `Web/PlanDeck.Server/Protos/greet.proto` and its
  `<Protobuf>` item — it contradicts the code-first pattern.

## .NET Aspire (pin: 13.x)
- Run via `dotnet run --project Aspire/PlanDeck.AppHost` (Podman must be running).
- Every new service/worker project MUST call `builder.AddServiceDefaults()` and
  `app.MapDefaultEndpoints()`, then be registered in `AppHost.cs` via
  `builder.AddProject<Projects.X>("name")` and added to `PlanDeck.slnx`.

## Repo tooling conventions
- SDK is pinned via `global.json` to the .NET 10 SDK — match it; do not bump silently.
- Follow `.editorconfig` for formatting; run `dotnet build PlanDeck.slnx` (from
  `src/PlanDeck/`) and ensure it succeeds before considering a task done.
```

> Note: items above reference `global.json` and `.editorconfig`. If you add those files (recommended — see Gap 3), the rules become enforceable; otherwise treat them as conventions to introduce.

## Summary

**Overall agent-readiness: ready-with-compensation.** Evidence: the stack passes three of the four criteria cleanly across the board — it's fully typed (C#/.NET 10, nullable enabled), built on strongly-conventioned, mainstream, well-documented Microsoft frameworks (ASP.NET Core, Blazor, EF Core, MudBlazor, NUnit). The single criterion with a real gap is **popularity in training data**, localized to two components: the **code-first `protobuf-net.Grpc`** pattern (niche vs the dominant `.proto`-first idiom) and the **recency of .NET Aspire**.

**Key strengths**: end-to-end type safety; predictable layered layout; first-party docs for almost everything; a detailed `copilot-instructions.md` that already compensates for the main gap.

**Key gaps**: agents will default to the wrong gRPC pattern unless steered (made worse by leftover `greet.proto` scaffolding); Aspire wiring may be hallucinated; no `global.json`, `.editorconfig`, central package management, or CI to anchor the agent's output.

**Recommended next step**: run `/10x-health-check` to audit dependency health, the test suite, and the (currently absent) CI/CD coverage — and to track the tooling-guardrail gaps (SDK pin, editorconfig, CI) surfaced here.
