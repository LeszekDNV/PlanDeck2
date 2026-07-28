---
date: 2026-07-28T16:34:37.227+02:00
researcher: GitHub Copilot CLI
git_commit: 1fef5794dc3e7694ce014038f99f7125bc8d6ec3
branch: main
repository: LeszekDNV/PlanDeck2
topic: "Jak głęboko subdomena Core jest dziś rozsmarowana po warstwach"
tags: [research, codebase, core-domain, voting-round, architecture]
status: complete
last_updated: 2026-07-28
last_updated_by: GitHub Copilot CLI
---

# Research: Jak głęboko subdomena Core jest dziś rozsmarowana po warstwach

**Date**: 2026-07-28T16:34:37.227+02:00  
**Researcher**: GitHub Copilot CLI  
**Git Commit**: `1fef5794dc3e7694ce014038f99f7125bc8d6ec3`  
**Branch**: `main`  
**Repository**: `LeszekDNV/PlanDeck2`

## Research Question

Na podstawie [`context/domain/01-domain-distillation.md`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/context/domain/01-domain-distillation.md): jak głęboko subdomena Core jest dziś rozsmarowana po warstwach?

## Summary

Subdomena Core jest rozsmarowana **głęboko: przez wszystkie pięć warstw runtime** (`Application`, `Core.Shared`, `Infrastructure`, `Server`, `Client`). Sam rozkład technicznych obowiązków nie jest błędem: transport powinien pozostać w hoście, persystencja w Infrastructure, a stan prezentacji w Client. Problem polega na tym, że **jeden niezmiennik biznesowy rundy jest przecięty między warstwy i żadna z nich nie posiada go w całości**:

1. `PlanningRoomService` w Application pilnuje oddania i ukrycia głosu.
2. `PlanningRoomHub` w Server prowadzi przejście reveal -> wybór estymaty i ustala kolejność zapisu.
3. `SessionRepository` w Infrastructure zapisuje wynik, nie znając stanu rundy.
4. `VotingRoom` w Client zna część reguł gościa i stanów rundy, ale nie może ich autorytatywnie egzekwować.
5. `Core.Shared` przenosi semantykę jako luźne DTO (`string?`, flagi i licznik rewizji), bez kontraktu automatu stanów.

Największym problemem nie jest więc liczba projektów dotkniętych przez Core, lecz **brak jednej transakcyjnej, wersjonowanej granicy dla sekwencji `active task -> votes -> reveal -> agreed estimate`**. Encje są anemiczne, runda nie istnieje jako trwały agregat, a host SignalR pełni rolę serwisu aplikacyjnego. W rezultacie wynik może zostać zapisany przed reveal lub dla nieaktywnego zadania, a aktywna runda znika po restarcie.

### Ocena głębokości

| Warstwa | Głębokość Core | Ocena |
| --- | ---: | --- |
| `PlanDeck.Application` | **5/5** | Zawiera encje, przypadki użycia, pamięciowy pokój, walidację głosu i orkiestrację ADO, ale model jest proceduralny i anemiczny. |
| `PlanDeck.Core.Shared` | **3/5** | Eksponuje pełny słownik rundy i sesji w kontraktach, lecz głównie jako wire state bez zachowania. |
| `PlanDeck.Infrastructure` | **4/5** | Egzekwuje trwałe więzy, tenant isolation i ADO concurrency; przyjmuje jednak mutację estymaty bez kontekstu rundy. |
| `PlanDeck.Server` | **5/5** | Hub i endpoint gościa realizują przypadki użycia, autoryzację moderatorską, kolejność mutacji i procesową konkurencję. |
| `PlanDeck.Client` | **2/5** | Głównie prezentuje i wysyła komendy, ale duplikuje część polityki gościa i interpretuje stan rundy. |

## Detailed Findings

### 1. Application skupia pojęcia, ale nie agregat Core

`PlanningSession` i `SessionTask` są strukturami danych z publicznymi setterami; `AgreedEstimate` jest zwykłym `string?`, a sesja nie ma metod typu `Activate`, `Reveal`, `SelectEstimate` ani `Complete` ([`PlanningSession.cs:3-20`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/src/PlanDeck/Core/PlanDeck.Application/Domain/PlanningSession.cs#L3-L20), [`SessionTask.cs:3-24`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/src/PlanDeck/Core/PlanDeck.Application/Domain/SessionTask.cs#L3-L24)). Lifecycle kończy się na `Draft` i `Active` ([`SessionStatus.cs:3-7`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/src/PlanDeck/Core/PlanDeck.Application/Domain/SessionStatus.cs#L3-L7)).

Zachowanie znajduje się w dwóch dużych serwisach:

- `SessionGrpcService` obsługuje tworzenie, konfigurację, aktywację, import ADO i write-back ([`SessionGrpcService.cs:28-104`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/src/PlanDeck/Core/PlanDeck.Application/Services/SessionGrpcService.cs#L28-L104), [`SessionGrpcService.cs:334-435`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/src/PlanDeck/Core/PlanDeck.Application/Services/SessionGrpcService.cs#L334-L435)).
- `PlanningRoomService` przechowuje pokój w `ConcurrentDictionary`, sprawdza aktywne zadanie, skalę i zakaz głosu po reveal oraz ukrywa wartości w projekcji ([`PlanningRoomService.cs:7-10`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/src/PlanDeck/Core/PlanDeck.Application/Planning/PlanningRoomService.cs#L7-L10), [`PlanningRoomService.cs:221-270`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/src/PlanDeck/Core/PlanDeck.Application/Planning/PlanningRoomService.cs#L221-L270), [`PlanningRoomService.cs:444-480`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/src/PlanDeck/Core/PlanDeck.Application/Planning/PlanningRoomService.cs#L444-L480)).

Wewnętrzne `PlanningRoom`, `RoomTask` i `Participant` są faktycznym modelem rundy, lecz są prywatnymi, nietrwałymi klasami implementacyjnymi ([`PlanningRoomService.cs:485-523`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/src/PlanDeck/Core/PlanDeck.Application/Planning/PlanningRoomService.cs#L485-L523)). To skupia część zachowania, ale nie tworzy granicy agregatu zdolnej objąć zapis wyniku.

### 2. Server jest faktycznym koordynatorem rundy

`PlanningRoomHub` nie jest wyłącznie adapterem SignalR. Rozstrzyga, kto może wykonać akcję moderatorską, serializuje operacje per sesja, waliduje skalę, zapisuje estymatę, aktualizuje pokój i publikuje stan ([`PlanningRoomHub.cs:69-131`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/src/PlanDeck/Web/PlanDeck.Server/Hubs/PlanningRoomHub.cs#L69-L131)).

Najważniejszy przypadek użycia Core, `SelectEstimate`, jest proceduralnie złożony w hoście:

1. host bierze procesową blokadę,
2. sprawdza wartość względem skali,
3. wywołuje zapis SQL,
4. aktualizuje pamięciową projekcję,
5. rozgłasza wynik.

Jednocześnie nie sprawdza, czy głosy zostały odsłonięte ani czy `taskId` jest aktywnym zadaniem ([`PlanningRoomHub.cs:108-131`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/src/PlanDeck/Web/PlanDeck.Server/Hubs/PlanningRoomHub.cs#L108-L131)). `SemaphoreSlim` jest lokalny dla procesu, więc nie daje deterministycznej semantyki w wielu instancjach ([`PlanningRoomHub.cs:17`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/src/PlanDeck/Web/PlanDeck.Server/Hubs/PlanningRoomHub.cs#L17)).

Guest flow również omija przypadek użycia Application: endpoint `/guest/join` bezpośrednio rozwiązuje kod przez repozytorium, tworzy participant ID i cookie ([`Program.cs:91-126`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/src/PlanDeck/Web/PlanDeck.Server/Program.cs#L91-L126)). Hub następnie egzekwuje zasadę „gość głosuje, ale nie moderuje” ([`PlanningRoomHub.cs:228-275`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/src/PlanDeck/Web/PlanDeck.Server/Hubs/PlanningRoomHub.cs#L228-L275)). To jest poprawna granica bezpieczeństwa, ale polityka produktu nie ma odpowiednika w modelu domenowym.

### 3. Infrastructure utrwala fragment wyniku, nie przebieg decyzji

EF zapisuje sesję, zadania i `AgreedEstimate`, lecz nie ma encji dla aktywnej rundy, głosów, reveal ani jej rewizji ([`PlanDeckDbContext.cs:17-40`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/PlanDeckDbContext.cs#L17-L40)). `SetAgreedEstimateAsync` przyjmuje dowolny `string?` dla wskazanego zadania sesji; nie może udowodnić pochodzenia wyniku z poprawnej rundy ([`SessionRepository.cs:65-76`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/SessionRepository.cs#L65-L76)).

Infrastructure poprawnie egzekwuje niezmienniki, które dają się wyrazić relacyjnie:

- izolację tenantów i fail-closed writes ([`PlanDeckDbContext.cs:63-72`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/PlanDeckDbContext.cs#L63-L72), [`PlanDeckDbContext.cs:151-192`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/PlanDeckDbContext.cs#L151-L192));
- globalną unikalność aktywnego share code ([`PlanningSessionConfiguration.cs:36-40`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/Configurations/PlanningSessionConfiguration.cs#L36-L40));
- brak duplikatu ADO work itemu w sesji ([`SessionTaskConfiguration.cs:33-35`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/Configurations/SessionTaskConfiguration.cs#L33-L35));
- concurrency zewnętrznego work itemu przez `test /rev` i mapowanie 409/412 ([`AzureDevOpsWorkItemClient.cs:67-94`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/src/PlanDeck/Core/PlanDeck.Infrastructure/AzureDevOps/AzureDevOpsWorkItemClient.cs#L67-L94), [`AzureDevOpsWorkItemClient.cs:205-224`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/src/PlanDeck/Core/PlanDeck.Infrastructure/AzureDevOps/AzureDevOpsWorkItemClient.cs#L205-L224)).

Naturalne adaptery są więc dobrze umieszczone. Przeciek zaczyna się tam, gdzie repozytorium udostępnia domenowo nazwaną mutację wyniku bez warunku stanu rundy.

### 4. Shared utrwala słownik, ale spłaszcza zachowanie

`PlanningRoomState` udostępnia `CurrentTaskId`, `IsRevealed`, uczestników, zadania, skalę i `Revision`; uczestnik ma osobno `HasVoted` i nullable `Vote` ([`PlanningRoomState.cs:3-24`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/src/PlanDeck/Core/PlanDeck.Core.Shared/Realtime/PlanningRoomState.cs#L3-L24)). Kontrakty sesji przenoszą status, share code, źródło zadania, rewizję ADO i uzgodnioną estymatę ([`ISessionService.cs:70-132`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/src/PlanDeck/Core/PlanDeck.Core.Shared/Contracts/ISessionService.cs#L70-L132)).

To właściwe miejsce dla wire contracts, ale obecny kształt eksponuje mechaniczne flagi zamiast jawnego stanu rundy. `Revision` jest licznikiem pamięciowej projekcji, nie trwałym tokenem concurrency, a `AgreedEstimate` nie niesie informacji o rundzie, z której pochodzi.

### 5. Client zna Core, ale przeważnie nie jest źródłem reguł

`VotingRoom` interpretuje `IsRevealed`, `CurrentTaskId`, lokalny `_myVote` i claim gościa. Blokuje akcje moderatorskie po stronie UX oraz resetuje lokalny wybór po zmianie zadania ([`VotingRoom.razor.cs:82-140`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/src/PlanDeck/Web/PlanDeck.Client/Pages/VotingRoom.razor.cs#L82-L140)). To częściowo nieunikniona semantyka prezentacji, ale `_isGuest` duplikuje regułę hosta.

Wrapper SignalR pozostaje cienki i głównie wysyła komendy ([`PlanningRoomClientService.cs:80-103`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/src/PlanDeck/Web/PlanDeck.Client/Services/PlanningRoomClientService.cs#L80-L103)). `PlanningRoomStateRevisionGate` chroni klienta przed stanami dostarczonymi poza kolejnością, ale nie rozwiązuje konkurencji domenowej ([`PlanningRoomStateRevisionGate.cs:10-24`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/src/PlanDeck/Web/PlanDeck.Client/Services/PlanningRoomStateRevisionGate.cs#L10-L24)).

### 6. Testy potwierdzają pękniętą granicę

Testy jednostkowe dobrze pokrywają lokalne reguły `PlanningRoomService`, m.in. ukrycie głosu i zakaz głosu po reveal ([`PlanningRoomServiceTests.cs:34-47`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/src/PlanDeck/Tests/PlanDeck.Unit.Tests/Planning/PlanningRoomServiceTests.cs#L34-L47), [`PlanningRoomServiceTests.cs:81-88`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/src/PlanDeck/Tests/PlanDeck.Unit.Tests/Planning/PlanningRoomServiceTests.cs#L81-L88)). Integracja huba sprawdza ważność sesji i autoryzację hosta ([`PlanningRoomHubTests.cs:96-102`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/src/PlanDeck/Tests/PlanDeck.Integration.Tests/Realtime/PlanningRoomHubTests.cs#L96-L102), [`PlanningRoomHubTests.cs:175-200`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/src/PlanDeck/Tests/PlanDeck.Integration.Tests/Realtime/PlanningRoomHubTests.cs#L175-L200)).

E2E dowodzi happy path `vote -> reveal -> pick -> reload` ([`VotingRoomTests.cs:15-42`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/src/PlanDeck/Tests/PlanDeck.E2e.Tests/VotingRoomTests.cs#L15-L42)), lecz nie ma testu odrzucenia wyboru estymaty przed reveal/dla nieaktywnego zadania. Brak takiego testu odpowiada brakowi jednej warstwy, którą można byłoby testować jako właściciela całego niezmiennika.

## Architecture Insights

### Co jest zdrowym podziałem

- SignalR pozostaje mechanizmem transportowym, a Client odbiorcą projekcji.
- EF egzekwuje tenant isolation, klucze obce i unikalność.
- Adapter ADO odpowiada za HTTP, JSON Patch i mapowanie błędów zewnętrznych.
- Application orkiestruje import i write-back przez abstrakcję klienta ADO.

### Co jest szkodliwym rozsmarowaniem

- Host wykonuje przypadek użycia `SelectEstimate`, zamiast przekazać jedną komendę do właściciela agregatu.
- Pamięciowy model rundy i trwały wynik mają różne źródła prawdy.
- Repozytorium zapisuje rezultat bez oczekiwanej rewizji i bez warunków `Revealed` oraz `ActiveTaskId`.
- Encje domenowe nie chronią własnych przejść; reguły żyją w serwisach, hubie i konfiguracji EF.
- Application jest dodatkowo związane z transportem przez implementacje usług gRPC, co łączy use case z wire errors.
- Guest credential jest kształtowany przez endpoint, claims i repozytorium, bez jawnego modelu `GuestAccessGrant`.

### Docelowa granica

Pierwszy refaktor powinien wprowadzić trwały, wersjonowany agregat `VotingRound` obejmujący co najmniej:

- `SessionId` i aktywne `TaskId`;
- stan rundy (`Voting`, `Revealed`, `Completed`);
- głosy per participant;
- uzgodnioną estymatę;
- rewizję/concurrency token.

Hub powinien stać się adapterem: zbudować tożsamość, wysłać komendę i rozgłosić zwróconą projekcję. Repozytorium powinno zapisywać zmianę agregatu z oczekiwaną rewizją, a nie osobno `AgreedEstimate`. `PlanningRoomService` może pozostać projekcją realtime lub cache, ale nie źródłem prawdy.

## Code References

- [`PlanningRoomService.cs:221-270`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/src/PlanDeck/Core/PlanDeck.Application/Planning/PlanningRoomService.cs#L221-L270) - lokalne niezmienniki oddania i odsłonięcia głosu.
- [`PlanningRoomService.cs:485-523`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/src/PlanDeck/Core/PlanDeck.Application/Planning/PlanningRoomService.cs#L485-L523) - faktyczny, ale prywatny i pamięciowy model rundy.
- [`VotingRoundService.cs:64-67`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/src/PlanDeck/Core/PlanDeck.Application/Planning/VotingRoundService.cs#L64-L67) - wybór estymaty delegowany bez walidacji stanu rundy.
- [`PlanningRoomHub.cs:108-131`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/src/PlanDeck/Web/PlanDeck.Server/Hubs/PlanningRoomHub.cs#L108-L131) - przypadek użycia Core zaimplementowany w hoście.
- [`SessionRepository.cs:65-76`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/SessionRepository.cs#L65-L76) - trwała mutacja wyniku bez kontekstu rundy.
- [`SessionGrpcService.cs:334-414`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/src/PlanDeck/Core/PlanDeck.Application/Services/SessionGrpcService.cs#L334-L414) - bezpieczny write-back ADO, ale oparty na już zapisanym wyniku.
- [`PlanningRoomState.cs:3-24`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/src/PlanDeck/Core/PlanDeck.Core.Shared/Realtime/PlanningRoomState.cs#L3-L24) - wire state rundy.
- [`VotingRoom.razor.cs:82-140`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/src/PlanDeck/Web/PlanDeck.Client/Pages/VotingRoom.razor.cs#L82-L140) - interpretacja stanu i duplikacja części polityki gościa w UI.

## Historical Context (from prior changes)

Pierwszy realtime slice świadomie wybrał pamięciowy, autorytatywny pokój dla MVP ([`realtime-vote-integrity/plan.md:29-55`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/context/archive/2026-06-22-realtime-vote-integrity/plan.md#L29-L55)). Kolejny slice dodał per-task voting i trwałe `AgreedEstimate`, pozostawiając głosy, aktywne zadanie oraz reveal w pamięci ([`realtime-voting-round/plan.md:40-50`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/context/archive/2026-06-22-realtime-voting-round/plan.md#L40-L50)).

Guest voting świadomie rozdzielił credential między share code, scoped claims i hub authorization ([`guest-link-voting/plan.md:50-56`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/context/archive/2026-06-24-guest-link-voting/plan.md#L50-L56)). ADO write-back został poprawnie rozdzielony na orkiestrację Application i adapter Infrastructure ([`ado-estimate-writeback/plan-brief.md:22-38`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/context/archive/2026-06-24-ado-estimate-writeback/plan-brief.md#L22-L38)).

Obecne luki są już nazwane w aktywnych zmianach:

- [`persist-voting-round-state/change.md:10-12`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/context/changes/persist-voting-round-state/change.md#L10-L12) - trwałość aktywnego zadania, głosów, reveal i rewizji;
- [`enforce-voting-round-transitions/change.md:10-12`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/context/changes/enforce-voting-round-transitions/change.md#L10-L12) - wybór estymaty wyłącznie po reveal i dla aktywnego zadania;
- [`expire-revoke-guest-access/change.md:10-12`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/context/changes/expire-revoke-guest-access/change.md#L10-L12) - wygaśnięcie, rotacja i odwołanie dostępu gościa.

Zmiana projekt/sesja rozbudowała supporting subdomain i role, ale nie naprawia granicy rundy ([`reorganize-project-and-sessions/plan.md:73-114`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/context/changes/reorganize-project-and-sessions/plan.md#L73-L114)).

## Related Research

- [`context/archive/2026-06-24-ado-estimate-writeback/research.md`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/context/archive/2026-06-24-ado-estimate-writeback/research.md) - granice import -> decyzja -> write-back.
- [`context/archive/2026-06-24-azure-devops-import/research.md`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/context/archive/2026-06-24-azure-devops-import/research.md) - integracja i mapowanie ADO.
- [`context/archive/2026-06-27-testing-critical-path-integrity/research.md`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/context/archive/2026-06-27-testing-critical-path-integrity/research.md) - pokrycie krytycznych przepływów.
- [`context/changes/reorganize-project-and-sessions/research.md`](https://github.com/LeszekDNV/PlanDeck2/blob/1fef5794dc3e7694ce014038f99f7125bc8d6ec3/context/changes/reorganize-project-and-sessions/research.md) - ewolucja projektu jako supporting boundary.

## Open Questions

1. Czy `VotingRound` ma być agregatem podrzędnym `PlanningSession`, czy niezależnym rootem z własnym repozytorium i concurrency tokenem?
2. Czy po wyborze estymaty runda staje się nieodwracalnie `Completed`, czy moderator może ją ponownie otworzyć?
3. Czy głosy należy przechowywać jako dane audytowe po zakończeniu rundy, czy usuwać po utrwaleniu wyniku?
4. Czy `PlanningRoomState.Revision` ma stać się trwałą rewizją agregatu, czy pozostać wyłącznie numerem projekcji realtime?
5. Czy `persist-voting-round-state` i `enforce-voting-round-transitions` powinny zostać połączone w jedną zmianę, skoro trwałość bez automatu stanów nadal pozostawi niechroniony wynik?
