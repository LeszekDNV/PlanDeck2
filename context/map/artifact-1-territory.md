# Territory map: aktywność i współzmiany

## Zakres i metodologia

- Okno analizy: **2025-08-08 – 2026-08-08**.
- Historia w tym oknie: **160 commitów**, faktycznie z okresu **2026-06-11 – 2026-07-29**.
- Częstotliwość oznacza liczbę commitów modyfikujących element, maksymalnie raz na commit.
- Obszary analizowano na poziomie podmodułów, np. `Client/Pages` i `Infrastructure/Persistence`, zamiast ogólnych `src/PlanDeck/Web` i `src/PlanDeck/Core`.
- Z głównych rankingów wykluczono lockfile'y, snapshoty, pliki generowane, migracje, dotenv, konfigurację projektu, artefakty build/test, dokumentację procesową oraz `context/` i `.github/`.
- Przy szukaniu plików-hubów ponownie uwzględniono configi, lockfile'y, zasoby i pliki generowane, aby wykryć przekrojowe współzmiany.

## Najczęściej modyfikowane obszary

| # | Obszar | Commity |
|---:|---|---:|
| 1 | `Core/PlanDeck.Application/Services` | 31 |
| 2 | `Web/PlanDeck.Server/Extensions` | 31 |
| 3 | `Core/PlanDeck.Application/Abstractions` | 28 |
| 4 | `Web/PlanDeck.Client/Pages` | 28 |
| 5 | `Web/PlanDeck.Server` (pliki główne hosta) | 27 |
| 6 | `Core/PlanDeck.Infrastructure/Persistence` | 26 |
| 7 | `Tests/PlanDeck.E2e.Tests` (scenariusze główne) | 26 |
| 8 | `Web/PlanDeck.Client/Services` | 25 |
| 9 | `Core/PlanDeck.Core.Shared/Contracts` | 24 |
| 10 | `Tests/PlanDeck.E2e.Tests/Pages` | 21 |

## Najczęściej modyfikowane pliki

| # | Plik | Commity |
|---:|---|---:|
| 1 | `Web/PlanDeck.Server/Extensions/ServiceCollectionExtensions.cs` | 30 |
| 2 | `Web/PlanDeck.Server/Program.cs` | 27 |
| 3 | `Web/PlanDeck.Client/Resources/SharedResource.pl.resx` | 20 |
| 4 | `Web/PlanDeck.Client/Resources/SharedResource.resx` | 20 |
| 5 | `Aspire/PlanDeck.AppHost/AppHost.cs` | 19 |
| 6 | `Core/PlanDeck.Application/Services/SessionGrpcService.cs` | 16 |
| 7 | `Tests/PlanDeck.Integration.Tests/Realtime/PlanningRoomHubTests.cs` | 16 |
| 8 | `Web/PlanDeck.Client/Pages/Sessions.razor` | 16 |
| 9 | `Tests/PlanDeck.Unit.Tests/Sessions/SessionGrpcServiceTests.cs` | 15 |
| 10 | `Web/PlanDeck.Client/Pages/Sessions.razor.cs` | 13 |

Największa aktywność skupiała się wokół obsługi sesji, konfiguracji backendu i DI, UI sesji, persystencji oraz testów real-time i E2E.

## Zmiana nacisku w czasie

### 2025-Q3, 2025-Q4 i 2026-Q1

Brak commitów.

### 2026-Q2: fundamenty sesji i logika aplikacyjna

**85 commitów**, aktywność od 11 do 30 czerwca.

Najaktywniejsze obszary:

| Obszar | Commity |
|---|---:|
| `Application/Services` | 18 |
| `Core.Shared/Contracts` | 15 |
| `Infrastructure/Persistence` | 15 |
| `Client/Pages` | 15 |
| `Server/Extensions` | 15 |

Nacisk: budowa pionowego przepływu sesji — kontrakty, gRPC, planning room, persystencja, UI i testy jednostkowe.

### 2026-Q3: hosting, projekty i E2E

**75 commitów**, aktywność od 1 do 29 lipca; kwartał jest niepełny.

Najaktywniejsze obszary:

| Obszar | Commity |
|---|---:|
| `Server` (pliki główne hosta) | 17 |
| `Server/Extensions` | 16 |
| `E2e.Tests` | 15 |
| `Aspire/PlanDeck.AppHost` | 14 |
| `Application/Abstractions` | 14 |

Nacisk przesunął się z implementacji domeny sesji na integrację i utwardzanie aplikacji: konfigurację hosta, Aspire, obsługę projektów, testy E2E oraz integracyjne testy real-time.

## Najsilniejsze współzmiany

### Pary katalogów

| Para | Wspólne commity | Pokrycie rzadszego obszaru |
|---|---:|---:|
| `Application/Services` + `Core.Shared/Contracts` | 24 | 100% |
| `Application/Abstractions` + `Infrastructure/Persistence` | 21 | 81% |
| `Application/Abstractions` + `Application/Services` | 21 | 75% |
| `E2e.Tests` + `E2e.Tests/Pages` | 20 | 95% |
| `Server` + `Server/Extensions` | 19 | 70% |

### Trójki katalogów

| Trójka | Wspólne commity |
|---|---:|
| `Application/Services` + `Core.Shared/Contracts` + `Client/Services` | 16 |
| `Application/Abstractions` + `Application/Services` + `Core.Shared/Contracts` | 15 |
| `Application/Abstractions` + `Application/Services` + `Infrastructure/Persistence` | 15 |

Dominują dwa przekrojowe przepływy:

1. **Kontrakt → implementacja gRPC → wrapper klienta.**
2. **Abstrakcja → serwis aplikacyjny → persystencja.**

To wskazuje przede wszystkim na pełne, pionowe przyrosty funkcjonalne. Jednocześnie kontrakty API i interfejsy repozytoriów nadal intensywnie ewoluowały, więc granice między warstwami nie były jeszcze stabilne.

## Pliki będące hubami współzmian

Liczba obszarów oznacza różne katalogi kodu współwystępujące z plikiem w analizowanym okresie.

| Plik | Commity | Różne obszary | Śr. obszarów na commit |
|---|---:|---:|---:|
| `Server/Extensions/ServiceCollectionExtensions.cs` | 30 | 51 | 9,0 |
| `Server/Program.cs` | 27 | 47 | 8,1 |
| `AppHost/AppHost.cs` | 19 | 44 | 8,2 |
| `Infrastructure/PlanDeck.Infrastructure.csproj` | 6 | 41 | 13,5 |
| `Client/Program.cs` | 9 | 40 | 10,8 |
| `Client/Resources/SharedResource.resx` | 20 | 38 | 7,1 |
| `Client/Resources/SharedResource.pl.resx` | 20 | 38 | 7,1 |

Głównym wspólnym mianownikiem jest konfiguracja kompozycji aplikacji: DI, endpointy, hosting i Aspire. Tłumaczenia także są silnym elementem przekrojowym i zmieniały się przy wielu pionowych funkcjach.

W odfiltrowanym szumie wyróżniały się:

- `PlanDeck.Infrastructure.csproj` — szerokie współzmiany, ale tylko w 6 commitach.
- `PlanDeckDbContextModelSnapshot.cs` — 13 commitów i 32 obszary; istnieje, ale jest generowany.
- `AppHost/packages.lock.json` — 25 commitów; usunięty 25 czerwca 2026 po wyłączeniu lockfile'ów NuGet.

## Aktualność historycznych wyników

Wszystkie najważniejsze pliki z rankingów aktywności i współzmian nadal istnieją pod wskazanymi ścieżkami i są śledzone przez Git. Dotyczy to między innymi:

- `ServiceCollectionExtensions.cs`,
- `Program.cs`,
- obu plików `SharedResource*.resx`,
- `AppHost.cs`,
- `SessionGrpcService.cs`,
- `PlanningRoomHubTests.cs`,
- `Sessions.razor` i `Sessions.razor.cs`,
- `SessionGrpcServiceTests.cs`,
- `PlanningRoomHub.cs`,
- `PlanningRoomService.cs`,
- `PlanDeckDbContext.cs`,
- `SessionsPage.cs`,
- `MainLayout.razor`.

Istotnym historycznym wyjątkiem jest `Web/PlanDeck.Server/Identity/TestAuthenticationHandler.cs`. Plik został świadomie usunięty 25 lipca 2026 podczas migracji testów poza produkcyjne mechanizmy uwierzytelniania. Testowe handlery znajdują się obecnie lokalnie w testach integracyjnych, więc usuniętego pliku nie należy traktować jako aktualnego punktu sprzężenia.

## Podsumowanie terytorium

Projekt rozwijał się pionowymi funkcjami skupionymi początkowo na sesjach planning-poker, a następnie na integracji, hostingu, obsłudze projektów i utwardzaniu testów. Najważniejsze granice zmian biegną przez kontrakty gRPC, serwisy aplikacyjne, abstrakcje repozytoriów i persystencję. Najsilniejsze przekrojowe huby znajdują się natomiast w composition rootach aplikacji, co jest oczekiwane na tym etapie, ale czyni konfigurację hosta i DI obszarem o podwyższonym ryzyku regresji.
