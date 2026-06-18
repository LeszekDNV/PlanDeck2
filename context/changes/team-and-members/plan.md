# Team & Members (S-01) Implementation Plan

## Overview

Deliver FR-002: a signed-in user can create a team and add members to it. This is the first data-backed slice built on the F-01 multi-tenant persistence baseline. Per the user's scope choices, the slice also establishes two reusable client foundations the codebase lacks today — **client-side authentication state** (AuthenticationStateProvider + AuthorizeView + login/logout) and **WASM localization** (resx + `IStringLocalizer`, en/pl) — because the Teams UI must be auth-gated and fully localized.

## Current State Analysis

- **F-01 persistence convention is in place.** Adding a tenant-scoped entity is mechanical: inherit `TenantEntity` (`Core/PlanDeck.Application/Domain/TenantEntity.cs`), add an `IEntityTypeConfiguration<T>` (auto-applied by `ApplyConfigurationsFromAssembly` in `PlanDeckDbContext.OnModelCreating`, `PlanDeckDbContext.cs:21`), and expose a `DbSet`. The global query filter (`PlanDeckDbContext.cs:49-53`) and write-time tenant stamping + fail-closed guards (`StampTenantOnInsert` `:89`, `GuardTenantOwnership` `:110`) then apply automatically.
- **Layering (verified):** `Infrastructure → Application → Core.Shared`. `PlanDeck.Application` references **only** `Core.Shared` (`PlanDeck.Application.csproj:13-15`) and therefore **cannot see `PlanDeckDbContext`**. The established pattern (`AzureDevOpsWorkItemGrpcService.cs:7`) is: define an abstraction in `Application/Abstractions/`, implement it in `Infrastructure`, and have the gRPC service depend on the abstraction. Team CRUD must follow this — a `ITeamRepository` in Application, implemented over `PlanDeckDbContext` in Infrastructure.
- **gRPC is code-first** (`protobuf-net.Grpc`). Contract = a `[Service]` interface + `[DataContract]`/`[DataMember(Order=n)]` DTOs in `Core.Shared/Contracts/` (see `IHelloService.cs`, `IAzureDevOpsWorkItemService.cs`). Implementation lives in `Application/Services/`, is registered scoped in `ServiceCollectionExtensions.AddLocalServices()` (`:94-102`), and mapped via `app.MapGrpcService<T>()` in `Program.cs` (`:60-61`).
- **Client wraps gRPC behind an interface** (`Client/Services/IHelloClientService` + `HelloClientService`), using an injected `GrpcChannel` and `channel.CreateGrpcService<TContract>()` (`HelloClientService.cs:18`), registered by interface in `Client/Program.cs` (`:16-21`).
- **The app is a STANDALONE Blazor WASM app served by ASP.NET Core**, NOT a Razor-Components "Blazor Web App". There are **no server-side `.razor` components** — the server (`Sdk.Web`) serves `Client/wwwroot/index.html` via `MapFallbackToFile("index.html")` (`Program.cs:65`) and the client `App.razor` hosts a `<Router>` directly. Consequence: the .NET 8+ `PersistentComponentState` auth pattern does **not** apply. The server already authenticates via cookie + OIDC (`AddExternalServices`, `ServiceCollectionExtensions.cs:51-92`); same-origin gRPC-Web calls carry the auth cookie automatically. The correct pattern here is a server endpoint exposing the current identity + a client `AuthenticationStateProvider` that reads it.
- **No client auth packages, no AuthorizeView, no nav chrome.** `MainLayout.razor` renders only `@Body` (no AppBar/Drawer). `Microsoft.AspNetCore.Components.Authorization` is not referenced by the Client.
- **Localization is half-wired.** Server has `AddLocalization` + `UseRequestLocalization(en, pl)` (`Program.cs:12,44-50`) but **there are zero `.resx` files anywhere**, and the WASM client has no localization services. The only page hardcodes English.
- **`AppUser` is unused at runtime** — nothing creates `AppUser` rows; it exists as the F-01 example entity.
- **Identity claims:** `HttpContextCurrentUserContext` reads `tid`→TenantId and `oid`→UserId (`:11-13`) with `MapInboundClaims = false` (`ServiceCollectionExtensions.cs:83`), so raw OIDC claim types are preserved. DisplayName/Email are not yet surfaced.
- **Integration tests** boot the real app via Aspire (`AspireAppFixture.cs`), wait for `plandeck-server` Running (migration applied), and build their own `PlanDeckDbContext` with a `FakeCurrentUserContext` against the live MSSQL (`TenantPersistenceTests.cs:1-45`). The existing `InitialCreate` migration id is `20260618140615_InitialCreate`.

### Key Discoveries:

- Add-an-entity is 3 mechanical edits + 1 migration — no DbContext logic changes (`PlanDeckDbContext.cs:19-33`).
- Team CRUD must route through an Application abstraction; the service cannot touch EF directly (`PlanDeck.Application.csproj`).
- Standalone-WASM + server-cookie auth ⇒ client learns identity through a gRPC `IAuthService`, not prerendered state.
- Flat role model (PRD `Business Logic Changes`, line 135): `TeamMember` has **no** role field.
- Members are identified by **email (invite-style)** and the team creator by **oid only** (per user decisions) — no `AppUser` provisioning in this slice.

## Desired End State

A signed-in user lands on the app, sees their identity and a Teams nav entry in the header, opens `/teams`, creates a team, and adds/removes members by email — all UI strings available in English and Polish via a header language switch. Every team and member is tenant-isolated by the F-01 convention. Verified by: green build; integration tests proving Team/TeamMember tenant isolation + the member uniqueness constraint on real MSSQL; and a Playwright E2E (using a deterministic test-auth scheme) that creates a team and adds a member through the UI.

## What We're NOT Doing

- No `AppUser` provisioning / user directory / Entra people-picker (members are free-text email).
- No `TeamMember.Role` / permission tiers (flat model per PRD).
- No team rename/description-edit and no team deletion (out of scope; members can be removed).
- No sessions, assignments, or notifications (S-04/S-05 and later).
- No full client-side MSAL/OIDC token acquisition — auth stays cookie-based on the server; the client only reflects identity and triggers server login/logout redirects.
- No language switch persisted server-side beyond a browser `localStorage` value; no per-user culture preference entity.
- No new infra/deployment work (F-03).

## Implementation Approach

Build inward-out in six phases: (1) domain + persistence so the data shape and isolation are real and tested-against; (2) the gRPC contract + Application service + Infrastructure repository so the capability exists server-side; (3) the auth foundation (server identity endpoint + login/logout + E2E test scheme, and the client auth-state plumbing) so the UI can be gated and know the user; (4) the localization foundation so UI strings are en/pl; (5) the Teams UI consuming all of the above; (6) integration + E2E tests. Phases 1–2 are pure F-01/gRPC pattern-following. Phases 3–4 establish reusable foundations and carry the slice's real risk. Phase 5 is composition. Phase 6 locks in the tenant-isolation invariant and the end-to-end loop.

## Critical Implementation Details

- **Application cannot reference Infrastructure.** The gRPC service depends on `ITeamRepository` (Application abstraction); the EF implementation lives in Infrastructure and is registered in Server DI. Do not add an Application→Infrastructure project reference — it inverts the layering.
- **`CreatedByUserId` / `InvitedByUserId` are domain fields, not stamped by the DbContext.** Only `TenantId` + audit timestamps are auto-stamped (`StampTenantAndAudit`). The repository sets the user ids explicitly from `ICurrentUserContext.UserId`.
- **Member uniqueness is enforced by a DB unique index** `(TenantId, TeamId, Email)`. A duplicate add surfaces as `DbUpdateException`; the service must translate it to a clean gRPC error, not a 500.
- **Test-auth scheme must be strictly environment-gated.** It authenticates every request with a fixed tenant/oid and must only activate under the E2E run (config flag / dedicated environment), never in Development or Production.
- **WASM localization requires `<BlazorWebAssemblyLoadAllGlobalizationData>true</BlazorWebAssemblyLoadAllGlobalizationData>`** in the Client csproj, otherwise non-invariant cultures (pl) silently fail to format. Culture is selected client-side (localStorage) and applied before `RunAsync()`.

---

## Phase 1: Domain Model & Persistence

### Overview

Add `Team` and `TeamMember` tenant-scoped entities, their EF configurations, `DbSet`s, and a migration.

### Changes Required:

#### 1. Domain entities

**File**: `src/PlanDeck/Core/PlanDeck.Application/Domain/Team.cs`

**Intent**: A team owned by a tenant and created by a specific user; the membership root for later slices.

**Contract**: `public sealed class Team : TenantEntity` with `public required string Name { get; set; }`, `public string? Description { get; set; }`, `public Guid CreatedByUserId { get; set; }`. Inherits `Id`, `TenantId`, `CreatedAtUtc`, `UpdatedAtUtc` from `TenantEntity`.

**File**: `src/PlanDeck/Core/PlanDeck.Application/Domain/TeamMember.cs`

**Intent**: An invited member of a team, identified by email (no account required yet). No role field (flat model).

**Contract**: `public sealed class TeamMember : TenantEntity` with `public Guid TeamId { get; set; }`, `public required string Email { get; set; }`, `public string? DisplayName { get; set; }`, `public Guid InvitedByUserId { get; set; }`.

#### 2. EF configurations

**File**: `src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/Configurations/TeamConfiguration.cs`

**Intent**: Map `Team`, mirroring `AppUserConfiguration` conventions, with a one-to-many to `TeamMember`.

**Contract**: `IEntityTypeConfiguration<Team>` → `ToTable("Teams")`; `HasKey(Id)` + `Id ValueGeneratedNever()`; `Name` required, `HasMaxLength(200)`; `Description` `HasMaxLength(1024)`; `HasIndex(TenantId)`; `HasMany<TeamMember>().WithOne().HasForeignKey(m => m.TeamId).OnDelete(DeleteBehavior.Cascade)`.

**File**: `src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/Configurations/TeamMemberConfiguration.cs`

**Intent**: Map `TeamMember` and enforce one membership per email per team per tenant.

**Contract**: `IEntityTypeConfiguration<TeamMember>` → `ToTable("TeamMembers")`; `HasKey(Id)` + `Id ValueGeneratedNever()`; `Email` required, `HasMaxLength(320)`; `DisplayName` `HasMaxLength(256)`; `HasIndex(TenantId)`; unique `HasIndex(m => new { m.TenantId, m.TeamId, m.Email }).IsUnique()`.

#### 3. DbContext DbSets

**File**: `src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/PlanDeckDbContext.cs`

**Intent**: Expose the two new sets so EF maps them and the tenant filter/stamping apply automatically.

**Contract**: add `public DbSet<Team> Teams => Set<Team>();` and `public DbSet<TeamMember> TeamMembers => Set<TeamMember>();` alongside `AppUsers` (`:15`). No other change — `OnModelCreating` already discovers `ITenantScoped` types by reflection.

#### 4. Migration

**File**: `src/PlanDeck/Core/PlanDeck.Infrastructure/Migrations/<timestamp>_AddTeamsAndTeamMembers.cs` (+ Designer + snapshot update)

**Intent**: Create `Teams` and `TeamMembers` tables with the unique index.

**Contract**: generated via `dotnet ef migrations add AddTeamsAndTeamMembers --project Core/PlanDeck.Infrastructure --startup-project Web/PlanDeck.Server --output-dir Migrations` (run from `src/PlanDeck/`). The design-time `PlanDeckDbContextFactory` supplies a no-op `ICurrentUserContext`.

### Success Criteria:

#### Automated Verification:

- Build passes: `dotnet build PlanDeck.slnx`
- Migration file is generated and the model snapshot includes `Teams` + `TeamMembers`
- Migration applies cleanly on app startup in Development (covered by the integration-test boot in Phase 6)

#### Manual Verification:

- Reviewed the migration `Up()` creates both tables and the unique index `(TenantId, TeamId, Email)`

**Implementation Note**: After automated verification passes, pause for manual confirmation before Phase 2.

---

## Phase 2: gRPC Contract, Application Service & Repository

### Overview

Expose Team CRUD over code-first gRPC: an Application-layer `ITeamRepository` abstraction, its Infrastructure implementation over `PlanDeckDbContext`, the `ITeamService` contract + DTOs, the `TeamGrpcService` implementation, and server wiring.

### Changes Required:

#### 1. Repository abstraction (Application)

**File**: `src/PlanDeck/Core/PlanDeck.Application/Abstractions/ITeamRepository.cs`

**Intent**: The data-access surface the gRPC service depends on, keeping Application free of EF.

**Contract**: methods (all `CancellationToken`-bearing, returning domain types):
`Task<Team> CreateTeamAsync(string name, string? description, CancellationToken ct)`;
`Task<IReadOnlyList<Team>> GetTeamsAsync(CancellationToken ct)`;
`Task<IReadOnlyList<TeamMember>> GetMembersAsync(Guid teamId, CancellationToken ct)`;
`Task<TeamMember> AddMemberAsync(Guid teamId, string email, string? displayName, CancellationToken ct)`;
`Task<bool> RemoveMemberAsync(Guid teamId, Guid memberId, CancellationToken ct)`.

#### 2. Repository implementation (Infrastructure)

**File**: `src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/TeamRepository.cs`

**Intent**: Implement the abstraction over `PlanDeckDbContext`; rely on the global tenant filter for scoping; set creator/inviter ids from the current user.

**Contract**: `sealed class TeamRepository(PlanDeckDbContext db, ICurrentUserContext currentUser) : ITeamRepository`. `CreateTeamAsync` sets `CreatedByUserId = currentUser.UserId` (TenantId + audit auto-stamped) and `SaveChangesAsync`. `AddMemberAsync` first verifies the team exists in the current tenant (`db.Teams.FindAsync`/`AnyAsync` — filter-scoped) and throws a typed not-found if absent; sets `InvitedByUserId = currentUser.UserId`. `GetTeamsAsync`/`GetMembersAsync` are filtered reads (`GetMembersAsync` additionally `Where(m => m.TeamId == teamId)`). `RemoveMemberAsync` loads the member (filter-scoped) and removes it; returns false if not found.

#### 3. gRPC contract + DTOs (Core.Shared)

**File**: `src/PlanDeck/Core/PlanDeck.Core.Shared/Contracts/ITeamService.cs`

**Intent**: The shared wire contract for Team CRUD.

**Contract**: `[Service] interface ITeamService` with `[Operation]` methods `CreateTeamAsync`, `ListTeamsAsync`, `AddMemberAsync`, `RemoveMemberAsync`, `ListMembersAsync` (each `Task<TReply>(TRequest request, CallContext context = default)`). DTOs (`[DataContract]` + ordered `[DataMember]`): `TeamDto { Guid Id, string Name, string? Description, DateTimeOffset CreatedAtUtc }`; `TeamMemberDto { Guid Id, Guid TeamId, string Email, string? DisplayName }`; `CreateTeamRequest { string Name, string? Description }` / `CreateTeamReply { TeamDto Team }`; `ListTeamsReply { List<TeamDto> Teams }`; `AddMemberRequest { Guid TeamId, string Email, string? DisplayName }` / `AddMemberReply { TeamMemberDto Member }`; `RemoveMemberRequest { Guid TeamId, Guid MemberId }` / `RemoveMemberReply { bool Removed }`; `ListMembersRequest { Guid TeamId }` / `ListMembersReply { List<TeamMemberDto> Members }`.

#### 4. gRPC service implementation (Application)

**File**: `src/PlanDeck/Core/PlanDeck.Application/Services/TeamGrpcService.cs`

**Intent**: Implement `ITeamService` by validating input, delegating to `ITeamRepository`, and mapping domain↔DTO.

**Contract**: `sealed class TeamGrpcService(ITeamRepository repository) : ITeamService`. Validate: name non-empty/whitespace and email non-empty + minimal shape → throw `RpcException(new Status(StatusCode.InvalidArgument, ...))`. Translate the unique-index `DbUpdateException` from a duplicate member into `RpcException(StatusCode.AlreadyExists, ...)`. Map entities to DTOs inline.

#### 5. Server DI + endpoint mapping

**File**: `src/PlanDeck/Web/PlanDeck.Server/Extensions/ServiceCollectionExtensions.cs`

**Intent**: Register the repository and service.

**Contract**: in `AddLocalServices()` (`:94`) add `services.AddScoped<ITeamRepository, TeamRepository>();` and `services.AddScoped<TeamGrpcService>();`.

**File**: `src/PlanDeck/Web/PlanDeck.Server/Program.cs`

**Intent**: Expose the endpoint.

**Contract**: add `app.MapGrpcService<TeamGrpcService>();` next to the existing maps (`:60-61`).

### Success Criteria:

#### Automated Verification:

- Build passes: `dotnet build PlanDeck.slnx`
- The Team persistence/isolation behavior is exercised green in Phase 6 integration tests (repository + entities)

#### Manual Verification:

- Walked the contract: every `ITeamService` operation has a request/reply DTO and a `TeamGrpcService` implementation that delegates to the repository

**Implementation Note**: Pause for manual confirmation before Phase 3.

---

## Phase 3: Authentication Foundation (Server + Client)

### Overview

Give the client knowledge of the signed-in identity and the ability to log in/out, and add a deterministic test-auth scheme for E2E. Server: surface identity over gRPC + add login/logout endpoints + test-auth handler. Client: `AuthenticationStateProvider`, `CascadingAuthenticationState`, `AuthorizeView`, and a header showing the user.

### Changes Required:

#### 1. Extend the identity abstraction

**File**: `src/PlanDeck/Core/PlanDeck.Application/Abstractions/ICurrentUserContext.cs`

**Intent**: Expose display name + email so the UI can show the user.

**Contract**: add `string? DisplayName { get; }` and `string? Email { get; }` to the interface.

**File**: `src/PlanDeck/Web/PlanDeck.Server/Identity/HttpContextCurrentUserContext.cs`

**Intent**: Read the new claims (`MapInboundClaims = false`, so use raw OIDC claim types).

**Contract**: implement `DisplayName` from `name` (fallback `preferred_username`) and `Email` from `email` (fallback `preferred_username`), returning null when unauthenticated.

**File**: `src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/PlanDeckDbContextFactory.cs`

**Intent**: Keep the design-time no-op context compiling.

**Contract**: implement the two new members returning `null`.

#### 2. Identity gRPC contract + service

**File**: `src/PlanDeck/Core/PlanDeck.Core.Shared/Contracts/IAuthService.cs`

**Intent**: Let the WASM client read who is signed in.

**Contract**: `[Service] interface IAuthService` with `[Operation] Task<CurrentUserReply> GetCurrentUserAsync(CurrentUserRequest request, CallContext context = default)`. `CurrentUserRequest {}` (empty); `CurrentUserReply { bool IsAuthenticated, string? DisplayName, string? Email }`.

**File**: `src/PlanDeck/Core/PlanDeck.Application/Services/AuthGrpcService.cs`

**Intent**: Return the current identity from `ICurrentUserContext`.

**Contract**: `sealed class AuthGrpcService(ICurrentUserContext currentUser) : IAuthService`, mapping `IsAuthenticated`/`DisplayName`/`Email`. Register scoped in `AddLocalServices()` and `app.MapGrpcService<AuthGrpcService>()` in `Program.cs`.

#### 3. Login / logout endpoints

**File**: `src/PlanDeck/Web/PlanDeck.Server/Program.cs`

**Intent**: Trigger the OIDC challenge and sign-out from the browser.

**Contract**: minimal-API map: `GET /auth/login?returnUrl=` → `Results.Challenge` with `AuthenticationProperties { RedirectUri = returnUrl ?? "/" }` against the OIDC scheme (cookie-only when OIDC unconfigured); `GET /auth/logout` → sign out of the cookie scheme (and OIDC when configured) and redirect to `/`. Place the maps after `UseAuthorization()`.

#### 4. E2E test-auth scheme (environment-gated)

**File**: `src/PlanDeck/Web/PlanDeck.Server/Identity/TestAuthenticationHandler.cs`

**Intent**: Authenticate every request with a fixed tenant/oid/name/email so Playwright can drive the UI without interactive Entra.

**Contract**: `TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>` emitting claims `tid`, `oid`, `name`, `email` (constants). In `AddExternalServices` (`ServiceCollectionExtensions.cs:51`), when a config flag (e.g. `Authentication:UseTestScheme == true`) is set, register this handler as the default authenticate/challenge scheme **instead of** cookie+OIDC. Flag must be off in Development/Production.

**File**: `src/PlanDeck/Aspire/PlanDeck.AppHost/AppHost.cs`

**Intent**: Allow the E2E run to enable the flag on the server resource.

**Contract**: pass `Authentication__UseTestScheme=true` to the `plandeck-server` resource only when the AppHost is launched by the E2E fixture (e.g. via an environment variable the fixture sets, read in AppHost and forwarded with `.WithEnvironment(...)`). No effect on normal `dotnet run`.

#### 5. Client auth plumbing

**File**: `src/PlanDeck/Web/PlanDeck.Client/PlanDeck.Client.csproj`

**Intent**: Add the authorization package.

**Contract**: `<PackageReference Include="Microsoft.AspNetCore.Components.Authorization" Version="10.*" />`.

**File**: `src/PlanDeck/Web/PlanDeck.Client/Services/GrpcAuthenticationStateProvider.cs`

**Intent**: Build `AuthenticationState` from `IAuthService`, with a refresh hook after login.

**Contract**: `class GrpcAuthenticationStateProvider(GrpcChannel channel) : AuthenticationStateProvider`. `GetAuthenticationStateAsync` calls `IAuthService.GetCurrentUserAsync`; when authenticated builds a `ClaimsPrincipal` with a non-empty authentication type + name/email claims, else an anonymous principal. Expose `NotifyAuthenticationStateChanged` re-fetch. Tolerate transport errors by returning anonymous.

**File**: `src/PlanDeck/Web/PlanDeck.Client/Program.cs`

**Intent**: Register auth services.

**Contract**: `builder.Services.AddAuthorizationCore();` and `builder.Services.AddScoped<AuthenticationStateProvider, GrpcAuthenticationStateProvider>();`.

**File**: `src/PlanDeck/Web/PlanDeck.Client/App.razor` (+ `_Imports.razor`)

**Intent**: Provide cascading auth state to the tree.

**Contract**: wrap `<Router>` in `<CascadingAuthenticationState>`; add `@using Microsoft.AspNetCore.Components.Authorization` to `_Imports.razor`.

#### 6. Header chrome with user state

**File**: `src/PlanDeck/Web/PlanDeck.Client/Layout/MainLayout.razor`

**Intent**: Add a top bar showing the signed-in user with login/logout, plus a Teams nav link (consumed in Phase 5).

**Contract**: introduce `MudLayout`/`MudAppBar`; inside it an `<AuthorizeView>` — `Authorized` shows `context.User.Identity?.Name` + a Logout button linking to `/auth/logout`; `NotAuthorized` shows a Login button linking to `/auth/login`. Keep `@Body` in `MudMainContent`. (Language switch added in Phase 4; Teams link in Phase 5.)

### Success Criteria:

#### Automated Verification:

- Build passes: `dotnet build PlanDeck.slnx`
- Solution still builds with the new Client package restored (`packages.lock.json` updated)

#### Manual Verification:

- With Entra configured in dev, the header shows a Login button when signed out; after `/auth/login` it shows the signed-in display name; `/auth/logout` returns to anonymous
- `IAuthService.GetCurrentUserAsync` returns the correct identity for the signed-in user

**Implementation Note**: Pause for manual confirmation before Phase 4.

---

## Phase 4: Localization Foundation (WASM, en/pl)

### Overview

Establish the resx + `IStringLocalizer` pattern in the WASM client and a header language switch, so the Teams UI (Phase 5) can be fully localized.

### Changes Required:

#### 1. Client localization wiring

**File**: `src/PlanDeck/Web/PlanDeck.Client/PlanDeck.Client.csproj`

**Intent**: Enable non-invariant globalization data and add the localization package.

**Contract**: add `<BlazorWebAssemblyLoadAllGlobalizationData>true</BlazorWebAssemblyLoadAllGlobalizationData>` to the `PropertyGroup`; add `<PackageReference Include="Microsoft.Extensions.Localization" Version="10.*" />`.

**File**: `src/PlanDeck/Web/PlanDeck.Client/Program.cs`

**Intent**: Register localization and apply the persisted culture before the app runs.

**Contract**: `builder.Services.AddLocalization();`. Before `RunAsync()`, read `localStorage["BlazorCulture"]` (default `"en"`) via JS interop and set `CultureInfo.DefaultThreadCurrentCulture`/`DefaultThreadCurrentUICulture`. (Use the host's JS runtime during startup.)

#### 2. Shared resource + resx

**File**: `src/PlanDeck/Web/PlanDeck.Client/Resources/SharedResource.cs`

**Intent**: A marker type to bind `IStringLocalizer<SharedResource>` against.

**Contract**: empty `public sealed class SharedResource;` in a `Resources` namespace.

**Files**: `src/PlanDeck/Web/PlanDeck.Client/Resources/SharedResource.resx` (en, default) and `SharedResource.pl.resx` (Polish)

**Intent**: Hold every UI string for this slice keyed by name.

**Contract**: keys for header (`Nav_Teams`, `Auth_Login`, `Auth_Logout`, `Language`) and all Teams-screen strings used in Phase 5 (e.g. `Teams_Title`, `Teams_Create`, `Teams_Name`, `Teams_Description`, `Teams_Empty`, `Members_Title`, `Members_AddEmail`, `Members_AddDisplayName`, `Members_Add`, `Members_Remove`, `Members_Empty`, validation/error strings). `.resx` = English values; `.pl.resx` = Polish translations.

#### 3. Language switch in header

**File**: `src/PlanDeck/Web/PlanDeck.Client/Layout/MainLayout.razor`

**Intent**: Let the user toggle en/pl.

**Contract**: a `MudMenu`/toggle in the `MudAppBar` that writes `localStorage["BlazorCulture"]` and calls `NavigationManager.NavigateTo(uri, forceLoad: true)` to re-bootstrap under the new culture. Label via `IStringLocalizer<SharedResource>`.

### Success Criteria:

#### Automated Verification:

- Build passes: `dotnet build PlanDeck.slnx` (resx compiles; satellite resources produced)

#### Manual Verification:

- Switching the header language toggles header labels between English and Polish and the choice survives reload

**Implementation Note**: Pause for manual confirmation before Phase 5.

---

## Phase 5: Teams UI

### Overview

Add the client service wrapper and the auth-gated, localized Teams screen: list teams, create a team, list members, add/remove members.

### Changes Required:

#### 1. Client service wrapper

**File**: `src/PlanDeck/Web/PlanDeck.Client/Services/ITeamClientService.cs` + `TeamClientService.cs`

**Intent**: Hide gRPC behind a component-friendly interface (mirrors `HelloClientService`).

**Contract**: `ITeamClientService` exposes `Task<IReadOnlyList<TeamDto>> GetTeamsAsync()`, `Task<TeamDto> CreateTeamAsync(string name, string? description)`, `Task<IReadOnlyList<TeamMemberDto>> GetMembersAsync(Guid teamId)`, `Task<TeamMemberDto> AddMemberAsync(Guid teamId, string email, string? displayName)`, `Task<bool> RemoveMemberAsync(Guid teamId, Guid memberId)`. `TeamClientService(GrpcChannel channel)` calls `channel.CreateGrpcService<ITeamService>()` per call. Register both in `Client/Program.cs` (`AddScoped<ITeamClientService, TeamClientService>()`).

#### 2. Teams page

**File**: `src/PlanDeck/Web/PlanDeck.Client/Pages/Teams.razor`

**Intent**: The FR-002 screen, gated to authenticated users and fully localized.

**Contract**: `@page "/teams"`; inject `ITeamClientService` and `IStringLocalizer<SharedResource>`. Wrap content in `<AuthorizeView>` (`NotAuthorized` → prompt + Login link). MudBlazor: a list/table of the user's teams (`Teams_Empty` when none); a "Create team" action opening a `MudDialog` with name (required) + optional description; selecting a team loads and shows its members with an add-member form (email + optional display name) and a remove action per member. Surface gRPC `InvalidArgument`/`AlreadyExists` errors as `MudAlert`/snackbar using localized strings. All visible text via the localizer.

#### 3. Teams nav link

**File**: `src/PlanDeck/Web/PlanDeck.Client/Layout/MainLayout.razor`

**Intent**: Navigation entry to `/teams`.

**Contract**: add a `MudButton`/link `Href="/teams"` in the `MudAppBar`, labeled via `SharedResource.Nav_Teams`, shown within the `Authorized` branch.

### Success Criteria:

#### Automated Verification:

- Build passes: `dotnet build PlanDeck.slnx`

#### Manual Verification:

- Signed in (Entra), creating a team adds it to the list; adding a member by email shows it under the team; removing a member drops it
- Adding a duplicate email to the same team shows a friendly localized error
- All Teams-screen text switches between English and Polish via the header toggle

**Implementation Note**: Pause for manual confirmation before Phase 6.

---

## Phase 6: Tests (Integration + E2E)

### Overview

Lock in tenant isolation and the member uniqueness constraint with MSSQL integration tests, and verify the end-to-end loop with a Playwright E2E using the test-auth scheme.

### Changes Required:

#### 1. Integration tests (persistence)

**File**: `src/PlanDeck/Tests/PlanDeck.Integration.Tests/Persistence/TeamPersistenceTests.cs`

**Intent**: Prove Team/TeamMember isolation and constraints on the real Aspire-provisioned MSSQL, mirroring `TenantPersistenceTests`.

**Contract**: `[TestFixture]` using the existing `AspireAppFixture.ConnectionString` + a `CreateContext(FakeCurrentUserContext)` helper (extend the fake to implement the new `DisplayName`/`Email` members, returning null). Tests: (a) creating a team stamps `TenantId` + audit + `CreatedByUserId`; (b) teams are scoped per tenant both directions (tenant A cannot see tenant B's team and vice-versa); (c) members are scoped per tenant; (d) duplicate `(TenantId, TeamId, Email)` throws `DbUpdateException`; (e) `RemoveMemberAsync`-equivalent removes a member; (f) insert without a tenant is rejected (fail-closed); (g) cross-tenant modify/delete is guarded.

#### 2. E2E test (Playwright)

**Files**: `src/PlanDeck/Tests/PlanDeck.E2e.Tests/Pages/TeamsPage.cs` + `src/PlanDeck/Tests/PlanDeck.E2e.Tests/TeamsTests.cs` (+ enable test-auth in the E2E AppHost boot)

**Intent**: Drive the real UI (create team, add member) under the deterministic test-auth scheme.

**Contract**: a `TeamsPage` page object (Page Object Pattern, waiting for WASM boot before asserting) wrapping the create-team and add-member flows. The test boots the app (locally via the AppHost with `Authentication__UseTestScheme=true`; remotely via the `BaseUrl` run parameter against a deployed instance) so the browser is auto-authenticated, navigates to `/teams`, creates a team, adds a member, and asserts both render. Derive from Playwright's `PageTest` with `IgnoreHTTPSErrors = true` per the existing E2E convention.

### Success Criteria:

#### Automated Verification:

- Integration tests pass: `dotnet test Tests/PlanDeck.Integration.Tests/PlanDeck.Integration.Tests.csproj` (Podman running)
- E2E test passes locally: `dotnet test Tests/PlanDeck.E2e.Tests/PlanDeck.E2e.Tests.csproj` (Playwright browsers installed, Podman running)
- Full suite green: `dotnet test PlanDeck.slnx`

#### Manual Verification:

- Reviewed that the E2E test-auth path is environment-gated and cannot activate in Development/Production

**Implementation Note**: Final phase — after verification, the slice is complete.

---

## Testing Strategy

### Unit Tests:

- None added in this slice (service logic is thin mapping + validation; behavior is covered by integration tests against real MSSQL, per the chosen test depth).

### Integration Tests:

- `TeamPersistenceTests` against Aspire-provisioned MSSQL: tenant isolation (both directions), member tenant scoping, unique-member constraint, member removal, fail-closed insert, cross-tenant guard.

### E2E Tests:

- Playwright: sign-in via test-auth scheme → create team → add member → assert visible.

### Manual Testing Steps:

1. Run `dotnet run --project Aspire/PlanDeck.AppHost` (Podman started); sign in via the header Login (Entra configured in dev).
2. Create a team; confirm it appears; add a member by email; confirm it lists; remove it.
3. Add a duplicate email → friendly localized error.
4. Toggle the header language → all Teams strings switch en↔pl and persist across reload.

## Performance Considerations

CRUD volumes are trivial (a user's teams/members). Reads are tenant-filtered and indexed (`TenantId`, and the composite unique index). No pagination needed at MVP scale.

## Migration Notes

Single additive migration `AddTeamsAndTeamMembers` (new tables only; no data backfill). Applied automatically on startup in Development; applied to Azure SQL via the same `ApplyMigrationsAsync` path in deployed environments.

## References

- Roadmap item: `context/foundation/roadmap.md` → S-01 (Change ID `team-and-members`)
- PRD: FR-002 (`prd.md:78`), FR-001 (`prd.md:76`), flat role model (`prd.md:135`), tenant isolation guardrail (`prd.md:53`)
- F-01 baseline (archived): `context/archive/2026-06-18-multitenant-persistence-baseline/`
- Persistence convention: `Core/PlanDeck.Infrastructure/Persistence/PlanDeckDbContext.cs`
- gRPC contract pattern: `Core/PlanDeck.Core.Shared/Contracts/IAzureDevOpsWorkItemService.cs`; impl `Core/PlanDeck.Application/Services/AzureDevOpsWorkItemGrpcService.cs`
- Client wrapper pattern: `Web/PlanDeck.Client/Services/HelloClientService.cs`
- Integration test pattern: `Tests/PlanDeck.Integration.Tests/Persistence/TenantPersistenceTests.cs`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Domain Model & Persistence

#### Automated

- [x] 1.1 Build passes: `dotnet build PlanDeck.slnx` — 822bb6b
- [x] 1.2 Migration generated and model snapshot includes Teams + TeamMembers — 822bb6b
- [ ] 1.3 Migration applies cleanly on startup in Development (via Phase 6 boot)

#### Manual

- [x] 1.4 Reviewed migration Up() creates both tables and the unique index (TenantId, TeamId, Email) — 822bb6b

### Phase 2: gRPC Contract, Application Service & Repository

#### Automated

- [x] 2.1 Build passes: `dotnet build PlanDeck.slnx` — 520655a
- [ ] 2.2 Team persistence/isolation exercised green in Phase 6 integration tests

#### Manual

- [x] 2.3 Walked the contract: every ITeamService op has a request/reply DTO and a delegating implementation — 520655a

### Phase 3: Authentication Foundation (Server + Client)

#### Automated

- [x] 3.1 Build passes: `dotnet build PlanDeck.slnx`
- [x] 3.2 Solution builds with the new Client auth package restored (packages.lock.json updated)

#### Manual

- [x] 3.3 Header shows Login when signed out; signed-in display name after /auth/login; anonymous after /auth/logout
- [x] 3.4 IAuthService.GetCurrentUserAsync returns the correct identity for the signed-in user

### Phase 4: Localization Foundation (WASM, en/pl)

#### Automated

- [ ] 4.1 Build passes: `dotnet build PlanDeck.slnx` (resx compiles; satellite resources produced)

#### Manual

- [ ] 4.2 Header language toggle switches en↔pl and survives reload

### Phase 5: Teams UI

#### Automated

- [ ] 5.1 Build passes: `dotnet build PlanDeck.slnx`

#### Manual

- [ ] 5.2 Create team / add member / remove member work end-to-end when signed in
- [ ] 5.3 Duplicate member email shows a friendly localized error
- [ ] 5.4 All Teams-screen text switches en↔pl via the header toggle

### Phase 6: Tests (Integration + E2E)

#### Automated

- [ ] 6.1 Integration tests pass: `dotnet test Tests/PlanDeck.Integration.Tests/PlanDeck.Integration.Tests.csproj`
- [ ] 6.2 E2E test passes locally: `dotnet test Tests/PlanDeck.E2e.Tests/PlanDeck.E2e.Tests.csproj`
- [ ] 6.3 Full suite green: `dotnet test PlanDeck.slnx`

#### Manual

- [ ] 6.4 Reviewed that the E2E test-auth path is environment-gated (off in Development/Production)
