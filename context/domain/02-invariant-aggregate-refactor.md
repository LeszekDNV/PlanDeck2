---
title: "PlanDeck - refaktor agregatu strzegacego niezmiennika rundy"
created: 2026-07-28
type: refactor-plan
---

# PlanDeck - refaktor agregatu strzegacego niezmiennika rundy

## 0. Odkryty kontekst

### Produkt i zrodla

PlanDeck ma zamknac prosty obieg: import lub utworzenie zadania, sesja, ukryte
glosowanie, wspolny reveal, reczny wybor estymaty i opcjonalny write-back do
Azure DevOps. Pelny obieg jest glownym kryterium sukcesu
(`context/foundation/prd.md:27-31`, `context/foundation/prd.md:40-54`), a regule
biznesowa dokument streszcza jako hidden-vote -> reveal -> manual pick ->
write-back (`context/foundation/prd.md:121-129`).

Najwazniejsze guardrails wymagaja, aby:

- wartosci glosow nie byly widoczne przed reveal
  (`context/foundation/prd.md:49-53`);
- glosy nie ginely, nie duplikowaly sie i nie zmienialy kolejnosci
  (`context/foundation/prd.md:49-53`);
- zapis do ADO trafil do wlasciwego zadania i pola, a blad byl jawny
  (`context/foundation/prd.md:49-50`);
- dane pozostawaly w granicy tenant/session
  (`context/foundation/prd.md:53`).

Aktualne dokumenty zmian doprecyzowuja dwie niezabezpieczone reguly:

- aktywny stan rundy ma przetrwac cleanup, restart i deployment
  (`context/changes/persist-voting-round-state/change.md:10-12`);
- uzgodniona estymata moze zostac wybrana tylko po reveal i dla aktywnego
  zadania, z deterministyczna konkurencja rownoleglych wyborow
  (`context/changes/enforce-voting-round-transitions/change.md:10-12`).

`README.md` zawiera tylko nazwe projektu (`README.md:1`). Nie ma
`tech-stack.md`; aktualny stack wynika z PRD i kodu.

### Stack i warstwy logiki

| Warstwa | Technologia i lokalizacja | Obecna odpowiedzialnosc |
| --- | --- | --- |
| UI | Blazor WASM + MudBlazor, `Web/PlanDeck.Client` | Renderuje stan pokoju i ogranicza dostepne akcje. |
| Transport/API | SignalR hub i code-first gRPC, `Web/PlanDeck.Server` | Parsuje identyfikatory, sprawdza auth/session scope i koordynuje zapis oraz broadcast. |
| Application/domain | .NET 10, `Core/PlanDeck.Application` | Anemiczny model sesji, pamieciowa maszyna stanu pokoju i przypadki uzycia. |
| Persistence/integrations | EF Core 10/SQL i ADO, `Core/PlanDeck.Infrastructure` | Repozytoria, tenant filters, konfiguracja EF oraz ADO PATCH. |
| Contracts | `Core/PlanDeck.Core.Shared` | Stan SignalR i kontrakty gRPC wspoldzielone z klientem. |
| Tests | NUnit 4 + Playwright | Unit, integration i lokalne E2E. |

Logika rundy nie ma jednej granicy domenowej. `PlanningRoomService` jest
singletonem procesu (`src/PlanDeck/Web/PlanDeck.Server/Extensions/ServiceCollectionExtensions.cs:208-211`),
podczas gdy sesja i wynik sa zapisywane przez scoped repozytorium
(`src/PlanDeck/Web/PlanDeck.Server/Extensions/ServiceCollectionExtensions.cs:221-225`).

## 1. Zidentyfikowane niezmienniki biznesowe

| ID | Niezmiennik, ktory musi byc zawsze prawdziwy | Zrodlo wymagania | Dowod w kodzie |
| --- | --- | --- | --- |
| INV-01 | Przed reveal odbiorca widzi fakt oddania glosu, ale nigdy jego wartosc. | `context/foundation/prd.md:49-52`, `context/foundation/prd.md:125-129` | Projekcja zwraca `Vote` tylko po reveal (`src/PlanDeck/Core/PlanDeck.Application/Planning/PlanningRoomService.cs:444-480`). |
| INV-02 | Glos moze oddac dolaczony uczestnik, dla aktywnego zadania, przed reveal i tylko wartoscia ze skali. | `context/foundation/prd.md:125-129` | Preconditions istnieja w `CastVote` (`src/PlanDeck/Core/PlanDeck.Application/Planning/PlanningRoomService.cs:221-251`). |
| INV-03 | Ponowny glos tej samej osoby przed reveal zastepuje poprzedni, nie tworzy duplikatu. | `context/archive/2026-06-22-realtime-vote-integrity/plan.md:33-36` | Slownik participant -> vote (`src/PlanDeck/Core/PlanDeck.Application/Planning/PlanningRoomService.cs:244-249`, `src/PlanDeck/Core/PlanDeck.Application/Planning/PlanningRoomService.cs:514-516`). |
| INV-04 | Reveal ujawnia wszystkie oddane wartosci razem; nie tworzy nowych glosow ani wyniku. | `context/foundation/prd.md:125-129` | Reveal zmienia tylko `IsRevealed` i rewizje (`src/PlanDeck/Core/PlanDeck.Application/Planning/PlanningRoomService.cs:255-270`). |
| INV-05 | **Uzgodniona estymata moze zostac wybrana tylko po reveal i tylko dla aktywnego zadania; musi nalezec do skali.** | `context/foundation/prd.md:125-129`, `context/changes/enforce-voting-round-transitions/change.md:10-12` | UI spelnia regule, ale serwer sprawdza tylko skale i istnienie zadania (`src/PlanDeck/Web/PlanDeck.Client/Pages/VotingRoom.razor:138-153`, `src/PlanDeck/Web/PlanDeck.Server/Hubs/PlanningRoomHub.cs:108-131`). |
| INV-06 | Dwa rownolegle wybory wyniku maja jeden jawny, deterministyczny rezultat. | `context/changes/enforce-voting-round-transitions/change.md:10-12` | Lokalny `SemaphoreSlim` serializuje tylko jedna instancje procesu (`src/PlanDeck/Web/PlanDeck.Server/Hubs/PlanningRoomHub.cs:17`, `src/PlanDeck/Web/PlanDeck.Server/Hubs/PlanningRoomHub.cs:289-315`); brak wersji EF. |
| INV-07 | Aktywny task, glosy, reveal i rewizja rundy przezywaja reconnect, cleanup, restart i deployment. | `context/changes/persist-voting-round-state/change.md:10-12` | Stan zyje w prywatnym `ConcurrentDictionary`; seed odtwarza tylko zadania, skale i estimate (`src/PlanDeck/Core/PlanDeck.Application/Planning/PlanningRoomService.cs:7-43`, `src/PlanDeck/Core/PlanDeck.Application/Planning/PlanningRoomService.cs:485-516`, `src/PlanDeck/Core/PlanDeck.Application/Planning/VotingRoundService.cs:54-62`). |
| INV-08 | Reset wyniku, glosow i reveal jest jedna zmiana biznesowa: nie wolno trwale wyzerowac estimate bez zresetowania rundy ani odwrotnie. | Wynik ma pochodzic z jednego cyklu reveal-and-decide (`context/foundation/prd.md:125-129`). | Hub najpierw zapisuje `null`, a potem osobno czysci pamiec (`src/PlanDeck/Web/PlanDeck.Server/Hubs/PlanningRoomHub.cs:77-97`). |
| INV-09 | Write-back uzywa tylko zrodla ADO, numerycznej uzgodnionej estymaty, wlasciwego work itemu/pola i oczekiwanej rewizji. | `context/foundation/prd.md:49-50`, `context/foundation/prd.md:112-116` | Serwis waliduje task i estimate (`src/PlanDeck/Core/PlanDeck.Application/Services/SessionGrpcService.cs:334-365`), a PATCH wykonuje `test /rev` przed zapisem pola (`src/PlanDeck/Core/PlanDeck.Infrastructure/AzureDevOps/AzureDevOpsWorkItemClient.cs:67-86`). |
| INV-10 | Gosc dziala tylko w jednej aktywnej sesji i moze glosowac, ale nie reveal/reset/navigate/select. | `context/foundation/prd.md:100-101`, `context/foundation/prd.md:133-138` | Hub sprawdza claim `sid` i blokuje akcje sterujace (`src/PlanDeck/Web/PlanDeck.Server/Hubs/PlanningRoomHub.cs:228-275`). |
| INV-11 | Tenant jest granica kazdego odczytu i zapisu sesji. | `context/foundation/prd.md:49-54` | Klucz pokoju zawiera tenant i session (`src/PlanDeck/Web/PlanDeck.Server/Hubs/PlanningRoomHub.cs:228-245`); persistence stosuje tenant-scoped model opisany w `context/domain/01-domain-distillation.md:154-161`. |
| INV-12 | Konfiguracja sesji i jej skala sa stabilne po aktywacji. | `context/foundation/prd.md:87-92` | `PlanningSession` przechowuje status i skale (`src/PlanDeck/Core/PlanDeck.Application/Domain/PlanningSession.cs:3-19`); draft-only update jest opisany w `context/domain/01-domain-distillation.md:110-119`. |

## 2. Klasyfikacja i wybor niezmiennika #1

Skala rdzeniowosci: **5** oznacza warunek sensu produktu, **1** detal
wspierajacy. Rozsmarowanie liczy warstwy runtime, nie dokumenty. Status:
**egzekwowany**, **czesciowy**, **deklarowany** albo **naruszalny**.

| ID | Rdzeniowosc | Rozsmarowanie | Realna egzekucja |
| --- | ---: | --- | --- |
| INV-01 hidden-until-reveal | 5/5 - centralne doswiadczenie planning poker | 3 warstwy: application, contract, UI | **Egzekwowany serwerowo** przez projekcje. |
| INV-02 legalny glos | 5/5 | 3 warstwy: UI, hub, application | **Egzekwowany serwerowo**; UI jest tylko wygoda. |
| INV-03 jeden glos uczestnika | 4/5 | 1 warstwa: application | **Egzekwowany**, ale tylko w pamieci procesu. |
| INV-04 reveal razem | 5/5 | 3 warstwy: application, contract, UI | **Egzekwowany w procesie**, nietrwaly. |
| **INV-05 reveal + active task przed estimate** | **5/5 - wynik rundy i wejscie do north-star write-back** | **5 warstw, 7 plikow: UI, hub, application service/model, repository, EF** | **Naruszalny**; warunek reveal/active-task jest tylko w UI. |
| INV-06 deterministyczna konkurencja | 5/5 | 3 warstwy: hub, application, persistence | **Czesciowy**; lokalny lock, last-write-wins bez jawnej semantyki. |
| INV-07 trwalosc rundy | 5/5 - guardrail utraty glosow | 4 warstwy: hub, application, DI, persistence | **Deklarowany i naruszalny** po restart/deploy. |
| INV-08 atomowy reset | 4/5 | 4 warstwy: hub, application, repository, DB | **Naruszalny** przy awarii miedzy zapisem DB a pamiecia. |
| INV-09 poprawny ADO write-back | 5/5 - north star | 4 warstwy: UI, application, repository, ADO | **W duzej mierze egzekwowany**; ADO i lokalna rewizja nie sa jedna transakcja. |
| INV-10 guest vote-only/scope | 4/5 - wyroznik i security | 3 warstwy: UI, hub, auth | **Egzekwowany serwerowo**. |
| INV-11 tenant isolation | 5/5 jako guardrail, nie przewaga | 3 warstwy: hub, application, EF | **Egzekwowany centralnie**. |
| INV-12 stabilna konfiguracja | 3/5 | 3 warstwy: UI, application, persistence | **Egzekwowany** przez draft-only flow. |

### Wybor #1

Wybrany niezmiennik:

> **W sesji planistycznej uzgodniona estymata moze zostac zapisana tylko dla
> aktualnie aktywnego zadania, po ujawnieniu jego rundy i jako wartosc z
> konfiguracji skali; przejscie oraz wynik sa atomowe i wersjonowane.**

INV-05 wygrywa, bo laczy semantyczny rdzen hidden -> reveal -> manual pick z
wejsciem do north-star write-back, a jednoczesnie jest latwy do naruszenia
przez dowolnego klienta SignalR. INV-07 jest rownie powazny, lecz dotyczy
utraty poprawnego stanu przy awarii; INV-05 pozwala utworzyc **niepoprawny
wynik biznesowy w normalnym dzialaniu**, bez restartu. INV-06 i INV-08 sa
czesciami tej samej granicy spojnosci i dlatego musza zostac naprawione razem
z INV-05, nie jako osobne lokalne laty.

## 3. Diagnoza wybranego niezmiennika

### Gdzie regula zyje dzisiaj

1. **UI - jedyny pelny straznik.** Przyciski wyboru sa renderowane tylko przy
   `_state.IsRevealed`, a akcja uzywa `_activeTaskId`
   (`src/PlanDeck/Web/PlanDeck.Client/Pages/VotingRoom.razor:138-153`,
   `src/PlanDeck/Web/PlanDeck.Client/Pages/VotingRoom.razor.cs:132-140`).
2. **Shared contract - deklaruje dane, nie przejscie.** Stan zawiera
   `CurrentTaskId`, `IsRevealed`, taski i `Revision`, ale nie ma fazy rundy,
   expected revision ani capabilities
   (`src/PlanDeck/Core/PlanDeck.Core.Shared/Realtime/PlanningRoomState.cs:3-24`).
3. **Hub - walidacja czesciowa.** `SelectEstimate` blokuje goscia, autoryzuje
   sesje, parsuje task ID i sprawdza skale; nie sprawdza reveal ani zgodnosci z
   `CurrentTaskId` (`src/PlanDeck/Web/PlanDeck.Server/Hubs/PlanningRoomHub.cs:108-131`).
4. **Pamieciowy model - ma potrzebne dane, lecz ich nie laczy.**
   `ApplyAgreedEstimate` sprawdza tylko, czy task jest w pokoju; moze zapisac
   wynik dla taska nieaktywnego i przed reveal
   (`src/PlanDeck/Core/PlanDeck.Application/Planning/PlanningRoomService.cs:313-329`).
   `IsValidEstimate` sprawdza tylko skale
   (`src/PlanDeck/Core/PlanDeck.Application/Planning/PlanningRoomService.cs:331-343`).
5. **Application service - passthrough.** `SelectEstimateAsync` deleguje
   bezposrednio do repozytorium, bez jakiejkolwiek reguly rundy
   (`src/PlanDeck/Core/PlanDeck.Application/Planning/VotingRoundService.cs:64-67`).
6. **Repozytorium - bezposrednia aktualizacja pola.** Wyszukuje dowolny task
   sesji, ustawia publiczny setter i wykonuje `SaveChangesAsync`
   (`src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/SessionRepository.cs:65-76`).
7. **Encja - brak ochrony.** `SessionTask.AgreedEstimate` jest publicznie
   ustawialnym stringiem (`src/PlanDeck/Core/PlanDeck.Application/Domain/SessionTask.cs:3-24`).
8. **EF - tylko limit dlugosci.** Brak fazy rundy, glosow i concurrency tokena
   (`src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/Configurations/SessionTaskConfiguration.cs:7-41`,
   `src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/Configurations/PlanningSessionConfiguration.cs:7-47`).

### Niespojnosc i atomowosc

- `PlanningRoom` zawiera aktywny task, reveal, glosy i rewizje, ale tylko w
  pamieci (`src/PlanDeck/Core/PlanDeck.Application/Planning/PlanningRoomService.cs:485-516`).
- Trwala `PlanningSession` nie ma zadnego z tych pol
  (`src/PlanDeck/Core/PlanDeck.Application/Domain/PlanningSession.cs:3-20`).
- Hub zapisuje wynik do DB, potem osobno aktualizuje pamiec i broadcastuje
  (`src/PlanDeck/Web/PlanDeck.Server/Hubs/PlanningRoomHub.cs:113-131`). Commit
  moze wiec przezyc blad pozniejszej aktualizacji lub broadcastu.
- Lokalny lock jest indeksowany tylko `sessionId`, dziala w jednym procesie i
  nie obejmuje reveal ani nawigacji
  (`src/PlanDeck/Web/PlanDeck.Server/Hubs/PlanningRoomHub.cs:17`,
  `src/PlanDeck/Web/PlanDeck.Server/Hubs/PlanningRoomHub.cs:62-105`,
  `src/PlanDeck/Web/PlanDeck.Server/Hubs/PlanningRoomHub.cs:289-315`).
- Test konkurencji przechodzi bez reveal: po zmianie aktywnego taska dwa
  klienty od razu wywoluja `SelectEstimate`; test akceptuje dowolnego
  last-writera (`src/PlanDeck/Tests/PlanDeck.Integration.Tests/Realtime/PlanningRoomHubTests.cs:593-629`).

### Bledy i ich polykanie

- Nielegalny select przed reveal lub dla nieaktywnego taska **nie tworzy
  bledu**; system traktuje go jak legalna operacje. To luka fail-fast.
- Dla znanych naruszen pokoj rzuca ogolne `InvalidOperationException`, a hub
  tekstowe `HubException`; nie ma typow domenowych rundy
  (`src/PlanDeck/Core/PlanDeck.Application/Planning/PlanningRoomService.cs:221-242`,
  `src/PlanDeck/Web/PlanDeck.Server/Hubs/PlanningRoomHub.cs:117-126`).
- UI lapie kazdy `Exception` i pokazuje jeden ogolny komunikat, tracac kod
  przyczyny (`src/PlanDeck/Web/PlanDeck.Client/Pages/VotingRoom.razor.cs:142-157`).
- Sama sciezka select nie połyka awarii zapisu. Sasiedni notifier po commitcie
  lapie jednak kazdy wyjatek synchronizacji/broadcastu, loguje i zwraca sukces
  pierwotnej operacji
  (`src/PlanDeck/Web/PlanDeck.Server/Realtime/SignalRPlanningRoomNotifier.cs:20-37`).
  Po refaktorze nie wolno uzywac tego wzorca do egzekucji przejscia; broadcast
  moze byc eventual, ale zdarzenie do outboxa musi zostac zapisane atomowo z
  agregatem.

## 4. Projekt agregatu-straznika

### Wybrana granica

**Root: `PlanningSession`.**

To istniejacy byt, ktory juz posiada taski i skale. Po refaktorze root posiada
rowniez `ActiveTaskId`, `Revision` oraz wewnetrzne `VotingRound` per task.
`VotingRound` nie jest osobnym rootem, poniewaz regula "dla aktywnego taska"
przecina granice rundy i sesji. Osobne rooty wymusilyby rozproszony lock albo
unikalny indeks jako substytut logiki domenowej.

Wewnatrz agregatu:

- `PlanningSession`: lifecycle, skala, task membership, aktywny task, rewizja;
- `SessionTask`: metadane zrodla i uzgodniona estymata bez publicznego settera;
- `VotingRound`: `TaskId`, `Phase`, glosy per participant, wynik;
- `VotingRoundPhase`: `Hidden`, `Revealed`, `Estimated`;
- value objects: `EstimateValue`, `ParticipantId`.

Poza agregatem:

- connection IDs, liczba kart przegladarki i online/offline - efemeryczny
  `RoomPresence`, bo to stan transportu, nie warunek poprawnosci wyniku;
- ADO write-back - osobny proces po zatwierdzeniu wyniku, bo zewnetrznego API
  nie da sie objac transakcja SQL.

### Sygnatury metod domenowych

```csharp
public sealed class PlanningSession
{
    public Guid Id { get; }
    public Guid? ActiveTaskId { get; private set; }
    public long Revision { get; private set; }

    public void ActivateTask(Guid taskId);
    public void CastVote(ParticipantId participantId, EstimateValue vote);
    public void RevealActiveRound();
    public void SelectAgreedEstimate(Guid taskId, EstimateValue estimate);
    public void ResetActiveRound();
}
```

Minimalny pseudokod najwazniejszej operacji:

```text
SelectAgreedEstimate(taskId, estimate):
    if ActiveTaskId is null
        throw NoActiveTaskException

    if taskId != ActiveTaskId
        throw TaskIsNotActiveException(taskId, ActiveTaskId)

    task = require taskId belongs to session
        else throw SessionTaskNotFoundException(taskId)

    round = require round for taskId
        else throw VotingRoundNotStartedException(taskId)

    if round.Phase != Revealed
        throw RoundNotRevealedException(taskId, round.Phase)

    if estimate not in session.ScaleValues
        throw EstimateOutsideScaleException(estimate)

    if round.AgreedEstimate is not null
        throw RoundAlreadyEstimatedException(taskId)

    round.select(estimate)
    task.recordAgreedEstimate(estimate)
    round.Phase = Estimated
    Revision += 1
    raise AgreedEstimateSelected(SessionId, taskId, estimate, Revision)
```

Nielegalna operacja nie zwraca `false`, nie loguje i nie kontynuuje. Rzuca
nazwany blad domenowy. Proponowany zamkniety zestaw dla tego refaktoru:

- `NoActiveTaskException`;
- `TaskIsNotActiveException`;
- `VotingRoundNotStartedException`;
- `RoundNotRevealedException`;
- `EstimateOutsideScaleException`;
- `RoundAlreadyEstimatedException`;
- `ConcurrentPlanningSessionUpdateException`;
- istniejacy `SessionTaskNotFoundException`
  (`src/PlanDeck/Core/PlanDeck.Application/Abstractions/ISessionRepository.cs:42-46`).

`ResetActiveRound` jest jedyna legalna droga do ponownego glosowania po
`Estimated`; atomowo czysci glosy, reveal i `AgreedEstimate`. Rownolegly drugi
select z nieaktualna rewizja konczy sie
`ConcurrentPlanningSessionUpdateException`, a nie cichym last-write-wins.

### Repozytorium agregatu i jedna transakcja

Usunac punktowe `SetAgreedEstimateAsync` z
`ISessionRepository` (`src/PlanDeck/Core/PlanDeck.Application/Abstractions/ISessionRepository.cs:5-25`).
Zastapic je granica agregatu:

```csharp
public interface IPlanningSessionRepository
{
    Task<PlanningSession?> LoadAsync(
        Guid sessionId,
        CancellationToken cancellationToken);

    Task SaveAsync(
        PlanningSession session,
        long expectedRevision,
        CancellationToken cancellationToken);
}
```

Przypadek uzycia:

```text
begin SQL transaction
    session = repository.LoadAsync(command.SessionId)
        ?? throw SessionNotFoundException

    expectedRevision = session.Revision
    session.SelectAgreedEstimate(command.TaskId, command.Estimate)

    repository.SaveAsync(session, expectedRevision)
        -- EF UPDATE includes WHERE Revision = expectedRevision
        -- zero changed rows => ConcurrentPlanningSessionUpdateException

    outbox.Add(session.DequeueDomainEvents())
    SaveChanges once
commit SQL transaction
```

W tej samej transakcji sa: faza rundy, wynik rundy, `SessionTask.AgreedEstimate`,
nowa rewizja agregatu i zdarzenie outbox. Broadcast SignalR nastapi po commitcie
z outboxa. Nie stosowac transakcji rozproszonej z ADO: handler write-back
odczytuje dopiero zatwierdzony stan `Estimated`, wykonuje ADO PATCH z `test
/rev` (obecny mechanizm: `src/PlanDeck/Core/PlanDeck.Infrastructure/AzureDevOps/AzureDevOpsWorkItemClient.cs:77-86`)
i zapisuje jawny status/revision w osobnej, retryowalnej operacji.

### Cienki hub/API

```text
SelectEstimate(sessionIdText, taskIdText, valueText, clientRevision):
    require non-guest/authenticated principal
    parse sessionId, taskId and EstimateValue
    try:
        state = commandHandler.Handle(
            SelectAgreedEstimate(sessionId, taskId, value, clientRevision))
        return state
    catch TaskIsNotActiveException:
        throw HubException(code: "task-not-active")
    catch RoundNotRevealedException:
        throw HubException(code: "round-not-revealed")
    catch EstimateOutsideScaleException:
        throw HubException(code: "estimate-outside-scale")
    catch ConcurrentPlanningSessionUpdateException:
        throw HubException(code: "round-conflict")
```

Hub nie odczytuje `PlanningRoomService`, nie zapisuje repozytorium i nie
aktualizuje drugiego modelu. Handler laduje agregat, wywoluje jedna metode,
zapisuje jedna transakcje i zwraca projekcje. UI uzywa capabilities/fazy
zwracanej przez serwer; jego warunki pozostaja UX-em, nie zabezpieczeniem.

## 5. Before/after wedlug obecnych miejsc reguly

| Dzisiejsze miejsce | Before | After |
| --- | --- | --- |
| `Web/PlanDeck.Client/Pages/VotingRoom.razor` | Jedyny pelny warunek reveal przed pokazaniem pick (`:138-153`). | Renderuje `CanSelectEstimate`/phase ze stanu serwera; nie jest straznikiem. |
| `Web/PlanDeck.Client/Pages/VotingRoom.razor.cs` | Wysyla aktywny task i połyka szczegol bledu (`:132-157`). | Wysyla `clientRevision`; mapuje stabilny error code na lokalizowany komunikat. |
| `Core.Shared/Realtime/PlanningRoomState.cs` | Osobne `IsRevealed` i `Revision`, brak fazy/capabilities (`:3-24`). | `RoundPhase`, `Revision`, `CanVote`, `CanReveal`, `CanSelectEstimate`, bez ujawniania vote przed reveal. |
| `Web/PlanDeck.Server/Hubs/PlanningRoomHub.cs` | Koordynuje auth, skale, repozytorium, pamiec, lock i broadcast (`:108-131`). | Parse -> command handler -> mapowanie nazwanych bledow; zero logiki przejscia i zero lockow statycznych. |
| `Application/Planning/PlanningRoomService.cs` | Pamieciowe `ApplyAgreedEstimate` zna task, ale nie phase/active invariant (`:313-343`). | Projekcja/presence albo usunieta; nie jest zrodlem prawdy. |
| `Application/Planning/VotingRoundService.cs` | `SelectEstimateAsync` to passthrough do punktowego update (`:64-67`). | Handler laduje `PlanningSession`, wywoluje metode domenowa i zapisuje agregat. |
| `Application/Domain/PlanningSession.cs` | Anemiczna sesja bez active task/round/version (`:3-20`). | Behavior-rich root z prywatnymi kolekcjami, fazami rund i rewizja. |
| `Application/Domain/SessionTask.cs` | Publiczny setter `AgreedEstimate` (`:3-24`). | Prywatny setter; zmiana mozliwa tylko z root `PlanningSession`. |
| `Application/Abstractions/ISessionRepository.cs` | Punktowe `SetAgreedEstimateAsync` (`:19`). | `IPlanningSessionRepository.LoadAsync/SaveAsync(expectedRevision)`. |
| `Infrastructure/Persistence/SessionRepository.cs` | Bezposredni update pola i osobny `SaveChanges` (`:65-76`). | Hydratacja calego agregatu i optimistic concurrency dla `Revision`. |
| Konfiguracje EF | Tylko limit `AgreedEstimate`; brak round/version (`SessionTaskConfiguration.cs:30-41`, `PlanningSessionConfiguration.cs:21-47`). | Tabele/owned entities dla rund i glosow, required phase, unique `(SessionId, TaskId, ParticipantId)`, concurrency token `Revision`. |
| `SignalRPlanningRoomNotifier.cs` | Best-effort catch-all po commitcie (`:20-37`). | Outbox zapisany z agregatem; dispatcher retryuje i oznacza blad, bez cofania zatwierdzonej domeny. |
| Test konkurencji | Akceptuje `"3"` lub `"5"` bez reveal (`PlanningRoomHubTests.cs:593-629`). | Jeden select wygrywa; drugi dostaje `round-conflict` lub `round-already-estimated`; stan DB i event sa jednoznaczne. |

## 6. Plan faz refaktoru

### Faza 1 - testy charakteryzujace i kontrakt niezmiennika (test-first)

1. Dodac failing unit tests dla jawnego automatu
   `Hidden -> Revealed -> Estimated`.
2. Dodac failing integration tests, ktore wywoluja hub bez UI:
   select przed reveal, select dla nieaktywnego taska i rownolegly select.
3. Zachowac istniejace testy hidden vote, reveal i reset
   (`src/PlanDeck/Tests/PlanDeck.Unit.Tests/Planning/PlanningRoomServiceTests.cs:34-122`).

### Faza 2 - behavior-rich aggregate (test-first)

1. Wzbogacic `PlanningSession` i wprowadzic wewnetrzne `VotingRound`,
   `VotingRoundPhase`, `EstimateValue` oraz nazwane bledy.
2. Zamknac settery stanu krytycznego.
3. Uruchomic wyłącznie unit tests agregatu do green; bez EF, SignalR i ADO.

### Faza 3 - trwalosc i konkurencja (test-first integration)

1. Dodac mapping rund, glosow, aktywnego taska i `Revision`.
2. Dodac migracje EF bez zmiany publicznego zachowania UI.
3. Zaimplementowac `IPlanningSessionRepository` i optimistic concurrency.
4. Jedna transakcja zapisuje phase + estimate + revision + outbox.
5. Udowodnic restart/reload oraz konflikt dwoch writerow testami integration.

### Faza 4 - cienki command handler i hub (test-first integration)

1. Zastapic sekwencje hub -> punktowy repo update -> pamiec komenda agregatu.
2. Mapowac kazdy nazwany blad na stabilny kod transportowy.
3. Usunac `SessionLocks` dla operacji agregatu.
4. Utrzymac server-side guest/session authorization przed komenda.

### Faza 5 - projekcja realtime i klient

1. Budowac `PlanningRoomState` z trwalej projekcji agregatu plus efemerycznego
   presence.
2. Rozszerzyc kontrakt o phase, revision i capabilities.
3. UI ma tylko odzwierciedlac capabilities i lokalizowac error codes.
4. Outbox publikuje `RoomStateChanged`; awaria broadcastu jest retryowalna,
   a nie success-shaped silent fallback.

### Faza 6 - write-back i cleanup

1. Dopuszczac write-back tylko z trwalego stanu `Estimated`.
2. Zachowac ADO optimistic `/rev`; zapisac jawny status synchronizacji.
3. Usunac `SetAgreedEstimateAsync`, `ApplyAgreedEstimate` i podwojny model stanu.
4. Usunac testy zatwierdzajace nielegalny last-write-wins i zastapic je
   testami kontraktu.

## 7. Macierz testow niezmiennika

### Unit - agregat (test-first)

| Przypadek | Oczekiwany rezultat |
| --- | --- |
| Cast valid value w `Hidden` dla aktywnego taska | Glos zapisany, phase nadal `Hidden`, revision +1. |
| Ponowny cast tej samej osoby przed reveal | Jeden glos, nowa wartosc, brak duplikatu. |
| Cast po reveal | `RoundAlreadyRevealedException`; zero zmiany. |
| Reveal aktywnej rundy z czesciowa frekwencja | `Revealed`; obecna semantyka pozostaje legalna (`PlanningRoomServiceTests.cs:64-78`). |
| Select po reveal, aktywny task, wartosc ze skali | `Estimated`, task ma wynik, revision +1, event utworzony. |
| Select przed reveal | `RoundNotRevealedException`; brak wyniku/eventu. |
| Select dla nieaktywnego taska | `TaskIsNotActiveException`; brak wyniku/eventu. |
| Select spoza skali | `EstimateOutsideScaleException`; brak wyniku/eventu. |
| Drugi select po `Estimated` | `RoundAlreadyEstimatedException`; brak nadpisania. |
| Reset po `Estimated` | Atomowo `Hidden`, puste glosy i `AgreedEstimate = null`. |
| Nawigacja zmienia revision miedzy odczytem a select | Stary command nie moze zatwierdzic wyniku. |

### Integration - EF/repository/hub (test-first)

| Przypadek | Oczekiwany rezultat |
| --- | --- |
| Reload po cast/reveal | Active task, glosy, phase i revision odtworzone. |
| Select legalny | Jedna transakcja utrwala phase, estimate, revision i outbox. |
| Blad SaveChanges | Brak czesciowego estimate, phase i eventu. |
| Dwa selecty z ta sama expected revision | Jeden commit; drugi `ConcurrentPlanningSessionUpdateException`. |
| Select przez bezposrednie wywolanie SignalR przed reveal | Stabilny `round-not-revealed`, brak broadcastu i zapisu. |
| Select dla nieaktywnego taska | Stabilny `task-not-active`, brak broadcastu i zapisu. |
| Guest select | Nadal odrzucony przed wejsciem do agregatu. |
| Restart procesu | Runda i wynik nie wracaja do seeda pierwszego taska. |

### E2E - tylko krytyczny kontrakt

Istniejacy test obejmuje legalny happy path i reload
(`src/PlanDeck/Tests/PlanDeck.E2e.Tests/VotingRoomTests.cs:14-42`). Zachowac go
i dodac jeden negatywny scenariusz przez drugi/stary klient: po reveal klient
ze stara rewizja probuje zapisac wynik po zmianie aktywnego taska i otrzymuje
lokalizowany konflikt. Szczegoly automatu pozostaja w szybszych unit/integration
tests zgodnie z test planem (`context/foundation/test-plan.md:11-43`).

## 8. Load-bearing names

Nie znaleziono dedykowanego rejestru kontraktow (`context/` nie zawiera
"contract registry" ani "load-bearing"). Ponizsze nazwy trzeba jednak traktowac
jako load-bearing i dopisac do ubiquitous language przy wdrozeniu:

- `PlanningSession` - aggregate root;
- `VotingRound` - encja wewnetrzna agregatu;
- `VotingRoundPhase` - `Hidden`, `Revealed`, `Estimated`;
- `EstimateValue`, `ParticipantId` - value objects;
- `IPlanningSessionRepository` - jedyna granica zapisu agregatu;
- `SelectAgreedEstimateCommand`;
- `AgreedEstimateSelected`;
- `PlanningSessionRevision`;
- `RoomPresence` - jawnie efemeryczny stan polaczen;
- kody transportowe: `round-not-revealed`, `task-not-active`,
  `estimate-outside-scale`, `round-already-estimated`, `round-conflict`.

Istniejace `PlanningRoomState` pozostaje load-bearing kontraktem wire
(`src/PlanDeck/Core/PlanDeck.Core.Shared/Realtime/PlanningRoomState.cs:3-24`);
jego zmiana wymaga jednoczesnej aktualizacji klienta, huba i testow.

## 9. Kryteria zakonczenia refaktoru

Refaktor jest zakonczony dopiero, gdy:

1. nie istnieje publiczna ani repozytoryjna droga ustawienia
   `AgreedEstimate` z pominieciem `PlanningSession.SelectAgreedEstimate`;
2. select przed reveal i dla nieaktywnego taska fail-fast na serwerze;
3. phase, active task, votes, estimate, revision i outbox zatwierdzaja sie w
   jednej transakcji SQL;
4. restart odtwarza aktywna runde;
5. rownolegle komendy maja jeden deterministyczny winner i jawny conflict;
6. UI nie jest jedynym straznikiem zadnego przejscia;
7. ADO write-back czyta wyłącznie zatwierdzony stan `Estimated`;
8. stare punktowe update'y i procesowe locki zostaly usuniete.
