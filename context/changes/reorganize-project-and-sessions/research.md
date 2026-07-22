---
date: 2026-07-22T14:42:06.185+02:00
researcher: GitHub Copilot
git_commit: 93ae40194703235be4353204549167e6cb3dfb0c
branch: main
repository: LeszekDNV/PlanDeck2
topic: "Reorganize projects and project-owned sessions"
tags: [research, codebase, projects, sessions, authorization, azure-devops, blazor]
status: complete
last_updated: 2026-07-22
last_updated_by: GitHub Copilot
---

# Research: Reorganize projects and project-owned sessions

**Date**: 2026-07-22T14:42:06.185+02:00  
**Researcher**: GitHub Copilot  
**Git Commit**: 93ae40194703235be4353204549167e6cb3dfb0c  
**Branch**: main  
**Repository**: LeszekDNV/PlanDeck2

## Research Question

Jak przebudować aplikację, aby Projekt był nadrzędnym agregatem konfiguracji Azure DevOps, członkostwa i Sesji, Sesje nie mogły istnieć ani być tworzone poza Projektem, a właściciel mógł delegować prawa do modyfikacji Projektu i jego Sesji?

Zakres obejmuje stan obecny, docelową architekturę, migrację danych, backend, autoryzację, UI, testy, ryzyka i kolejność wdrożenia.

## Summary

Znaczna część fundamentu jest już gotowa:

- `PlanningSession.ProjectId` jest wymaganym FK do Projektu, ma indeks pod listowanie projektowe, a tworzenie Sesji odrzuca pusty `ProjectId`.
- Twórca Projektu automatycznie otrzymuje rolę `Owner`.
- Konfiguracja ADO jest przechowywana per Projekt; import i write-back Sesji rozwiązują ją przez `session.ProjectId`.
- Dostęp do większości operacji Sesji jest już wyprowadzany przez `SessionAccessResolver` do roli użytkownika w Projekcie.

Zmiana nie wymaga ponownego dodawania relacji w bazie. Najważniejsze luki to:

1. `ListSessionsRequest`, repozytorium i klient listują Sesje bez wymaganego kontekstu Projektu, powodując płaski widok cross-project i N+1 zapytań autoryzacyjnych.
2. `SessionMemberGrpcService` nie sprawdza dostępu do Projektu/Sesji, co tworzy wewnątrztenantową lukę IDOR.
3. Mutacje Sesji wymagają obecnie dowolnej roli projektowej. Istniejące `ProjectRole.Admin` powinno pełnić rolę edytora, zamiast tworzenia nowego modelu uprawnień.
4. UI eksponuje `/sessions` jako równorzędny moduł i każe wybierać Projekt dopiero w formularzu Sesji.
5. Brakuje testów kontroli dostępu do Sesji i członków Sesji oraz testów listowania per Projekt.

Rekomendowany kierunek: uczynić `ProjectId` obowiązkowym dla listowania Sesji, egzekwować `Admin` dla mutacji, zachować `Member` do odczytu i głosowania, przenieść zarządzanie Sesjami pod trasę Projektu oraz zachować niezależne trasy pokoju głosowania i guest join.

## Detailed Findings

### 1. Model i persystencja są już projektowe

`PlanningSession` ma wymagany `ProjectId` ([PlanningSession.cs:3-20](https://github.com/LeszekDNV/PlanDeck2/blob/93ae40194703235be4353204549167e6cb3dfb0c/src/PlanDeck/Core/PlanDeck.Application/Domain/PlanningSession.cs#L3-L20)). EF definiuje indeks `(TenantId, ProjectId, CreatedAtUtc)` oraz wymagany złożony FK do Projektu z `DeleteBehavior.Restrict` ([PlanningSessionConfiguration.cs:32-46](https://github.com/LeszekDNV/PlanDeck2/blob/93ae40194703235be4353204549167e6cb3dfb0c/src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/Configurations/PlanningSessionConfiguration.cs#L32-L46)).

Poprzednia migracja wyczyściła stare Sesje przed dodaniem niezerowego `ProjectId`, więc w aktualnym schemacie nie ma legacy rows bez Projektu ([AddProjectAccessControl migration](https://github.com/LeszekDNV/PlanDeck2/blob/93ae40194703235be4353204549167e6cb3dfb0c/src/PlanDeck/Core/PlanDeck.Infrastructure/Migrations/20260721174206_AddProjectAccessControl.cs#L62-L86)). Dla tej zmiany nie jest potrzebny backfill `ProjectId`.

Nie ma potrzeby dodawania nawigacji `PlanDeckProject.Sessions` tylko po to, aby wyrazić agregację. Obecny model FK i projektowo filtrowane repozytorium wystarczą; nawigację warto dodać dopiero, jeśli konkretny use case będzie ładował cały graf.

### 2. Tworzenie Sesji jest już zależne od Projektu

`CreateSessionAsync`:

- odrzuca `Guid.Empty`,
- wymaga dostępu członka do Projektu,
- zapisuje `ProjectId`,
- dla importu ADO rozwiązuje połączenie z Projektu.

Przepływ znajduje się w [SessionGrpcService.cs:28-95](https://github.com/LeszekDNV/PlanDeck2/blob/93ae40194703235be4353204549167e6cb3dfb0c/src/PlanDeck/Core/PlanDeck.Application/Services/SessionGrpcService.cs#L28-L95). `CreateSessionRequest.ProjectId` i `SessionDto.ProjectId` są już częścią kontraktu ([ISessionService.cs:70-98](https://github.com/LeszekDNV/PlanDeck2/blob/93ae40194703235be4353204549167e6cb3dfb0c/src/PlanDeck/Core/PlanDeck.Core.Shared/Contracts/ISessionService.cs#L70-L98), [ISessionService.cs:177-202](https://github.com/LeszekDNV/PlanDeck2/blob/93ae40194703235be4353204549167e6cb3dfb0c/src/PlanDeck/Core/PlanDeck.Core.Shared/Contracts/ISessionService.cs#L177-L202)).

Brakująca zależność dotyczy głównie listowania i UX, nie samego zapisu.

### 3. Listowanie nadal traktuje Sesje jako zasób globalny

`ListSessionsRequest` jest pusty ([ISessionService.cs:203-205](https://github.com/LeszekDNV/PlanDeck2/blob/93ae40194703235be4353204549167e6cb3dfb0c/src/PlanDeck/Core/PlanDeck.Core.Shared/Contracts/ISessionService.cs#L203-L205)), `ISessionRepository.GetSessionsAsync` nie przyjmuje Projektu ([ISessionRepository.cs:6-24](https://github.com/LeszekDNV/PlanDeck2/blob/93ae40194703235be4353204549167e6cb3dfb0c/src/PlanDeck/Core/PlanDeck.Application/Abstractions/ISessionRepository.cs#L6-L24)), a implementacja pobiera wszystkie Sesje tenanta ([SessionRepository.cs:18-25](https://github.com/LeszekDNV/PlanDeck2/blob/93ae40194703235be4353204549167e6cb3dfb0c/src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/SessionRepository.cs#L18-L25)).

Następnie `SessionGrpcService.ListSessionsAsync` sprawdza dostęp osobno dla każdej Sesji ([SessionGrpcService.cs:117-132](https://github.com/LeszekDNV/PlanDeck2/blob/93ae40194703235be4353204549167e6cb3dfb0c/src/PlanDeck/Core/PlanDeck.Application/Services/SessionGrpcService.cs#L117-L132)). Powoduje to:

- brak jawnej granicy Projektu w API,
- możliwość projektowania klienta jako globalnej listy,
- N+1 zapytań przez `SessionAccessResolver` i `ProjectAccessResolver`.

Docelowo `ListSessionsRequest.ProjectId` powinien być wymaganym `Guid` z nowym numerem pola protobuf. Dodanie pola jest zgodne na poziomie wire format, ale odrzucenie pustej wartości jest świadomą zmianą zachowania. Klient i serwer są jednym wdrażanym artefaktem, więc mogą zostać zmienione atomowo.

Po jednorazowym `RequireRoleAsync(projectId, Member)` repozytorium powinno wykonać pojedyncze zapytanie `WHERE ProjectId = ...`; istniejący indeks już je wspiera.

### 4. Konfiguracja ADO jest poprawnie dziedziczona z Projektu

`ProjectAzureDevOpsConnection` przechowuje organizację, projekt ADO, mapowanie pól, sekret PAT, stan walidacji i blokadę celu ([ProjectAzureDevOpsConnection.cs:3-69](https://github.com/LeszekDNV/PlanDeck2/blob/93ae40194703235be4353204549167e6cb3dfb0c/src/PlanDeck/Core/PlanDeck.Application/Domain/ProjectAzureDevOpsConnection.cs#L3-L69)).

Istniejące ścieżki są spójne:

- import wyszukujący work items wymaga `request.ProjectId` i sprawdza dostęp ([AzureDevOpsWorkItemGrpcService.cs:16-76](https://github.com/LeszekDNV/PlanDeck2/blob/93ae40194703235be4353204549167e6cb3dfb0c/src/PlanDeck/Core/PlanDeck.Application/Services/AzureDevOpsWorkItemGrpcService.cs#L16-L76)),
- dodanie ADO tasks do istniejącej Sesji bierze Projekt z `session.ProjectId`,
- write-back estymaty rozwiązuje połączenie przez `session.ProjectId` ([SessionGrpcService.cs:308-389](https://github.com/LeszekDNV/PlanDeck2/blob/93ae40194703235be4353204549167e6cb3dfb0c/src/PlanDeck/Core/PlanDeck.Application/Services/SessionGrpcService.cs#L308-L389)),
- cel ADO jest blokowany po pierwszym użyciu.

Nie należy duplikować danych ADO w Sesji ani dodawać `ProjectId` do komend, które już zaczynają się od `SessionId`; serwer powinien rozwiązywać Projekt z zapisanej Sesji, aby klient nie mógł podać niespójnej pary identyfikatorów.

### 5. Własność i role Projektu są gotowe do ponownego użycia

Istnieje hierarchia `Member < Admin < Owner` ([ProjectRole.cs:1-8](https://github.com/LeszekDNV/PlanDeck2/blob/93ae40194703235be4353204549167e6cb3dfb0c/src/PlanDeck/Core/PlanDeck.Application/Domain/ProjectRole.cs#L1-L8)). `ProjectRepository.CreateAsync` zapisuje twórcę jako zaakceptowanego `Owner` w tej samej operacji ([ProjectRepository.cs:20-40](https://github.com/LeszekDNV/PlanDeck2/blob/93ae40194703235be4353204549167e6cb3dfb0c/src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/ProjectRepository.cs#L20-L40)).

`ProjectMember` obsługuje zaproszenia po e-mailu i późniejsze powiązanie z `AppUser`; konfiguracja bazy pilnuje aktywnego członkostwa i pojedynczego Ownera ([ProjectMember.cs:3-20](https://github.com/LeszekDNV/PlanDeck2/blob/93ae40194703235be4353204549167e6cb3dfb0c/src/PlanDeck/Core/PlanDeck.Application/Domain/ProjectMember.cs#L3-L20), [ProjectMemberConfiguration.cs:22-40](https://github.com/LeszekDNV/PlanDeck2/blob/93ae40194703235be4353204549167e6cb3dfb0c/src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/Configurations/ProjectMemberConfiguration.cs#L22-L40)).

Rekomendowane znaczenie ról:

| Rola | Uprawnienia |
|---|---|
| `Owner` | Pełna kontrola, ADO credentials, transfer własności, usunięcie Projektu |
| `Admin` | Edytor: tworzenie i modyfikacja Sesji, tasks, konfiguracja głosowania, zarządzanie uczestnikami; obecne operacje administracyjne Projektu |
| `Member` | Odczyt Projektu i Sesji, udział w głosowaniu |
| Guest | Wyłącznie jedna Sesja wskazana przez guest claim/share link |

Nie należy tworzyć dodatkowej roli `Editor`; `Admin` już realizuje ten przypadek.

### 6. Kontrola mutacji Sesji jest zbyt szeroka

`SessionAccessResolver` poprawnie rozwiązuje `sessionId -> projectId -> effective project role` ([SessionAccessResolver.cs:11-28](https://github.com/LeszekDNV/PlanDeck2/blob/93ae40194703235be4353204549167e6cb3dfb0c/src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/SessionAccessResolver.cs#L11-L28)). Jednak helper w `SessionGrpcService` sprawdza tylko obecność dowolnej roli ([SessionGrpcService.cs:530-536](https://github.com/LeszekDNV/PlanDeck2/blob/93ae40194703235be4353204549167e6cb3dfb0c/src/PlanDeck/Core/PlanDeck.Application/Services/SessionGrpcService.cs#L530-L536)).

W efekcie `Member`, także odziedziczony przez zespół, może obecnie aktualizować konfigurację, tasks, aktywować i usuwać Sesje. Należy wprowadzić wspólną operację wymagającą minimalnej roli dla Sesji i użyć `Admin` dla wszystkich mutacji. Odczyt pozostaje na poziomie `Member`.

To jest zmiana semantyczna: obecni bezpośredni i zespołowi `Member` utracą możliwość edycji. Schemat danych nie wymaga migracji, ale przed wdrożeniem trzeba zaakceptować mapowanie ról.

### 7. `SessionMemberGrpcService` ma lukę IDOR

Operacje przypisywania, usuwania i listowania uczestników odrzucają gości, lecz nie sprawdzają dostępu wywołującego do Sesji lub Projektu ([SessionMemberGrpcService.cs:10-68](https://github.com/LeszekDNV/PlanDeck2/blob/93ae40194703235be4353204549167e6cb3dfb0c/src/PlanDeck/Core/PlanDeck.Application/Services/SessionMemberGrpcService.cs#L10-L68)).

Globalny filtr tenanta nie wystarcza: dowolny uwierzytelniony użytkownik tego samego tenanta może próbować zarządzać uczestnikami cudzej Sesji. Serwis powinien używać tego samego resolvera:

- `Member` do listowania uczestników,
- `Admin` do przypisywania i usuwania uczestników,
- `NotFound` przy braku dostępu, aby nie ujawniać istnienia zasobu.

To jest najpilniejsza poprawka bezpieczeństwa powiązana z zakresem zmiany.

### 8. UI nadal promuje Sesję nad Projektem

Obecna nawigacja eksponuje Projekty, Zespoły i Sesje jako równorzędne przyciski ([MainLayout.razor:20-27](https://github.com/LeszekDNV/PlanDeck2/blob/93ae40194703235be4353204549167e6cb3dfb0c/src/PlanDeck/Web/PlanDeck.Client/Layout/MainLayout.razor#L20-L27)). `/sessions` ładuje wszystkie dostępne Sesje i osobno wszystkie Projekty, a formularz tworzenia wymaga wyboru Projektu z dropdownu ([Sessions.razor.cs:67-120](https://github.com/LeszekDNV/PlanDeck2/blob/93ae40194703235be4353204549167e6cb3dfb0c/src/PlanDeck/Web/PlanDeck.Client/Pages/Sessions.razor.cs#L67-L120), [Sessions.razor:339-346](https://github.com/LeszekDNV/PlanDeck2/blob/93ae40194703235be4353204549167e6cb3dfb0c/src/PlanDeck/Web/PlanDeck.Client/Pages/Sessions.razor#L339-L346)).

Docelowa hierarchia:

```text
/                         -> redirect /projects
/projects                 -> lista/karty Projektów + utworzenie Projektu
/projects/{projectId}     -> dashboard/szczegóły Projektu
/projects/{projectId}/sessions
                          -> lista i zarządzanie Sesjami Projektu
/voting/{sessionId}       -> bez zmian
/join/{code}              -> bez zmian
/teams                    -> bez zmian
```

Najbezpieczniej rozdzielić obecną dużą stronę `Projects` na listę i szczegóły, a istniejącą stronę `Sessions` osadzić w wymaganym kontekście trasy. Formularz tworzenia nie powinien zawierać wyboru Projektu; `ProjectId` pochodzi z trasy. Nawigacja główna nie powinna zawierać pozycji Sesje.

Dashboard Projektu może używać zakładek/sekcji `Sessions`, `ADO Connection`, `Members`, `Teams`, ale zarządzanie Sesjami powinno mieć własny adres do deep-linkowania i stabilnej nawigacji.

### 9. Przepływy, które powinny pozostać sesyjne

Nie każda operacja powinna wymagać `projectId` w URL lub żądaniu:

- `/join/{code}` rozwiązuje aktywną Sesję po share code,
- guest claim `sid` ogranicza gościa do jednej Sesji,
- `/voting/{sessionId}` i klucz pokoju SignalR pozostają sesyjne,
- komendy ADO zaczynające się od `SessionId` rozwiązują Projekt po stronie serwera.

Zmiana dotyczy zarządzania Sesją przez użytkowników aplikacji, nie publicznego/guest flow głosowania. Przycisk powrotu z Voting Room powinien kierować do Projektu Sesji zamiast globalnego `/sessions`.

### 10. Usuwanie Projektu

Obecnie FK `Restrict` i `ProjectRepository.EnsureCanDeleteAsync` blokują usunięcie Projektu posiadającego Sesje ([ProjectRepository.cs:242-259](https://github.com/LeszekDNV/PlanDeck2/blob/93ae40194703235be4353204549167e6cb3dfb0c/src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/ProjectRepository.cs#L242-L259)).

Nie rekomenduje się automatycznego przejścia na cascade delete bez osobnej decyzji produktowej. Sesje zawierają wyniki planowania i integracje ADO, więc bezpiecznym domyślnym zachowaniem jest `Restrict` z jednoznacznym komunikatem domenowym. Cascade lub archiwizacja Projektu wymagają osobnego scenariusza, potwierdzenia i testów.

## Recommended Implementation Sequence

### Phase 1: Zamknięcie granicy backendowej

1. Dodać wymagany `ProjectId` do `ListSessionsRequest`.
2. Zmienić repozytorium na listowanie po `projectId`.
3. W `ListSessionsAsync` wykonać jeden check roli Projektu, a następnie jedno filtrowane zapytanie.
4. Dodać wspólny helper `RequireSessionRoleAsync(sessionId, minimumRole)`.
5. Wymagać `Admin` dla tworzenia i mutacji Sesji, `Member` dla odczytu.
6. Zabezpieczyć `SessionMemberGrpcService` przez `ISessionAccessResolver`.

### Phase 2: Testy backendu i persystencji

1. Unit: wymagany `ProjectId`, filtrowanie, `Member` read-only, `Admin` edit.
2. Unit: wszystkie operacje `SessionMemberGrpcService` odrzucają użytkownika spoza Projektu.
3. Integration: Projekt A nie widzi Sesji Projektu B.
4. Integration: pojedynczy query path listowania korzysta z istniejącego indeksu.
5. Integration: team-inherited `Member` nie uzyskuje praw edycji.

### Phase 3: Project-first UI

1. `/` przekierować do `/projects`.
2. Rozdzielić listę i szczegóły Projektu.
3. Przenieść Sesje pod `/projects/{projectId}/sessions`.
4. Usunąć wybór Projektu z formularza Sesji.
5. Usunąć top-level `Nav_Sessions`.
6. Przekazywać route `ProjectId` do listowania, tworzenia i importu ADO.
7. Dodać stany empty/loading/not-found/access-denied i klucze EN/PL.
8. Zmienić powrót z Voting Room na Projekt Sesji.

### Phase 4: E2E i regresja

1. Zmienić `SessionsPage.GotoAsync()` na przepływ przez Projekt.
2. Dodać page object szczegółów Projektu.
3. Zaktualizować testy tworzenia Sesji: Projekt jest tworzony/otwierany przed Sesją.
4. Dodać scenariusz multi-user Owner/Admin/Member.
5. Zachować niezależne testy guest join i voting route.

## Code References

- [`PlanningSession.cs:3-20`](https://github.com/LeszekDNV/PlanDeck2/blob/93ae40194703235be4353204549167e6cb3dfb0c/src/PlanDeck/Core/PlanDeck.Application/Domain/PlanningSession.cs#L3-L20) - wymagany `ProjectId`.
- [`PlanningSessionConfiguration.cs:32-46`](https://github.com/LeszekDNV/PlanDeck2/blob/93ae40194703235be4353204549167e6cb3dfb0c/src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/Configurations/PlanningSessionConfiguration.cs#L32-L46) - indeks i FK Projektu.
- [`ProjectRepository.cs:20-40`](https://github.com/LeszekDNV/PlanDeck2/blob/93ae40194703235be4353204549167e6cb3dfb0c/src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/ProjectRepository.cs#L20-L40) - twórca staje się Ownerem.
- [`SessionGrpcService.cs:28-95`](https://github.com/LeszekDNV/PlanDeck2/blob/93ae40194703235be4353204549167e6cb3dfb0c/src/PlanDeck/Core/PlanDeck.Application/Services/SessionGrpcService.cs#L28-L95) - tworzenie Sesji w Projekcie.
- [`SessionGrpcService.cs:117-132`](https://github.com/LeszekDNV/PlanDeck2/blob/93ae40194703235be4353204549167e6cb3dfb0c/src/PlanDeck/Core/PlanDeck.Application/Services/SessionGrpcService.cs#L117-L132) - globalne listowanie i N+1.
- [`SessionAccessResolver.cs:11-28`](https://github.com/LeszekDNV/PlanDeck2/blob/93ae40194703235be4353204549167e6cb3dfb0c/src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/SessionAccessResolver.cs#L11-L28) - dostęp Sesji przez Projekt.
- [`SessionMemberGrpcService.cs:10-68`](https://github.com/LeszekDNV/PlanDeck2/blob/93ae40194703235be4353204549167e6cb3dfb0c/src/PlanDeck/Core/PlanDeck.Application/Services/SessionMemberGrpcService.cs#L10-L68) - brak resource access check.
- [`Sessions.razor.cs:67-120`](https://github.com/LeszekDNV/PlanDeck2/blob/93ae40194703235be4353204549167e6cb3dfb0c/src/PlanDeck/Web/PlanDeck.Client/Pages/Sessions.razor.cs#L67-L120) - ładowanie cross-project.
- [`SessionClientService.cs:9-14`](https://github.com/LeszekDNV/PlanDeck2/blob/93ae40194703235be4353204549167e6cb3dfb0c/src/PlanDeck/Web/PlanDeck.Client/Services/SessionClientService.cs#L9-L14) - pusty request listowania.
- [`MainLayout.razor:20-27`](https://github.com/LeszekDNV/PlanDeck2/blob/93ae40194703235be4353204549167e6cb3dfb0c/src/PlanDeck/Web/PlanDeck.Client/Layout/MainLayout.razor#L20-L27) - Sesje w top-level navigation.
- [`SessionsPage.cs:46-48`](https://github.com/LeszekDNV/PlanDeck2/blob/93ae40194703235be4353204549167e6cb3dfb0c/src/PlanDeck/Tests/PlanDeck.E2e.Tests/Pages/SessionsPage.cs#L46-L48) - E2E zależne od `/sessions`.

## Architecture Insights

- Projekt jest już faktyczną granicą danych i ADO; teraz musi stać się również granicą API, autoryzacji i nawigacji.
- Tenant isolation i project authorization rozwiązują różne problemy. Filtr EF chroni tenanty, ale nie zastępuje sprawdzania członkostwa w Projekcie.
- `ProjectMember` i `SessionMember` muszą pozostać osobnymi pojęciami: pierwszy nadaje dostęp do zasobu, drugi oznacza uczestnika głosowania.
- `SessionId` pozostaje prawidłową granicą dla voting/guest flows; wymuszanie `ProjectId` wszędzie prowadziłoby do duplikacji i niespójnych identyfikatorów.
- Wymagany `ProjectId` w listowaniu upraszcza zapytania i jednocześnie uniemożliwia powrót do globalnego modelu Sesji.
- Bezpieczne usuwanie danych powinno pozostać restrykcyjne, dopóki produkt jawnie nie zdefiniuje archiwizacji lub cascade delete.

## Historical Context (from prior changes)

- [`secure-ado-grpc-endpoints/plan.md:52-75`](https://github.com/LeszekDNV/PlanDeck2/blob/93ae40194703235be4353204549167e6cb3dfb0c/context/archive/2026-07-21-secure-ado-grpc-endpoints/plan.md#L52-L75) - wprowadzenie `ProjectId` do Sesji oraz świadome rozdzielenie `ProjectMember` i `SessionMember`.
- [`azure-devops-import/research.md`](https://github.com/LeszekDNV/PlanDeck2/blob/93ae40194703235be4353204549167e6cb3dfb0c/context/archive/2026-06-24-azure-devops-import/research.md) - pierwotny przepływ importu ADO.
- [`ado-estimate-writeback/research.md`](https://github.com/LeszekDNV/PlanDeck2/blob/93ae40194703235be4353204549167e6cb3dfb0c/context/archive/2026-06-24-ado-estimate-writeback/research.md) - przepływ zapisu estymat do ADO.
- [`create-configure-session/plan.md`](https://github.com/LeszekDNV/PlanDeck2/blob/93ae40194703235be4353204549167e6cb3dfb0c/context/archive/2026-06-18-create-configure-session/plan.md) - pierwotny, samodzielny model tworzenia Sesji.
- [`assign-session-members/plan.md`](https://github.com/LeszekDNV/PlanDeck2/blob/93ae40194703235be4353204549167e6cb3dfb0c/context/archive/2026-06-22-assign-session-members/plan.md) - wcześniejsze założenia uczestników Sesji.

Nie znaleziono `context/foundation/lessons.md`, więc brak dodatkowych utrwalonych reguł zespołu dla tego tematu.

## Related Research

- [`context/archive/2026-06-24-azure-devops-import/research.md`](https://github.com/LeszekDNV/PlanDeck2/blob/93ae40194703235be4353204549167e6cb3dfb0c/context/archive/2026-06-24-azure-devops-import/research.md)
- [`context/archive/2026-06-24-ado-estimate-writeback/research.md`](https://github.com/LeszekDNV/PlanDeck2/blob/93ae40194703235be4353204549167e6cb3dfb0c/context/archive/2026-06-24-ado-estimate-writeback/research.md)
- [`context/archive/2026-06-27-testing-critical-path-integrity/research.md`](https://github.com/LeszekDNV/PlanDeck2/blob/93ae40194703235be4353204549167e6cb3dfb0c/context/archive/2026-06-27-testing-critical-path-integrity/research.md)

## Open Questions

1. Czy `Admin` ma być jedyną delegowaną rolą edytora, a `Member` rolą read/vote? Jest to rekomendowane mapowanie, ale zmienia obecne zachowanie.
2. Czy zwykły `Member` może tworzyć nowe Sesje, czy tworzenie również wymaga `Admin`? Rekomendacja: `Admin`.
3. Czy Projekt z Sesjami pozostaje nieusuwalny, czy produkt potrzebuje archiwizacji/cascade delete? Rekomendacja MVP: zachować `Restrict`.
4. Czy szczegóły Projektu użyją osobnych tras (`/projects/{id}/sessions`) czy jednej strony z zakładkami? Rekomendacja: osobna trasa Sesji, opcjonalnie prezentowana jako zakładka.
