---
title: "PlanDeck - warstwa antykorupcyjna dla transportu gRPC"
created: 2026-07-28
type: refactor-plan
---

# PlanDeck - warstwa antykorupcyjna dla transportu gRPC

## 0. Odkryty kontekst

### Deklarowana architektura

PlanDeck jest warstwowa aplikacja .NET 10: Blazor WebAssembly, ASP.NET Core,
code-first gRPC/gRPC-Web, SignalR, EF Core/SQL i Aspire
(`context/foundation/prd.md:18-25`). Produktowym rdzeniem jest obieg import ->
sesja -> glosowanie -> write-back (`context/foundation/prd.md:27-31`,
`context/foundation/prd.md:40-54`).

Dokumentacja projektu jednoczesnie:

- nakazuje umieszczac wire contracts code-first gRPC w `Core.Shared`
  (`.github/copilot-instructions.md:71-75`);
- nakazuje umieszczac implementacje gRPC w `PlanDeck.Application`
  (`.github/copilot-instructions.md:74`);
- deklaruje inward dependencies i zabrania Application/domain zalezec od
  typow hostingu gRPC (`.github/copilot-instructions.md:77`).

Te trzy reguly sa wewnetrznie sprzeczne. Obecne rozmieszczenie realizuje dwie
pierwsze, lecz lamie trzecia: przypadki uzycia implementuja bezposrednio wire
interfaces, przyjmuja `CallContext` i rzucaja `RpcException`.

`README.md` zawiera tylko nazwe projektu (`README.md:1`). Nie ma
`tech-stack.md`. `stack-assessment.md` potwierdza, ze `protobuf-net.Grpc` jest
celowo wybranym, ale niszowym wariantem transportu
(`context/foundation/stack-assessment.md:18-29`,
`context/foundation/stack-assessment.md:46-63`).

### Manifest zaleznosci zewnetrznych

| Projekt | Zaleznosci istotne dla granic |
| --- | --- |
| `Core.Shared` | `protobuf-net`, `protobuf-net.Grpc` (`src/PlanDeck/Core/PlanDeck.Core.Shared/PlanDeck.Core.Shared.csproj:9-12`) |
| `Application` | `protobuf-net.Grpc` (`src/PlanDeck/Core/PlanDeck.Application/PlanDeck.Application.csproj:9-15`) |
| `Client` | SignalR Client, MudBlazor, Markdig, `Grpc.Net.Client`, `Grpc.Net.Client.Web`, `protobuf-net.Grpc` (`src/PlanDeck/Web/PlanDeck.Client/PlanDeck.Client.csproj:14-29`) |
| `Server` | OpenID Connect, gRPC-Web, EF Design, `protobuf-net`, `protobuf-net.Grpc.AspNetCore` (`src/PlanDeck/Web/PlanDeck.Server/PlanDeck.Server.csproj:12-31`) |
| `Infrastructure` | Key Vault, MailKit, ASP.NET Core Identity EF, EF SQL Server (`src/PlanDeck/Core/PlanDeck.Infrastructure/PlanDeck.Infrastructure.csproj:10-18`) |

Warstwy kodu:

- domain i use cases: `Core/PlanDeck.Application`;
- persistence i zewnetrzne integracje: `Core/PlanDeck.Infrastructure`;
- obecne wire contracts: `Core/PlanDeck.Core.Shared/Contracts`;
- host/API: `Web/PlanDeck.Server`;
- UI i klient transportu: `Web/PlanDeck.Client`;
- testy: `Tests/`.

## 1. Zidentyfikowane przeciekajace zaleznosci

### LEAK-01: code-first gRPC (`protobuf-net.Grpc` + `Grpc.Core`)

To nie jest pojedynczy import, lecz rodzina transportowa:

- `protobuf-net.Grpc` definiuje `[Service]`, `[Operation]`, `CallContext` i
  tworzenie proxy;
- `Grpc.Core` definiuje `RpcException`, `Status` i `StatusCode`;
- `Grpc.Net.Client`/gRPC-Web buduje kanal w WASM;
- `protobuf-net.Grpc.AspNetCore` rejestruje i mapuje endpointy.

#### Wszystkie produkcyjne pliki bezposrednio znajace SDK

**Wire contracts - 7 plikow**

| Plik | Wiedza o bibliotece |
| --- | --- |
| `src/PlanDeck/Core/PlanDeck.Core.Shared/Contracts/IAuthService.cs` | `[Service]`, `[Operation]`, `CallContext` (`:2-11`) |
| `src/PlanDeck/Core/PlanDeck.Core.Shared/Contracts/IAzureDevOpsWorkItemService.cs` | `[Service]`, `[Operation]`, `CallContext` (`:2-11`) |
| `src/PlanDeck/Core/PlanDeck.Core.Shared/Contracts/IHelloService.cs` | `[Service]`, `[Operation]`, `CallContext` (`:2-11`) |
| `src/PlanDeck/Core/PlanDeck.Core.Shared/Contracts/IProjectService.cs` | `[Service]`, operacje i `CallContext` (`:2-83`) |
| `src/PlanDeck/Core/PlanDeck.Core.Shared/Contracts/ISessionMemberService.cs` | `[Service]`, operacje i `CallContext` (`:2-17`) |
| `src/PlanDeck/Core/PlanDeck.Core.Shared/Contracts/ISessionService.cs` | `[Service]`, operacje i `CallContext` (`:2-44`) |
| `src/PlanDeck/Core/PlanDeck.Core.Shared/Contracts/ITeamService.cs` | `[Service]`, operacje i `CallContext` (`:2-26`) |

Przyklad pokazuje, ze biblioteka ksztaltuje sama sygnature kontraktu:
`ISessionService` ma `[Service]`, 12 `[Operation]` i `CallContext` w kazdej
metodzie (`src/PlanDeck/Core/PlanDeck.Core.Shared/Contracts/ISessionService.cs:1-45`).

**Application - 8 plikow**

| Plik | Wiedza o bibliotece |
| --- | --- |
| `src/PlanDeck/Core/PlanDeck.Application/Services/AuthGrpcService.cs` | `CallContext` i implementacja wire interface (`:3-9`) |
| `src/PlanDeck/Core/PlanDeck.Application/Services/HelloGrpcService.cs` | `CallContext` i wire reply (`:2-8`) |
| `src/PlanDeck/Core/PlanDeck.Application/Services/AzureDevOpsWorkItemGrpcService.cs` | `RpcException`, `CallContext`, statusy (`:1-53`) |
| `src/PlanDeck/Core/PlanDeck.Application/Services/GuestAccessGuard.cs` | tworzy `RpcException`/`StatusCode` (`:1`, `:17-25`) |
| `src/PlanDeck/Core/PlanDeck.Application/Services/ProjectGrpcService.cs` | wire DTO, `CallContext`, mapowanie bledow gRPC (`:1-8`, `:26-83`, `:726-748`) |
| `src/PlanDeck/Core/PlanDeck.Application/Services/SessionGrpcService.cs` | wire DTO, `CallContext`, `RpcException` i statusy (`:1-50`, `:334-415`, `:694-736`) |
| `src/PlanDeck/Core/PlanDeck.Application/Services/SessionMemberGrpcService.cs` | wire DTO, `CallContext`, statusy (`:1-90`) |
| `src/PlanDeck/Core/PlanDeck.Application/Services/TeamGrpcService.cs` | wire DTO, `CallContext`, statusy (`:1-117`) |

`SessionGrpcService` laczy w jednej klasie walidacje domenowa, repozytoria,
integracje, wire request/reply, `CallContext` i `RpcException`
(`src/PlanDeck/Core/PlanDeck.Application/Services/SessionGrpcService.cs:1-50`).
`ProjectGrpcService` koduje mapowanie bledow infrastruktury na gRPC bezposrednio
w Application (`src/PlanDeck/Core/PlanDeck.Application/Services/ProjectGrpcService.cs:726-748`).

**Server - 1 plik**

- `src/PlanDeck/Web/PlanDeck.Server/Program.cs`: `ProtoBuf.Grpc.Server`,
  `AddCodeFirstGrpc`, `UseGrpcWeb` i siedem `MapGrpcService`
  (`:8`, `:27-31`, `:130-143`).

**Client - 20 plikow z bezposrednim SDK**

- bootstrap: `src/PlanDeck/Web/PlanDeck.Client/Program.cs:6-7,21-23`;
- proxy:
  `src/PlanDeck/Web/PlanDeck.Client/Services/AzureDevOpsClientService.cs:3,15`,
  `src/PlanDeck/Web/PlanDeck.Client/Services/GrpcAuthenticationStateProvider.cs:5,17`,
  `src/PlanDeck/Web/PlanDeck.Client/Services/HelloClientService.cs:3,18`,
  `src/PlanDeck/Web/PlanDeck.Client/Services/ProjectClientService.cs:3,11-35`,
  `src/PlanDeck/Web/PlanDeck.Client/Services/SessionClientService.cs:3,16-138`,
  `src/PlanDeck/Web/PlanDeck.Client/Services/SessionMemberClientService.cs:3,11-30`,
  `src/PlanDeck/Web/PlanDeck.Client/Services/TeamClientService.cs:3,11-59`;
- UI/policies z `Grpc.Core`:
  `src/PlanDeck/Web/PlanDeck.Client/Components/AdoImportPanel.razor.cs:1,68`,
  `src/PlanDeck/Web/PlanDeck.Client/Pages/Projects.razor:2`,
  `src/PlanDeck/Web/PlanDeck.Client/Pages/Projects.razor.cs:1,37,67,94-101`,
  `src/PlanDeck/Web/PlanDeck.Client/Pages/ProjectDetails.razor:2`,
  `src/PlanDeck/Web/PlanDeck.Client/Pages/ProjectDetails.razor.cs:1,75,94,98,124,150,184,217,240,263,294,325,350,374,408,442,506-513`,
  `src/PlanDeck/Web/PlanDeck.Client/Pages/Sessions.razor:2`,
  `src/PlanDeck/Web/PlanDeck.Client/Pages/Sessions.razor.cs:1,104,109,122,250,275,316,352,385,415,455,486,535,571,599-604,659,683,762-764`,
  `src/PlanDeck/Web/PlanDeck.Client/Pages/Teams.razor:2`,
  `src/PlanDeck/Web/PlanDeck.Client/Pages/Teams.razor.cs:1,43,72,101,137,168,204,239-261`,
  `src/PlanDeck/Web/PlanDeck.Client/Pages/VotingRoom.razor:2`,
  `src/PlanDeck/Web/PlanDeck.Client/Pages/VotingRoom.razor.cs:1,56`,
  `src/PlanDeck/Web/PlanDeck.Client/Pages/SessionPagePolicy.cs:1,14-23`.

Razem daje to 36 produkcyjnych plikow bezposrednio znajacych SDK transportu.

#### Dodatkowe pliki znajace ksztalt wire DTO

Poza bezposrednimi importami gRPC kolejnych osiem plikow klienta zna wire
contracts lub wystawia je we wlasnych portach:

- `src/PlanDeck/Web/PlanDeck.Client/Components/AdoImportDialog.razor:1`;
- `src/PlanDeck/Web/PlanDeck.Client/Components/AdoImportDialog.razor.cs:3`;
- `src/PlanDeck/Web/PlanDeck.Client/Components/AdoImportPanel.razor:2`;
- `src/PlanDeck/Web/PlanDeck.Client/Services/IAzureDevOpsClientService.cs:1,7`;
- `src/PlanDeck/Web/PlanDeck.Client/Services/IProjectClientService.cs:1,7-50`;
- `src/PlanDeck/Web/PlanDeck.Client/Services/ISessionClientService.cs:1,7-41`;
- `src/PlanDeck/Web/PlanDeck.Client/Services/ISessionMemberClientService.cs:1,7-9`;
- `src/PlanDeck/Web/PlanDeck.Client/Services/ITeamClientService.cs:1,7-9`.

Przyklad: pozornie neutralny `IProjectClientService` zwraca `ProjectDto`,
`GetProjectReply`, `ProjectMemberDto`, `ProjectTeamDto` i
`ProjectConnectionDto` z wire assembly
(`src/PlanDeck/Web/PlanDeck.Client/Services/IProjectClientService.cs:1-50`).

**Laczny zasieg LEAK-01: 44 pliki produkcyjne, 4 projekty i 5 granic
odpowiedzialnosci: wire contract, Application, host, client transport, UI.**

### LEAK-02: ASP.NET Core Identity

Identity jest lepiej odseparowane od Application i UI, lecz przecieka z
Infrastructure do hosta Server.

#### Wszystkie produkcyjne pliki runtime znajace Identity

- `src/PlanDeck/Core/PlanDeck.Infrastructure/Identity/ApplicationUser.cs:1,5`;
- `src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/PlanDeckDbContext.cs:2,13,46,50-60`;
- `src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/Configurations/AppUserConfiguration.cs:4,33-35`;
- `src/PlanDeck/Core/PlanDeck.Infrastructure/Identity/AccountProvisioningService.cs:1,12,29,41`;
- `src/PlanDeck/Core/PlanDeck.Infrastructure/Identity/LocalAccountService.cs:3,18-23,100,116,176,213`;
- `src/PlanDeck/Core/PlanDeck.Infrastructure/Identity/ExternalAccountService.cs:3,17-18,106,121,128-130,209-211,267,339`;
- `src/PlanDeck/Core/PlanDeck.Infrastructure/Identity/AccountLifecycleService.cs:1,14-16,45,151,173`;
- `src/PlanDeck/Core/PlanDeck.Infrastructure/Identity/PlanDeckUserClaimsPrincipalFactory.cs:2,11-15,24-60`;
- `src/PlanDeck/Core/PlanDeck.Infrastructure/Identity/SmtpEmailSender.cs:4,17,21-49`;
- `src/PlanDeck/Core/PlanDeck.Infrastructure/Identity/IdentityAccountRepository.cs:12-34`;
- `src/PlanDeck/Web/PlanDeck.Server/Extensions/ServiceCollectionExtensions.cs:7,239,257-279`;
- `src/PlanDeck/Web/PlanDeck.Server/Extensions/AccountEndpointExtensions.cs:7,41-43,173,252,294,356,364-365`;
- `src/PlanDeck/Web/PlanDeck.Server/Identity/EntraCallbackHandler.cs:5,16-17,67,141`;
- `src/PlanDeck/Web/PlanDeck.Server/Identity/CookieSessionValidator.cs:2,11,41-54`.

Artefakty persystencji rowniez znaja schemat biblioteki:

- `src/PlanDeck/Core/PlanDeck.Infrastructure/Migrations/20260724073135_InitialCreate.cs:14-37,88-171,425-437`;
- `src/PlanDeck/Core/PlanDeck.Infrastructure/Migrations/20260724073135_InitialCreate.Designer.cs:28,52,73,639,709-738`;
- `src/PlanDeck/Core/PlanDeck.Infrastructure/Migrations/PlanDeckDbContextModelSnapshot.cs:25,49,70,636,704-735`.

Plan swiadomie rozdziela `ApplicationUser : IdentityUser<Guid>` od domenowego
`AppUser`, deklaruje provider-neutral principal i zabrania typom frameworka
wejsc do Application
(`context/archive/2026-07-23-create-local-account/plan.md:45-65`,
`context/archive/2026-07-23-create-local-account/plan.md:89-100`).
Kod spelnia granice Application/UI, ale Server nadal bezposrednio przyjmuje
`UserManager`, `SignInManager` i `ILookupNormalizer`.

### LEAK-03: SignalR

SignalR ma port po stronie Application i wrapper po stronie klienta, lecz jego
wyjatek przecieka do widoku.

Wszystkie produkcyjne pliki znajace zaleznosc:

- `src/PlanDeck/Web/PlanDeck.Server/Program.cs:19,145-146`;
- `src/PlanDeck/Web/PlanDeck.Server/Hubs/PlanningRoomHub.cs:1-15,32-75,119-126,160-283`;
- `src/PlanDeck/Web/PlanDeck.Server/Realtime/SignalRPlanningRoomNotifier.cs:1,14-32`;
- `src/PlanDeck/Web/PlanDeck.Client/Services/PlanningRoomClientService.cs:2,8-12,25-101`;
- `src/PlanDeck/Web/PlanDeck.Client/Pages/VotingRoom.razor:2`;
- `src/PlanDeck/Web/PlanDeck.Client/Pages/VotingRoom.razor.cs:3,65-76`.

`IPlanningRoomNotifier` jawnie deklaruje, ze Application zna tylko abstrakcje,
a implementacja SignalR zyje w Web host
(`src/PlanDeck/Core/PlanDeck.Application/Abstractions/IPlanningRoomNotifier.cs:14-25`).
Ta strona granicy jest poprawna. Po stronie klienta
`IPlanningRoomClientService` ukrywa `HubConnection`, ale UI lapie
`HubException` (`src/PlanDeck/Web/PlanDeck.Client/Services/IPlanningRoomClientService.cs:1-24`,
`src/PlanDeck/Web/PlanDeck.Client/Pages/VotingRoom.razor.cs:1-76`).

### Zaleznosci poprawnie ograniczone - punkty kontrolne

Nie sa kandydatami #1:

- **Markdig**: package jest tylko w Client
  (`src/PlanDeck/Web/PlanDeck.Client/PlanDeck.Client.csproj:20-24`), a cala
  biblioteke zna jeden wrapper `Components/MarkdownView.razor:2-42`;
- **MailKit/MimeKit**: package jest tylko w Infrastructure
  (`src/PlanDeck/Core/PlanDeck.Infrastructure/PlanDeck.Infrastructure.csproj:10-13`),
  a typy biblioteki zna `Infrastructure/Identity/SmtpEmailSender.cs:2-8,63-92,144-147`;
- **Azure Key Vault**: `SecretClient` i `RequestFailedException` pozostaja w
  `Infrastructure/AzureDevOps/KeyVaultProjectSecretStore.cs:2-10,24-29,53-68,89-94,115-154`,
  za portem `IProjectSecretStore`;
- **Azure DevOps REST**: `HttpClient`, JSON i statusy HTTP pozostaja w
  `Infrastructure/AzureDevOps/AzureDevOpsWorkItemClient.cs:10-36,67-93,207-234`,
  za `IAzureDevOpsWorkItemClient`.

Te przyklady potwierdzaja, ze jedna implementacja infrastrukturalna za waskim
portem jest osiagalnym wzorcem w tym repozytorium.

## 2. Klasyfikacja i wybor #1

| Kandydat | Warstwy / pliki | Koszt wymiany dzis | Intencja vs kod | Ocena |
| --- | --- | --- | --- | --- |
| **LEAK-01 gRPC stack** | **5 granic / 44 pliki produkcyjne** | **Bardzo wysoki**: 39 operacji, 7 wire interfaces, 7 implementacji, host, 7 proxy i UI error handling | Dokument nakazuje gRPC, ale rownoczesnie zabrania Application zalezec od typow hostingu; kod i instrukcja sa sprzeczne (`.github/copilot-instructions.md:73-77`). | **#1** |
| LEAK-02 Identity | 2 warstwy / 14 runtime + 3 artefakty migracji | Bardzo wysoki: schemat, hash, tokeny, cookies, Entra linking | Provider-neutral principal jest intencja, ale Identity nie jest zadeklarowane jako wymienne (`context/archive/2026-07-23-create-local-account/plan.md:45-65`). | #2 |
| LEAK-03 SignalR | 2 warstwy / 6 plikow | Wysoki: hub + client protocol | Server-side port jest zgodny z deklaracja; pozostaje jeden jawny wyciek `HubException` do UI. | #3 |

### Uzasadnienie wyboru

LEAK-01 ma najwiekszy zasieg i najwyzszy koszt zmiany wynikajacy z
rozsmarowania, nie z samej trudnosci technologii. Identity moze byc drozsze do
migracji danych, ale domena, Application i UI sa juz od niego odseparowane.
SignalR ma realny wyciek wyjatku, lecz podstawowe porty istnieja. gRPC natomiast
ksztaltuje sygnatury Application, klasy bledow, klientowe porty, modele UI i
cztery manifesty pakietow. Jest tez jedynym kandydatem z jawnym rozjazdem
miedzy deklarowanym inward dependency a faktycznym kierunkiem zaleznosci.

## 3. Diagnoza LEAK-01

### Przeciek przez granice

```text
UI
  -> Grpc.Core.StatusCode / RpcException
  -> client ports returning wire DTO
  -> protobuf-net proxy
  -> wire interfaces with CallContext
  -> Application class implementing wire interface
  -> RpcException/StatusCode created next to business rules
  -> repository/domain
```

Groźne miejsca:

1. **Transport w Application.** `SessionGrpcService` dziedziczy kontrakt wire,
   przyjmuje `CallContext` i tworzy `RpcException` obok budowania
   `PlanningSession`
   (`src/PlanDeck/Core/PlanDeck.Application/Services/SessionGrpcService.cs:8-50`).
2. **Bledy transportowe sa modelem bledu use-case.** `ProjectGrpcService`
   mapuje problemy secret store bezposrednio na `StatusCode`
   (`src/PlanDeck/Core/PlanDeck.Application/Services/ProjectGrpcService.cs:726-748`).
3. **Klientowy port nie jest portem.** `IProjectClientService` wystawia wire DTO
   (`src/PlanDeck/Web/PlanDeck.Client/Services/IProjectClientService.cs:1-50`).
4. **UI zna transport.** `Projects` lapie `RpcException` i mapuje `StatusCode`
   na zasoby (`src/PlanDeck/Web/PlanDeck.Client/Pages/Projects.razor.cs:1-40`,
   `src/PlanDeck/Web/PlanDeck.Client/Pages/Projects.razor.cs:94-105`);
   `SessionPagePolicy` przyjmuje `Grpc.Core.StatusCode`
   (`src/PlanDeck/Web/PlanDeck.Client/Pages/SessionPagePolicy.cs:1-25`).
5. **Biblioteka trafia do bundla WASM.** Client ma trzy bezposrednie pakiety
   gRPC (`src/PlanDeck/Web/PlanDeck.Client/PlanDeck.Client.csproj:22-24`).
   Sam klient transportu musi je miec, ale widoki i client ports nie powinny
   znac ich typow.

### Zduplikowane rekonstrukcje i mapowania

- **Domain -> wire DTO** powtarza sie w `ProjectGrpcService.cs:468-495`,
  `SessionGrpcService.cs:723-736`,
  `SessionMemberGrpcService.cs:94`,
  `TeamGrpcService.cs:121-129`.
- **Wire request -> wywolanie** jest recznie odtwarzane w kazdym proxy, np.
  `ProjectClientService.cs:11-40`.
- **Blad Application/infrastructure -> gRPC** jest rozproszony miedzy
  `ProjectGrpcService.cs:726-748`, `SessionGrpcService.cs:334-404`,
  `SessionMemberGrpcService.cs:44-72` i `TeamGrpcService.cs:64-108`.
- **gRPC -> komunikat UI** powtarza sie w `Projects.razor.cs:94-105`,
  `ProjectDetails.razor.cs:506-513`,
  `Sessions.razor.cs:599-604`, `Teams.razor.cs:239-261` oraz
  `SessionPagePolicy.cs:14-34`.
- Klient tworzy proxy wielokrotnie zamiast raz na adapter, np.
  `ProjectClientService.cs:11,17,24,35`.

To nie jest tylko duplikacja techniczna. `StatusCode.FailedPrecondition`
oznacza w roznych ekranach rozne zdarzenia biznesowe, a czesc UI dodatkowo
porownuje tekst `detail` z komunikatami walidacji
(`src/PlanDeck/Web/PlanDeck.Client/Pages/SessionPagePolicy.cs:17-34`).
Zmiana tekstu po stronie serwera moze zatem zmienic zachowanie UI.

### Rozjazd intencji i kodu

Instrukcja poprawnie chce wspolnego wire contractu i cienkiego hosta
(`.github/copilot-instructions.md:73-76`), ale umieszczenie implementacji w
Application powoduje, ze transport staje sie architektura use-case.
Jednoczesny zakaz zaleznosci Application od gRPC
(`.github/copilot-instructions.md:77`) nie jest dotrzymany. ACL powinien
zachowac business logic w Application, lecz przeniesc implementacje wire
interface i mapowanie statusow do zewnetrznego adaptera.

## 4. Projekt anti-corruption layer

### Docelowe projekty i kierunek zaleznosci

```text
PlanDeck.Domain/Application
  <- PlanDeck.Infrastructure (repositories/external systems)
  <- PlanDeck.Grpc.ServerAdapter
       <- PlanDeck.Server host

PlanDeck.Client.Models + client ports
  <- PlanDeck.Grpc.ClientAdapter
       <- PlanDeck.Client composition root/UI

PlanDeck.Grpc.Contracts
  <- ServerAdapter
  <- ClientAdapter
```

Proponowane nowe projekty:

- `Adapters/PlanDeck.Grpc.Contracts` - tylko wire interfaces, request/reply,
  `[Service]`, `[Operation]`, `[DataContract]`, field numbers;
- `Adapters/PlanDeck.Grpc.ServerAdapter` - endpoints, wire/application mappers,
  error mapper i neutralne extension methods do DI/routingu;
- `Adapters/PlanDeck.Grpc.ClientAdapter` - channel/proxy, wire/client-model
  mappers i translacja bledow;
- bez nowych zaleznosci gRPC w `Application`, `Infrastructure`, widokach ani
  client ports.

`Core.Shared/Realtime` moze pozostac transport-neutralnym modelem realtime,
jezeli nie ma atrybutow gRPC. `Core.Shared/Contracts` zostaje przeniesione do
`PlanDeck.Grpc.Contracts`.

### Kanoniczne value objects i modele

Nie wolno dodac metod `ToGrpc()` do domeny - to odtworzyloby przeciek.
Kanoniczny ksztalt jest domenowy, a o typie biblioteki wie tylko mapper ACL.

```csharp
public readonly record struct SessionId
{
    public Guid Value { get; }

    public static SessionId From(Guid value)
    {
        if (value == Guid.Empty)
            throw new InvalidSessionIdException();
        return new SessionId(value);
    }
}

public sealed record SessionView(
    SessionId Id,
    ProjectId ProjectId,
    string Name,
    SessionLifecycle Status,
    IReadOnlyList<SessionTaskView> Tasks);

public readonly record struct ApplicationFailure(
    FailureCode Code,
    string? Field = null);
```

`SessionId` jest jedynym semantycznym ksztaltem ID:

- EF converter mapuje `SessionId <-> Guid` w Infrastructure;
- gRPC mapper mapuje wire `Guid <-> SessionId` w ServerAdapter;
- ClientAdapter mapuje wire `Guid -> ClientSessionId` lub bezpieczny UI model;
- operacje domenowe przyjmuja `SessionId`, nie wire request.

Mapowanie persystencji i transportu pozostaje w osobnych adapterach. Laczenie
obu konwersji w value object byloby zlamaniem ACL.

### Waski port Application

Porty powinny odpowiadac use-case'om, nie transportowym serwisom:

```csharp
public interface ICreateSessionUseCase
{
    Task<UseCaseResult<SessionView>> ExecuteAsync(
        CreateSessionCommand command,
        CancellationToken cancellationToken);
}

public interface IListSessionsUseCase
{
    Task<UseCaseResult<IReadOnlyList<SessionView>>> ExecuteAsync(
        ProjectId projectId,
        CancellationToken cancellationToken);
}

public sealed record CreateSessionCommand(
    ProjectId ProjectId,
    SessionName Name,
    VotingScale Scale);
```

Nie tworzyc jednego 39-metodowego `IApplicationService`. Waskie porty pozwalaja
adapterowi REST, gRPC lub testowi wywolac ten sam przypadek uzycia.

### Server adapter

```text
[Service] IGrpcSessionService
    CreateSessionAsync(CreateSessionRequest, CallContext)

GrpcSessionEndpoint implements IGrpcSessionService:
    command = GrpcSessionMapper.ToCommand(request)
    result = createSession.ExecuteAsync(command, context.CancellationToken)
    return result.Match(
        success => GrpcSessionMapper.ToReply(success),
        failure => throw GrpcFailureMapper.ToRpcException(failure))
```

Tylko `GrpcSessionMapper` zna jednoczesnie wire DTO i modele Application.
Tylko `GrpcFailureMapper` zna `FailureCode` i `Grpc.Core.StatusCode`.
Endpoint nie zawiera reguly biznesowej.

Host wywoluje neutralne:

```csharp
builder.Services.AddPlanDeckRpcAdapter();
app.MapPlanDeckRpcAdapter();
```

Implementacje extension methods znajduja sie w `ServerAdapter`, dlatego
`Program.cs` nie importuje `ProtoBuf.*` ani nie wymienia endpoint classes.

### Client adapter

```csharp
public interface ISessionGateway
{
    Task<ClientResult<SessionViewModel>> GetAsync(
        SessionId id,
        CancellationToken cancellationToken);
}

GrpcSessionGateway:
    try:
        reply = grpc.GetSessionAsync(GrpcSessionMapper.ToRequest(id))
        return Success(GrpcSessionMapper.ToViewModel(reply))
    catch RpcException ex:
        return Failure(GrpcFailureMapper.ToClientFailure(ex))
```

UI otrzymuje `SessionViewModel` i `ClientFailure.Code`. Nie otrzymuje
`SessionDto`, `GetSessionReply`, `RpcException`, `StatusCode` ani `detail`.
Lokalizacja mapuje stabilny `FailureCode`, nie tekst serwera.

### Rozstrzygniecia na podstawie dokumentacji biblioteki

Zweryfikowana dokumentacja dostawcy:
`https://protobuf-net.github.io/protobuf-net.Grpc/gettingstarted`.

1. **Czy Application potrzebuje `CallContext`? - Nie.** Dokumentacja pozwala
   uzyc zwyklego `CancellationToken`, gdy nie sa potrzebne headers/trailers.
   Biezace use-case'y odczytuja z contextu cancellation token, nie modeluja
   metadanych biznesowych. Decyzja: `CallContext` zostaje w ServerAdapter,
   Application przyjmuje `CancellationToken`.
2. **Gdzie maja zyc `[Service]`/`[Operation]`?** Dokumentacja pozwala trzymac
   service/data contracts w osobnej bibliotece. Decyzja:
   `PlanDeck.Grpc.Contracts`, nie `Core.Shared` ani Application.
3. **Czy mozna ponownie uzyc domenowych encji jako protobuf DTO? - Nie w tym
   projekcie.** Dokumentacja wymaga stabilnych numerow pol i drzewiastego
   modelu wire. Decyzja: odrebne wire DTO i jawne mappery ACL; encje EF nie
   dostaja `[DataContract]`/`[DataMember]`.
4. **Czy potrzebny jest streaming? - Nie obecnie.** Kontrakty nie zawieraja
   `IAsyncEnumerable` (brak trafien w `Core.Shared/Contracts`), wiec wszystkie
   porty pozostaja zwyklymi async use-case'ami. SignalR zachowuje osobna
   granice realtime.

Te decyzje nalezy zakodowac w projektach ACL i architecture tests, nie w
`Program.cs`, UI ani klasach Application.

## 5. Dowod izolacji i before/after

### Co zmienia sie przy wymianie biblioteki

Przy zamianie `protobuf-net.Grpc` na inny code-first RPC:

- zmieniaja sie tylko `PlanDeck.Grpc.Contracts`, `ServerAdapter` i
  `ClientAdapter`;
- Application ports, domain/value objects, repositories, EF mappings i tabele
  pozostaja bez zmian;
- UI i jego view models pozostaja bez zmian;
- stabilne `FailureCode` pozostaja bez zmian;
- testy use-case i UI pozostaja bez zmian; zmieniaja sie contract/adapter tests.

Przy zamianie gRPC na REST:

- `PlanDeck.Grpc.*` mozna usunac i dodac `PlanDeck.Rest.*`;
- te same Application ports i client gateways sa implementowane przez nowe
  adaptery;
- tabele, encje, use-case'y i widoki nie zmieniaja sie;
- URL/payload sa detalem nowego adaptera, a nie modelem domeny.

### Before/after

| Obecne miejsce | Before | After |
| --- | --- | --- |
| `Core.Shared/Contracts/*.cs` | Wire i wspoldzielony model aplikacji sa tym samym (`ISessionService.cs:1-45`). | Pliki przeniesione do `Adapters/PlanDeck.Grpc.Contracts`; tylko wire. |
| `Application/*GrpcService.cs` | Use-case implementuje `[Service]`, przyjmuje `CallContext`, rzuca `RpcException`. | Transport-neutral handler implementuje waski port i zwraca `UseCaseResult`; endpoint gRPC zyje w ServerAdapter. |
| `GuestAccessGuard.cs` | Rzuca `RpcException` (`:17-25`). | Zwraca/rzuca `GuestAccessDenied` domenowy; adapter mapuje kod. |
| `ProjectGrpcService.cs` | Miesza mapowanie secret-store -> gRPC z use-case (`:726-748`). | `FailureCode.ProjectSecretUnavailable`; jeden `GrpcFailureMapper`. |
| `Client/*ClientService.cs` | Kazda metoda tworzy proxy i zwraca wire DTO, np. `ProjectClientService.cs:7-40`. | `Grpc*Gateway` w ClientAdapter tworzy proxy raz i zwraca client view model/result. |
| `Client/I*ClientService.cs` | Porty wystawiaja `*Dto`/`*Reply`, np. `IProjectClientService.cs:1-50`. | Porty wystawiaja tylko `*ViewModel`, value objects i `ClientResult`. |
| Widoki `.razor(.cs)` | Importuja wire contracts, lapia `RpcException`, switchuja `StatusCode`. | Znaja gateway result i stabilny `FailureCode`; brak `Grpc.Core`. |
| `SessionPagePolicy.cs` | Sygnatura przyjmuje `StatusCode` i tekst `detail` (`:14-34`). | Sygnatura przyjmuje `ClientFailure`; mapuje tylko kod/field. |
| `Server/Program.cs` | Zna biblioteke i kazdy endpoint (`:8,27-31,130-143`). | Wywoluje dwie neutralne extension methods z ServerAdapter. |
| `.csproj` Core/Application/Client/Server | Cztery projekty maja direct/transitive gRPC concerns. | Pakiety sa tylko w trzech projektach `Adapters/PlanDeck.Grpc.*`. |

### Pliki, ktore po refaktorze przestaja znac zaleznosc

1. Wszystkie 7 obecnych `Core.Shared/Contracts/I*Service.cs` - zostaja usuniete
   z Core i zastapione transport-neutralnymi modelami Application/Client.
2. Wszystkie 8 `Application/Services/*GrpcService.cs`/`GuestAccessGuard.cs` -
   po rozdzieleniu pozostaja handlers bez gRPC.
3. `Web/PlanDeck.Server/Program.cs` - tylko neutralna rejestracja adaptera.
4. Wszystkie 7 klientowych proxy w `Client/Services` - implementacje
   przeniesione do ClientAdapter.
5. Wszystkie 8 klientowych interfejsow/komponentow wystawiajacych wire DTO.
6. Wszystkie wymienione widoki i `SessionPagePolicy` - brak `Grpc.Core` i wire
   contracts.

### Pliki, ktore po refaktorze znaja zaleznosc

Wylacznie:

- `Adapters/PlanDeck.Grpc.Contracts/**/*.cs`;
- `Adapters/PlanDeck.Grpc.ServerAdapter/**/*.cs`;
- `Adapters/PlanDeck.Grpc.ClientAdapter/**/*.cs`;
- odpowiadajace `Adapters/PlanDeck.Grpc.*/**/*.csproj`;
- testy adapterow umieszczone pod `Adapters/Tests/PlanDeck.Grpc.*`.

Nie zna jej `Core`, `Infrastructure`, `Web/*/Pages`, `Web/*/Components`,
client ports ani host `Program.cs`.

## 6. Weryfikacja i plan faz

### Kryteria grep

Po refaktorze:

```powershell
rg -n "ProtoBuf\.Grpc|Grpc\.Core|Grpc\.Net|protobuf-net\.Grpc" `
  src\PlanDeck\Core src\PlanDeck\Web\PlanDeck.Client\Pages `
  src\PlanDeck\Web\PlanDeck.Client\Components `
  src\PlanDeck\Web\PlanDeck.Client\Services `
  src\PlanDeck\Web\PlanDeck.Server
```

ma zwrocic zero trafien poza neutralnym wywolaniem adapter registration
(docelowo rowniez zero w `Program.cs`).

Pozytywna kontrola:

```powershell
rg -n "ProtoBuf\.Grpc|Grpc\.Core|Grpc\.Net|protobuf-net\.Grpc" `
  src\PlanDeck\Adapters
```

ma zwrocic wylacznie `PlanDeck.Grpc.Contracts`, `ServerAdapter`,
`ClientAdapter` i ich adapter tests.

Dodatkowe architecture tests:

- `PlanDeck.Application` nie referencjonuje `PlanDeck.Grpc.*`, `Grpc.Core` ani
  `ProtoBuf.Grpc`;
- assemblies UI nie importuja `Grpc.Core` ani `PlanDeck.Grpc.Contracts`;
- mappery gRPC istnieja tylko w adapter projects;
- Application errors nie zawieraja transportowych statusow ani tekstow wire.

### Faza 1 - zamrozenie zachowania i granicy

1. Dodac architecture tests dla obecnie oczekiwanego kierunku zaleznosci.
2. Dodac contract tests dla 39 operacji: wire request/reply i error semantics.
3. Spisac stabilny katalog `FailureCode`; nie kopiowac tekstow gRPC.
4. Nie zmieniac jeszcze publicznego zachowania.

### Faza 2 - pionowy pilot session read/create (test-first)

1. Wprowadzic `SessionId`, `ProjectId`, `SessionView`,
   `ApplicationFailure` i waskie use-case ports.
2. Wydzielic `CreateSession` i `Get/ListSession` z `SessionGrpcService`.
3. Dodac `GrpcSessionEndpoint` i mappery w ServerAdapter.
4. Dodac `GrpcSessionGateway` w ClientAdapter.
5. Przepiac jeden ekran na neutralny view model i `ClientFailure`.
6. Udowodnic grepem, ze pilot nie ma `Grpc.Core` poza adapterem.

### Faza 3 - pozostale session/task/write-back operacje (test-first)

1. Migrowac reszte `ISessionService` operacja po operacji.
2. Zachowac invariants z planu agregatu
   (`context/domain/02-invariant-aggregate-refactor.md:96-110`,
   `context/domain/02-invariant-aggregate-refactor.md:181-344`).
3. Usunac tekstowe mapowanie `detail` z `SessionPagePolicy`.

### Faza 4 - project/team/member/ADO/auth

1. Powtorzyc wzorzec port -> endpoint -> mapper -> gateway.
2. Skonsolidowac mapowanie bledow w `GrpcFailureMapper`.
3. Przeniesc wire contracts z `Core.Shared` do `Grpc.Contracts`.
4. `AuthGrpcService` migrowac ostatni, aby nie przerwac bootstrapu principal.

### Faza 5 - hosty, pakiety i cleanup

1. Zamknac rejestracje/routing w neutralnych extension methods adaptera.
2. Usunac gRPC package references z `Core.Shared`, `Application`, Client i
   Server; hosty referencjonuja tylko projekty adapterow.
3. Usunac stare `*GrpcService`, klientowe proxy i wire DTO z Core.
4. Zaktualizowac `PlanDeck.slnx` o trzy projekty adapterow i adapter tests.

### Faza 6 - dowod wymienialnosci

1. Uruchomic kryteria grep i architecture tests.
2. Podmienic w tescie kontraktowym jeden gateway na in-memory fake bez
   `GrpcChannel`.
3. Udowodnic, ze use-case tests, EF tests i component tests nie wymagaja gRPC.
4. Zaktualizowac `.github/copilot-instructions.md`: business logic w
   Application, gRPC endpoints i wire mapping tylko w adapterach.

## 7. Kryteria zakonczenia

Plan jest zrealizowany, gdy:

1. `Application` nie ma package reference ani importu gRPC;
2. UI nie zna `RpcException`, `StatusCode`, `CallContext` ani wire DTO;
3. client ports zwracaja tylko wlasne modele i stabilne failure codes;
4. wszystkie mapowania wire <-> application sa w ACL;
5. wszystkie mapowania failure <-> transport status sa w jednym mapperze ACL;
6. tabele i EF mappings nie zmieniaja sie przy podmianie transportu;
7. grep zaleznosci zwraca tylko katalogi adapterow;
8. wymiana adaptera nie wymaga zmian w domenie, use-case'ach ani widokach.
