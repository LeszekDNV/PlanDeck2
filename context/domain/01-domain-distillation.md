---
title: "PlanDeck - destylacja domeny"
created: 2026-07-28
type: domain-distillation
---

# PlanDeck - destylacja domeny

## 0. Kontekst projektu i źródła

### Materiał źródłowy

Najbardziej aktualnym dokumentem wymagań jest `context/foundation/prd.md`. Opisuje on produkt jako narzędzie planning-poker, którego zakładem produktowym jest prostota konfiguracji oraz pełny obieg Azure DevOps: import zadania, głosowanie, wybór estymaty i zapis wyniku z powrotem (`context/foundation/prd.md:18-31`, `context/foundation/prd.md:40-54`).

Źródła uzupełniające:

- pierwotna notatka produktowa: `idea-notes.md:3-26`;
- notatki z kształtowania produktu: `context/foundation/shape-notes.md:35-48`, `context/foundation/shape-notes.md:60-80`;
- aktywne decyzje o trwałości i przejściach rundy: `context/changes/persist-voting-round-state/change.md:10-12`, `context/changes/enforce-voting-round-transitions/change.md:10-12`;
- historyczne plany wdrożonych zmian, zwłaszcza głosowanie, goście, multi-tenancy i ADO: `context/archive/2026-06-22-realtime-vote-integrity/plan.md:29-55`, `context/archive/2026-06-22-realtime-voting-round/plan.md:40-50`, `context/archive/2026-06-24-guest-link-voting/plan.md:50-56`, `context/archive/2026-06-24-ado-estimate-writeback/plan-brief.md:6-38`;
- aktualna reorganizacja wokół projektu: `context/changes/reorganize-project-and-sessions/plan.md:65-114`.

`README.md` zawiera wyłącznie nazwę projektu (`README.md:1`). Nie znaleziono `tech-stack.md`; stack i architektura są jednak opisane w PRD i shape notes (`context/foundation/prd.md:18-25`, `context/foundation/shape-notes.md:35-40`).

### Stack i rozmieszczenie odpowiedzialności

PlanDeck jest warstwową aplikacją .NET 10: Blazor Web App z hostowanym WebAssembly, MudBlazor, code-first gRPC/gRPC-Web, SignalR dla czasu rzeczywistego, EF Core/SQL oraz Aspire (`context/foundation/shape-notes.md:37-40`).

| Warstwa | Lokalizacja | Odpowiedzialność |
| --- | --- | --- |
| Kontrakty współdzielone | `src/PlanDeck/Core/PlanDeck.Core.Shared/` | DTO i kontrakty gRPC oraz stan przesyłany przez SignalR. |
| Model i przypadki użycia | `src/PlanDeck/Core/PlanDeck.Application/` | Encje domenowe, serwisy aplikacyjne, autoryzacja uczestnika i pamięciowy model pokoju. |
| Persystencja i integracje | `src/PlanDeck/Core/PlanDeck.Infrastructure/` | EF Core, repozytoria, izolacja tenantów, Azure DevOps i tożsamość. |
| Host/API czasu rzeczywistego | `src/PlanDeck/Web/PlanDeck.Server/` | Host gRPC, SignalR, uwierzytelnienie członka i gościa. |
| UI | `src/PlanDeck/Web/PlanDeck.Client/` | Przepływy projektów, sesji, zadań i pokoju głosowania. |
| Testy | `src/PlanDeck/Tests/` | Testy jednostkowe, integracyjne i Playwright E2E. |

Logika biznesowa nie jest już placeholderem, mimo że starsze dokumenty tak ją opisywały. Encje żyją w `PlanDeck.Application.Domain`, operacje sesji w `SessionGrpcService`, reguły rundy w `PlanningRoomService`, a trwałe ograniczenia w konfiguracjach EF i `PlanDeckDbContext`.

## 1. Ubiquitous Language

Terminologia poniżej pochodzi z dokumentów i została zestawiona z rzeczywistym kodem. Nazwy kodowe są wskazane tylko wtedy, gdy zostały zweryfikowane.

| Pojęcie | Definicja domenowa | Cytat źródłowy | Gdzie żyje w kodzie |
| --- | --- | --- | --- |
| **PlanDeck** | Narzędzie do prowadzenia sesji estymacyjnych SCRUM planning-poker: importuje zadania, konfiguruje sesję, zbiera głosy i zapisuje wynik. | „import tasks, configure a session, vote in real-time, and save results” (`context/foundation/prd.md:20-21`) | Zachowanie rozproszone między `SessionGrpcService`, `PlanningRoomService` i klientem; brak jednego bytu `PlanDeck`. |
| **Scrum Master / organizator** | Osoba przygotowująca i prowadząca sesję; w wizji jest personą, a nie formalną rolą. | „Scrum Master is simply whoever creates a session, not a separate role” (`context/foundation/prd.md:63`) | **BRAK dedykowanej roli w kodzie**. Twórcę sesji przechowuje `PlanningSession.CreatedByUserId` (`src/PlanDeck/Core/PlanDeck.Application/Domain/PlanningSession.cs:7-11`). |
| **Projekt** | Granica organizująca sesje oraz konfigurację Azure DevOps; użytkownik ogląda i modyfikuje sesje w kontekście projektu. | „Require callers to identify the Project whose Sessions are being listed” (`context/changes/reorganize-project-and-sessions/plan.md:73-82`) | `PlanDeckProject` (`src/PlanDeck/Core/PlanDeck.Application/Domain/PlanDeckProject.cs:3-10`); sesja wymaga `ProjectId` (`src/PlanDeck/Core/PlanDeck.Application/Services/SessionGrpcService.cs:32-50`). |
| **Zespół** | Trwała grupa użytkowników używana ponownie między sesjami. | „teams persist membership across sessions and drive assignment/notifications” (`context/foundation/prd.md:90-91`) | `Team` i `TeamMember` (`src/PlanDeck/Core/PlanDeck.Application/Domain/Team.cs:3-10`, `src/PlanDeck/Core/PlanDeck.Application/Domain/TeamMember.cs:3-20`). |
| **Sesja planistyczna** | Kontener projektu, zadań, skali, uczestników i przebiegu estymacji; zaczyna jako szkic i może zostać aktywowana. | „create a planning session from a set of selected tasks” (`context/foundation/prd.md:99-103`) | `PlanningSession` (`src/PlanDeck/Core/PlanDeck.Application/Domain/PlanningSession.cs:3-20`), statusy `Draft`/`Active` (`src/PlanDeck/Core/PlanDeck.Application/Domain/SessionStatus.cs:3-7`). |
| **Zadanie sesji** | Element estymowany podczas sesji; pochodzi z ADO albo jest utworzony ad hoc i może otrzymać uzgodnioną estymatę. | „imported from Azure DevOps or added ad-hoc” (`context/foundation/prd.md:127-129`) | `SessionTask` przechowuje źródło, identyfikator i rewizję ADO oraz `AgreedEstimate` (`src/PlanDeck/Core/PlanDeck.Application/Domain/SessionTask.cs:3-24`). |
| **Zadanie ad hoc** | Zadanie utworzone ręcznie, niezależne od zewnętrznego trackera. | „A user can create ad-hoc tasks manually” (`context/foundation/prd.md:96-97`) | `TaskSource.AdHoc` (`src/PlanDeck/Core/PlanDeck.Application/Domain/TaskSource.cs:3-7`). |
| **Zadanie Azure DevOps / work item** | Zadanie zaimportowane z ADO, powiązane z oryginalnym work itemem i jego rewizją. | „import selected tasks into PlanDeck” (`context/foundation/prd.md:93-95`) | `TaskSource.AzureDevOps`, `AdoWorkItemId`, `AdoRevision` (`src/PlanDeck/Core/PlanDeck.Application/Domain/SessionTask.cs:11-21`). |
| **Skala głosowania** | Zamknięty zbiór wartości dozwolonych jako głosy i finalna estymata; Fibonacci, T-shirt albo skala własna. | „task selection and voting scale” (`context/foundation/prd.md:102-103`) | `VotingScaleType` (`src/PlanDeck/Core/PlanDeck.Application/Domain/VotingScaleType.cs:3-8`) i `PlanningSession.ScaleValues` (`src/PlanDeck/Core/PlanDeck.Application/Domain/PlanningSession.cs:13-15`). |
| **Pokój planowania** | Bieżący, współdzielony w czasie rzeczywistym stan aktywnej sesji: uczestnicy, aktywne zadanie, skala, głosy, reveal i rewizja. | „The room is seeded ... with the session's ordered task list and scale” (`context/archive/2026-06-22-realtime-voting-round/plan.md:40-46`) | Pamięciowy `PlanningRoomService` i wewnętrzne `PlanningRoom`/`RoomTask` (`src/PlanDeck/Core/PlanDeck.Application/Planning/PlanningRoomService.cs:7-16`, `src/PlanDeck/Core/PlanDeck.Application/Planning/PlanningRoomService.cs:485-523`). |
| **Runda głosowania** | Cykl dla jednego aktywnego zadania: prywatne oddanie głosów, wspólne odsłonięcie, dyskusja, ręczny wybór estymaty. | `context/foundation/prd.md:137-143` | Nie ma trwałej encji rundy. Stan rundy jest polami `CurrentTaskId`, `Votes`, `IsRevealed`, `Revision` w pamięci (`src/PlanDeck/Core/PlanDeck.Application/Planning/PlanningRoomService.cs:485-516`). |
| **Uczestnik** | Osoba obecna w pokoju, identyfikowana stabilnym participant ID, z informacją online/offline i statusem oddania głosu. | „participants ... submit a vote privately” (`context/foundation/prd.md:139-143`) | Pamięciowy `Participant` i projekcja `PlanningParticipantState` (`src/PlanDeck/Core/PlanDeck.Application/Planning/PlanningRoomService.cs:450-466`, `src/PlanDeck/Core/PlanDeck.Application/Planning/PlanningRoomService.cs:519-523`). |
| **Przypisany członek sesji** | Znany użytkownik dopuszczony do sesji przez przypisanie adresu e-mail. | „assign/invite team members to a session” (`context/foundation/prd.md:104-105`) | `SessionMember` (`src/PlanDeck/Core/PlanDeck.Application/Domain/SessionMember.cs:3-12`); autoryzacja przez twórcę lub zgodność e-maila (`src/PlanDeck/Core/PlanDeck.Application/Planning/VotingRoundService.cs:69-84`). |
| **Gość** | Uczestnik bez konta, dopuszczony kodem jednej aktywnej sesji i ograniczony do głosowania. | `context/foundation/prd.md:114-115` | Brak encji DB. Dostęp wynika z cookie i zakresu `sid`; hub blokuje akcje moderatorskie (`src/PlanDeck/Web/PlanDeck.Server/Hubs/PlanningRoomHub.cs:24-35`, `src/PlanDeck/Web/PlanDeck.Server/Hubs/PlanningRoomHub.cs:69-110`). |
| **Głos ukryty** | Wartość wybrana przez uczestnika, niewidoczna dla innych aż do odsłonięcia; widoczny jest jedynie fakt oddania głosu. | `context/foundation/prd.md:107-111`, `context/foundation/prd.md:147-149` | `CastVote` odrzuca głos po reveal i spoza skali (`src/PlanDeck/Core/PlanDeck.Application/Planning/PlanningRoomService.cs:221-249`); projekcja ustawia `Vote=null` przed reveal (`src/PlanDeck/Core/PlanDeck.Application/Planning/PlanningRoomService.cs:444-464`). |
| **Reveal / odsłonięcie** | Jednoczesne ujawnienie wszystkich wartości po zakończeniu zbierania głosów. | „then all values appear together” (`context/foundation/prd.md:109-111`) | `RevealVotes` ustawia `IsRevealed` dla aktywnego zadania (`src/PlanDeck/Core/PlanDeck.Application/Planning/PlanningRoomService.cs:255-270`). |
| **Uzgodniona estymata** | Jedna wartość wybrana ręcznie po reveal; jest wynikiem rundy, a nie obliczeniem automatycznym. | `context/foundation/prd.md:139-143` | `SessionTask.AgreedEstimate` (`src/PlanDeck/Core/PlanDeck.Application/Domain/SessionTask.cs:23`), zapis przez `SessionRepository.SetAgreedEstimateAsync` (`src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/SessionRepository.cs:65-76`). |
| **Write-back** | Zapis numerycznej uzgodnionej estymaty do pola estymaty oryginalnego work itemu ADO z kontrolą rewizji. | „closes PlanDeck's import → vote → write-back loop” (`context/archive/2026-06-24-ado-estimate-writeback/plan-brief.md:6-12`) | `WriteTaskEstimateToAdoAsync` (`src/PlanDeck/Core/PlanDeck.Application/Services/SessionGrpcService.cs:334-415`). |
| **Tenant** | Najszersza granica izolacji danych wyznaczona organizacją użytkownika. | „a user only ever sees the teams and sessions they belong to” (`context/foundation/prd.md:76-80`) | Globalny filtr i fail-closed zapis w `PlanDeckDbContext` (`src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/PlanDeckDbContext.cs:63-72`, `src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/PlanDeckDbContext.cs:151-192`). |
| **Powiadomienie o starcie** | Wiadomość e-mail lub Teams wysyłana przypisanemu użytkownikowi po starcie sesji. | `context/foundation/prd.md:117-120` | **BRAK w kodzie domenowym i modelu persystencji**; `PlanDeckDbContext` nie zawiera takiego bytu (`src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/PlanDeckDbContext.cs:17-40`). |
| **Historia sesji** | Widok zakończonych sesji i wcześniejszych wyników. | `context/foundation/prd.md:120-121` | **BRAK jawnego modelu historii/zakończenia**; `SessionStatus` ma tylko `Draft` i `Active` (`src/PlanDeck/Core/PlanDeck.Application/Domain/SessionStatus.cs:3-7`). |

## 2. Klasyfikacja subdomen

Klasyfikacja bierze za punkt odniesienia cel główny: prosty, pełny przepływ import → sesja → głosowanie → decyzja → write-back (`context/foundation/prd.md:67-80`). Integracje inne niż ADO, aplikacje mobilne, formalna hierarchia uprawnień i alternatywne workflow głosowania są non-goals (`context/foundation/prd.md:165-172`).

| Obszar / pojęcia | Klasa | Uzasadnienie |
| --- | --- | --- |
| Ukryte głosowanie, reveal, aktywne zadanie, spójny stan uczestników | **Core** | Real-time jest nazwany „product's reason to exist”, a zsynchronizowany reveal centralnym doświadczeniem (`context/foundation/prd.md:107-111`). |
| Ręczne uzgodnienie jednej estymaty | **Core** | Jedyny wspierany workflow kończy się świadomym wyborem, bez automatycznego obliczenia (`context/foundation/prd.md:137-143`, `context/foundation/prd.md:170`). |
| Import ADO i bezpieczny write-back | **Core** | Pełny round-trip jest kryterium sukcesu i „north-star slice” (`context/foundation/prd.md:69-71`, `context/archive/2026-06-24-ado-estimate-writeback/plan-brief.md:6-12`). |
| Gościnne głosowanie przez kod | **Core** | Frictionless guest voting jest jawnym wyróżnikiem produktu (`context/foundation/prd.md:114-115`). |
| Sesja planistyczna i jej minimalna konfiguracja | **Core** | Prostota utworzenia dopasowanej sesji jest głównym zakładem produktu (`context/foundation/prd.md:27-31`, `context/foundation/prd.md:99-105`). |
| Projekt, członkostwo projektu, role Member/Admin/Owner | **Supporting** | Uporządkowują własność i autoryzację sesji, ale nie są doświadczeniem planning-poker; formalne role są poza pierwotnym zakresem (`context/foundation/prd.md:169`). |
| Zespół i członkowie zespołu | **Supporting** | Zapewniają wielokrotne użycie składu, przypisania i przyszłe powiadomienia (`context/foundation/prd.md:90-91`). |
| Zadania ad hoc | **Supporting** | Uzupełniają ADO i umożliwiają działanie bez integracji, lecz nie realizują hipotezy round-trip (`context/foundation/prd.md:96-97`). |
| Powiadomienia i historia | **Supporting** | Są jawnie kryteriami drugorzędnymi / nice-to-have (`context/foundation/prd.md:72-75`, `context/foundation/prd.md:117-121`). |
| Uwierzytelnianie, tożsamość, tenant isolation | **Generic** | Są niezbędnymi guardrails, ale nie przewagą planning-poker; Entra jest zachowanym mechanizmem (`context/foundation/prd.md:156-163`). |
| EF Core/SQL, gRPC, SignalR, Aspire | **Generic** | Są technicznymi mechanizmami realizacji i zachowanymi ograniczeniami architektury (`context/foundation/prd.md:156-159`). |
| Lokalizacja, motyw jasny/ciemny | **Generic** | Przekrojowe wymagania UX, niezależne od modelu estymacji (`context/foundation/prd.md:145-154`). |

## 3. Kandydaci na agregaty i niezmienniki

Status:

- **egzekwuje** — kod odrzuca naruszenie lub struktura danych je uniemożliwia;
- **deklaruje** — model ma pole/typ lub dokument aktywnej zmiany, ale reguła nie jest kompletna;
- **ignoruje** — kod pozwala naruszyć regułę albo nie przechowuje wymaganego stanu.

### A. Runda głosowania

**Root kandydata:** `VotingRound` / bieżący stan `PlanningRoom` dla `(TenantId, SessionId, ActiveTaskId)`.

| Niezmiennik | Źródło | Status w kodzie |
| --- | --- | --- |
| Wartości głosów są niewidoczne przed reveal. | `context/foundation/prd.md:141-149` | **Egzekwuje** w projekcji stanu: `Vote` jest zwracany tylko po reveal (`PlanningRoomService.cs:444-464`). |
| Głos dotyczy aktywnego zadania, pochodzi ze skali i nie można go oddać po reveal. | `context/archive/2026-06-22-realtime-voting-round/plan.md:46-49` | **Egzekwuje** (`PlanningRoomService.cs:221-249`). |
| Powtórny głos tej samej osoby zastępuje poprzedni bez duplikatu. | `context/archive/2026-06-22-realtime-vote-integrity/plan.md:33-35` | **Egzekwuje** przez słownik `participantId -> vote` (`PlanningRoomService.cs:244-249`, `PlanningRoomService.cs:514-516`). |
| Stan aktywnej rundy przeżywa reconnect, cleanup, restart i deployment. | `context/changes/persist-voting-round-state/change.md:10-12` | **Ignoruje** dla restartu/deploymentu: pokoje są wyłącznie w `ConcurrentDictionary` (`PlanningRoomService.cs:7-10`), a seed z DB zawiera tylko zadania i skalę (`VotingRoundService.cs:41-62`). |
| Estymatę wolno wybrać dopiero po reveal i tylko dla aktywnego zadania. | `context/changes/enforce-voting-round-transitions/change.md:10-12` | **Ignoruje**: hub sprawdza skalę, ale nie `IsRevealed` ani zgodność z `CurrentTaskId`; zapisuje dowolne zadanie sesji (`PlanningRoomHub.cs:108-130`, `SessionRepository.cs:65-76`). |
| Jednoczesne wybory estymaty mają deterministyczny rezultat. | `context/changes/enforce-voting-round-transitions/change.md:10-12` | **Deklaruje częściowo**: procesowy `SemaphoreSlim` serializuje wywołania w jednej instancji (`PlanningRoomHub.cs:17`, `PlanningRoomHub.cs:113-131`), ale brak trwałej wersji rundy/warunku konkurencji. |

**Ocena:** to semantyczny agregat rdzeniowy, ale nie jest trwałym agregatem domenowym. Obecnie jest obiektem koordynującym w pamięci.

### B. Sesja planistyczna

**Root kandydata:** `PlanningSession`; encje wewnętrzne: `SessionTask`, logicznie także przypisani `SessionMember`.

| Niezmiennik | Źródło | Status w kodzie |
| --- | --- | --- |
| Sesja należy do dokładnie jednego projektu. | `context/changes/reorganize-project-and-sessions/plan.md:80-92` | **Egzekwuje** przez wymagany `ProjectId`, autoryzację i FK (`SessionGrpcService.cs:32-50`, `PlanningSessionConfiguration.cs:42-46`). |
| Sesja rozpoczyna się jako Draft i po aktywacji ma stabilny, globalnie unikalny kod gościa. | `context/archive/2026-06-24-guest-link-voting/plan.md:66-96` | **Egzekwuje** (`SessionGrpcService.cs:417-435`, `PlanningSessionConfiguration.cs:36-40`). |
| Konfigurację nazwy i skali można zmieniać tylko w Draft. | Minimalna konfiguracja: `context/foundation/prd.md:99-105`; lifecycle doprecyzowany w kodzie | **Egzekwuje** przez `LoadDraftAsync` (`SessionGrpcService.cs:181-207`). |
| Sesja prowadzi pełny cykl aż do wyniku i historii. | `context/foundation/prd.md:69-75` | **Deklaruje częściowo**: wynik zadania jest trwały, ale brak stanu `Closed/Completed` i modelu historii (`SessionStatus.cs:3-7`). |

### C. Zadanie sesji / wynik estymacji

**Root kandydata:** jako encja wewnątrz `PlanningSession`; ze względu na niezależne write-back ma silną własną tożsamość i wersję źródła.

| Niezmiennik | Źródło | Status w kodzie |
| --- | --- | --- |
| Na zadanie przypada jedna uzgodniona estymata. | `context/foundation/prd.md:141-143` | **Egzekwuje strukturalnie** przez pojedyncze `AgreedEstimate` (`SessionTask.cs:23`). |
| Ten sam work item ADO nie występuje dwa razy w jednej sesji. | Ochrona właściwego work itemu: `context/foundation/prd.md:156-160` | **Egzekwuje** przez deduplikację serwisu i unikalny indeks (`SessionGrpcService.cs:56-59`, `SessionTaskConfiguration.cs:33-35`). |
| Write-back dotyczy wyłącznie źródłowego zadania ADO, numerycznej estymaty i zapisanej rewizji. | `context/archive/2026-06-24-ado-estimate-writeback/plan-brief.md:22-38` | **Egzekwuje** (`SessionGrpcService.cs:349-365`, `SessionGrpcService.cs:386-414`). |
| Agreed estimate jest wynikiem poprawnie zakończonej rundy. | `context/foundation/prd.md:139-143` | **Ignoruje**: repozytorium nie zna stanu rundy i przyjmuje dowolny string/null (`SessionRepository.cs:65-76`). |

### D. Projekt

**Root kandydata:** `PlanDeckProject`; encje zależne: `ProjectMember`, `ProjectTeam`, `ProjectAzureDevOpsConnection`, `PlanningSession`.

| Niezmiennik | Źródło | Status w kodzie |
| --- | --- | --- |
| Odczyt sesji wymaga Member, mutacje wymagają Admin. | `context/changes/reorganize-project-and-sessions/plan.md:94-114` | **Egzekwuje** w `SessionGrpcService` (`SessionGrpcService.cs:117-133`, `SessionGrpcService.cs:181-217`). |
| Projekt ma najwyżej jednego zaakceptowanego Ownera. | Role projektu: `context/changes/reorganize-project-and-sessions/plan.md:94-105` | **Egzekwuje** filtrowanym unikalnym indeksem (`ProjectMemberConfiguration.cs:32-40`). |
| Usunięcie projektu usuwa jego sesje, zachowując współdzielone zespoły. | `context/changes/reorganize-project-and-sessions/plan.md:144-149` | **Deklaruje/egzekwuje relacyjnie** dla sesji przez cascade (`PlanningSessionConfiguration.cs:42-46`); pełny zewnętrzny lifecycle Key Vault/realtime wykracza poza ten agregat SQL. |
| Po użyciu ADO w sesji nie można zmienić celu organization/project. | Bezpieczny właściwy cel write-back: `context/foundation/prd.md:156-160` | **Egzekwuje** (`ProjectAzureDevOpsConnection.cs:29-51`, `ProjectAzureDevOpsConnectionRepository.cs:43-55`). |

### E. Dostęp gościa do sesji

**Root kandydata:** `GuestAccessGrant` jako pojęcie domenowe, dziś reprezentowane tylko przez `PlanningSession.ShareCode` i claims cookie.

| Niezmiennik | Źródło | Status w kodzie |
| --- | --- | --- |
| Kod ustala tenant z sesji, nie z danych klienta. | `context/archive/2026-06-24-guest-link-voting/plan.md:50-54` | **Egzekwuje** przez lookup omijający filtr tylko do odczytu (`SessionRepository.cs:93-105`). |
| Credential gościa pozwala działać tylko w jednej aktywnej sesji. | `context/archive/2026-06-24-guest-link-voting/plan.md:52-56` | **Egzekwuje** w zakresie `sid` i ponownej kontroli Active (`PlanningRoomHub.cs:145-175`). |
| Gość może głosować, ale nie reveal/reset/nawigować/wybierać estymaty. | `context/archive/2026-06-24-guest-link-voting/plan.md:54-56` | **Egzekwuje** przez `EnsureNotGuest` na akcjach moderatorskich (`PlanningRoomHub.cs:69-110`). |
| Dostęp można wygasić, obrócić i odwołać. | Aktywna zmiana `context/changes/expire-revoke-guest-access/change.md:10-12` | **BRAK / ignoruje** w obecnym modelu: `ShareCode` nie ma daty ważności ani stanu odwołania (`PlanningSession.cs:11-19`). |

### F. Tenant

**Root kandydata:** `PlanDeckTenant`; wszystkie `ITenantScoped` są objęte granicą izolacji, niekoniecznie jednym agregatem transakcyjnym.

| Niezmiennik | Źródło | Status w kodzie |
| --- | --- | --- |
| Odczyt bez tenant context zwraca zero, a zapis bez tenantu lub do innego tenantu jest zabroniony. | `context/archive/2026-06-18-multitenant-persistence-baseline/plan.md:25-33`, `context/archive/2026-06-18-multitenant-persistence-baseline/plan.md:47-51` | **Egzekwuje centralnie** (`PlanDeckDbContext.cs:63-72`, `PlanDeckDbContext.cs:151-192`). |
| `TenantId` istniejącego bytu jest niezmienny. | Fail-closed cross-tenant writes, j.w. | **Egzekwuje** (`PlanDeckDbContext.cs:172-192`). |

## 4. Rozjazdy MODEL vs KOD

| Dokument mówi X | Kod robi Y | Dowód |
| --- | --- | --- |
| Aktywna runda ma przetrwać reconnect, cleanup, restart i deployment. | Reconnect w tej samej instancji jest obsłużony, ale cały stan rundy jest tylko w pamięci i może zostać usunięty jako nieaktywny. | Model: `context/changes/persist-voting-round-state/change.md:10-12`. Kod: `PlanningRoomService.cs:7-10`, `PlanningRoomService.cs:370-397`, `VotingRoundService.cs:41-62`. |
| Estymatę można wybrać dopiero po reveal i dla aktywnego zadania. | `SelectEstimate` sprawdza jedynie, czy wartość należy do skali; repozytorium zapisuje wskazane zadanie bez kontroli reveal/active task. | Model: `context/changes/enforce-voting-round-transitions/change.md:10-12`. Kod: `PlanningRoomHub.cs:108-130`, `SessionRepository.cs:65-76`. |
| Równoczesne wybory estymaty mają deterministyczną semantykę. | Blokada jest lokalna dla procesu; brak trwałej rewizji rundy, optimistic concurrency lub jawnej zasady first/last writer. | Model: `context/changes/enforce-voting-round-transitions/change.md:12`. Kod: `PlanningRoomHub.cs:17`, `PlanningRoomHub.cs:113-131`. |
| Guardrail mówi „no lost ... votes”, a aktywna decyzja wymaga trwałości. | Restart/deployment usuwa słownik pokoi i głosy; baza przechowuje tylko finalne `AgreedEstimate`. | Model: `context/foundation/prd.md:76-79`, `context/changes/persist-voting-round-state/change.md:12`. Kod: `PlanningRoomService.cs:7-10`, `SessionTask.cs:23`, `PlanDeckDbContext.cs:36-40`. |
| PRD zachowuje płaski model: każdy uwierzytelniony użytkownik może tworzyć i konfigurować sesje. | Aktualny kod ma projektowe role Member/Admin/Owner i wymaga Admin do tworzenia oraz mutacji sesji. | Model: `context/foundation/prd.md:60-65`, `context/foundation/prd.md:169`. Kod: `ProjectRole.cs:3-8`, `SessionGrpcService.cs:32-39`, `SessionGrpcService.cs:181-217`. Jest to świadoma ewolucja opisana w `context/changes/reorganize-project-and-sessions/plan.md:94-114`, ale PRD nie został zaktualizowany. |
| Zespoły utrzymują członkostwo między sesjami i napędzają assignment/notifications. | `SessionMember` przechowuje niezależny e-mail i nie wskazuje `TeamMember`, `Team` ani `AppUser`; przypisanie jest kopią tekstową. | Model: `context/foundation/prd.md:90-91`, `context/foundation/prd.md:104-105`. Kod: `SessionMember.cs:3-12`, `SessionMemberConfiguration.cs:17-31`. |
| Użytkownik może przeglądać przeszłe sesje i wyniki. | Brak stanu zakończonej sesji i odrębnego modelu historii; istnieją tylko `Draft` i `Active`. | Model: `context/foundation/prd.md:72-75`, `context/foundation/prd.md:120-121`. Kod: `SessionStatus.cs:3-7`. |
| Przypisany użytkownik jest powiadamiany o starcie sesji. | W modelu domenowym/persystencji nie ma powiadomienia ani zlecenia wysyłki powiązanego ze startem. | Model: `context/foundation/prd.md:72-74`, `context/foundation/prd.md:117-120`. Kod rejestruje tylko zestawy z `PlanDeckDbContext.cs:17-40`; aktywacja kończy się zapisem sesji (`SessionGrpcService.cs:417-435`). |
| Wynik jest wybierany po ujawnieniu i dyskusji. | `AgreedEstimate` jest zwykłym, publicznie ustawialnym stringiem; warstwa persystencji nie potrafi udowodnić pochodzenia z rundy. | Model: `context/foundation/prd.md:139-143`. Kod: `SessionTask.cs:23`, `SessionRepository.cs:65-76`. |
| Jedynym pierwotnym statusem lifecycle jest Draft → Active, lecz produkt mówi o historii/past results. | Kod nie ma `Completed`, `Closed` ani `Archived`; nie da się formalnie odróżnić trwającej aktywnej sesji od historycznej. | Model: `context/foundation/prd.md:74`, `context/foundation/prd.md:120-121`. Kod: `SessionStatus.cs:3-7`. |

## 5. Ranking refaktoru agregatów

Skala wartości: znaczenie niezmiennika dla przewagi produktu. Skala ryzyka: możliwość naruszenia reguły w obecnym kodzie lub utraty danych.

| Ranking | Kandydat | Wartość | Ryzyko | Powód |
| ---: | --- | --- | --- | --- |
| **1** | **Runda głosowania** | Bardzo wysoka | Bardzo wysokie | Jest centrum produktu, ale nie ma trwałej reprezentacji; traci stan po restarcie i nie wymusza reveal-before-pick ani deterministycznej konkurencji. |
| **2** | **Zadanie sesji / decyzja estymacyjna** | Bardzo wysoka | Wysokie | Łączy wynik głosowania z write-back ADO. Sam write-back jest mocno chroniony, lecz kod nie dowodzi, że wynik pochodzi z poprawnej rundy. |
| **3** | **Sesja planistyczna** | Wysoka | Średnie | Dobrze utrzymuje projekt, konfigurację i aktywację, ale nie modeluje zakończenia ani historii i deleguje krytyczny lifecycle do pamięciowego pokoju. |
| **4** | **Dostęp gościa** | Wysoka | Średnie | Jest wyróżnikiem i ma dobrą izolację sesji, lecz brak wygaśnięcia, rotacji i odwołania pozostawia długowieczny bearer credential. |
| **5** | **Projekt** | Średnia | Niskie/średnie | Autoryzacja i więzy DB są silne; największym problemem jest rozjazd aktualnego modelu ról z PRD, nie brak egzekucji. |
| **6** | **Zespół** | Średnia | Średnie | Istnieje jako osobny model, ale nie jest związany referencyjnie z uczestnictwem w sesji, więc nie realizuje w pełni obietnicy ponownego użycia składu. |
| **7** | **Tenant** | Wysoka jako guardrail | Niskie | Nie jest przewagą produktu, ale izolacja jest centralna i konsekwentnie egzekwowana przez EF. |

### Priorytet #1

Pierwszym refaktorem powinna być **Runda głosowania jako jawny, trwały agregat z wersjonowanym automatem stanów**. To jedyny kandydat, w którym jednocześnie:

1. skupia się rdzeniowa przewaga produktu — hidden vote → reveal → manual pick;
2. aktywne dokumenty już wskazują konkretne brakujące niezmienniki (`persist-voting-round-state` i `enforce-voting-round-transitions`);
3. obecna implementacja może utracić głosy przy restarcie;
4. obecna implementacja pozwala zapisać estymatę przed reveal albo dla nieaktywnego zadania;
5. blokada konkurencji działa tylko w obrębie jednej instancji procesu.

Granica refaktoru powinna objąć co najmniej: `SessionId`, aktywne `TaskId`, stan rundy, głosy per participant, flagę reveal, uzgodnioną estymatę i rewizję. `PlanningRoomService` może pozostać projekcją czasu rzeczywistego, lecz źródłem prawdy dla przejść i decyzji powinien być trwały agregat aplikacyjno-domenowy.

## Wniosek

Najdojrzalsze części modelu to izolacja tenantów, projektowa autoryzacja i bezpieczny write-back ADO. Największa luka występuje dokładnie w rdzeniu produktu: runda głosowania jest mechanizmem pamięciowym, a nie trwałym agregatem pilnującym przejść. Dokumentacja aktywnych zmian poprawnie identyfikuje tę lukę, lecz PRD dodatkowo wymaga aktualizacji w obszarze płaskich ról, ponieważ kod przeszedł już na role projektowe. Zespół i członkostwo sesji pozostają dwoma luźno powiązanymi modelami, przez co deklarowane ponowne użycie składu nie jest formalnym niezmiennikiem. Powiadomienia i historia są nadal tylko wymaganiami drugorzędnymi, bez reprezentacji domenowej. Refaktor rundy powinien poprzedzić dalsze rozszerzanie UI lub integracji, ponieważ dopiero on zabezpieczy znaczenie wyniku, który następnie trafia do Azure DevOps.
