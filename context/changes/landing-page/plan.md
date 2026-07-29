# Adaptacyjny landing page PlanDeck — Implementation Plan

## Overview

Zastępujemy placeholder na głównej trasie `/` adaptacyjnym doświadczeniem dopasowanym do stanu użytkownika. Anonim zobaczy efektowny landing „The Estimation Table”, zwykły użytkownik otrzyma lekki panel szybkich akcji, a zalogowany gość — prostą ścieżkę ponownego wejścia kodem do sesji.

## Current State Analysis

`Home` jest nadal ekranem demonstracyjnym MudBlazor i wywołuje przykładową usługę Hello. Podczas inicjalizacji każdy zalogowany użytkownik niebędący gościem jest przekierowywany do `/projects`, co utrwala obecny test E2E.

Projekt ma już wszystkie potrzebne elementy infrastruktury: stan uwierzytelnienia i claim gościa, warunkowe wykrywanie logowania Microsoft, routing `/join/{Code}`, lokalizację EN/PL, dwa motywy MudBlazor oraz istniejące strony projektów i zespołów. Nie jest potrzebna zmiana backendu ani modelu danych.

## Desired End State

Trasa `/` renderuje jeden z trzech stabilnych wariantów bez automatycznego przekierowania:

- anonim: pełny, lokalizowany landing pokazujący rzeczywisty przepływ `Import → Vote → Sync`, CTA konta/logowania i pole kodu sesji;
- zalogowany użytkownik: panel „Kontynuuj planowanie” z szybkimi przejściami do projektów, tworzenia projektu i zespołów;
- zalogowany gość: minimalny ekran uczestnika z polem kodu sesji.

Publiczna wizualizacja zachowuje tę samą geometrię w jasnym i ciemnym motywie, składa się poprawnie przy 375 px, nie powoduje poziomego przewijania i ogranicza animację przy `prefers-reduced-motion`. Zachowanie trzech wariantów, routing CTA i mobilny układ są objęte testami.

### Key Discoveries:

- Obecny redirect i placeholder znajdują się w `src/PlanDeck/Web/PlanDeck.Client/Pages/Home.razor:1-27` oraz `Home.razor.cs:7-19`.
- `MainLayout` już obsługuje autoryzację, gościa, nawigację mobilną, język i oba motywy; osobny layout zwiększyłby ryzyko rozjazdu (`src/PlanDeck/Web/PlanDeck.Client/Layout/MainLayout.razor:12-125`).
- Dostępność Microsoft loginu jest pobierana przez `IAccountClientService.IsMicrosoftAuthenticationAvailableAsync()` i ma gotowy wzorzec użycia w `Pages/Account/Login.razor.cs:29-50`.
- Istniejąca trasa `src/PlanDeck/Web/PlanDeck.Client/Pages/JoinSession.razor:1` przyjmuje kod, a backend wykonuje właściwą walidację po podaniu nazwy gościa.
- Czyste reguły UI są już wydzielane do testowalnych klas policy, np. `Pages/SessionPagePolicy.cs:7-35`, bez dodawania frameworka testów komponentowych.
- Parytet kluczy EN/PL jest automatycznie chroniony przez `Tests/PlanDeck.Unit.Tests/Client/LocalizationResourceParityTests.cs:8-34`.

## What We're NOT Doing

- Nie dodajemy aliasu `/home`; kanoniczną trasą pozostaje `/`.
- Nie tworzymy osobnego `LandingLayout`.
- Nie pobieramy ani nie pokazujemy „ostatnich projektów”, ponieważ kontrakt nie dostarcza wiarygodnej kolejności ostatniej aktywności.
- Nie implementujemy interaktywnego demo głosowania ani stanu biznesowego w dekoracyjnym stole.
- Nie przenosimy formularza nazwy gościa ani walidacji aktywnej sesji z `JoinSession`.
- Nie dodajemy backendu, migracji, nowych kontraktów gRPC ani zależności testowych.
- Nie komunikujemy powiadomień, historii sesji, automatycznego wyboru estymaty, dodatkowych języków ani integracji innych niż Azure DevOps.

## Implementation Approach

Zachowujemy `Home` jako jeden komponent z jawnie wyliczonym wariantem widoku. Czysta policy mapuje `ClaimsPrincipal` na stan anonim/użytkownik/gość oraz normalizuje kod sesji, dzięki czemu krytyczne rozgałęzienia można sprawdzić zwykłymi testami NUnit. Code-behind pobiera stan uwierzytelnienia, warunkowo sprawdza dostępność Microsoft loginu tylko dla anonima i obsługuje nawigację; markup odpowiada wyłącznie za renderowanie odpowiedniego wariantu.

Publiczny landing używa komponentów MudBlazor i semantycznych sekcji, natomiast niestandardowa kompozycja „The Estimation Table” otrzymuje izolowane klasy `pd-home-*` w istniejącym arkuszu. Kolory wynikają z tokenów `--mud-palette-*`, a media queries odpowiadają za mobile i reduced motion.

## Critical Implementation Details

### Timing & lifecycle

Komponent musi pozostać w neutralnym stanie ładowania do czasu zakończenia `GetAuthenticationStateAsync()`, aby nie pokazać publicznych CTA zalogowanej osobie. Sprawdzenie dostępności Microsoft loginu jest wykonywane wyłącznie dla anonima; oczekiwany błąd gRPC ukrywa tylko opcjonalne CTA i jest raportowany przez logger, nie blokując lokalnego logowania ani rejestracji.

### User experience spec

Dekoracyjny stół nie może trafiać do drzewa dostępności jako zestaw interaktywnych kart; jego sens opisuje sąsiednia treść `Import → Vote → Sync`. Kod sesji jest przycinany, pusty kod nie nawiguje, a poprawne zatwierdzenie prowadzi do zakodowanej trasy `/join/{code}`.

## Phase 1: Adaptacyjny kontrakt strony głównej

### Overview

Usuwamy demonstracyjny kod Hello i redirect do projektów, wprowadzając testowalny model trzech stanów oraz właściwe akcje nawigacyjne.

### Changes Required:

#### 1. Reguły wariantu Home

**File**: `src/PlanDeck/Web/PlanDeck.Client/Pages/HomePagePolicy.cs`

**Intent**: Wydzielić deterministyczne decyzje UI od cyklu życia komponentu, zgodnie z istniejącym wzorcem `SessionPagePolicy`.

**Contract**: Policy klasyfikuje użytkownika jako anonymous, registered lub guest na podstawie `Identity.IsAuthenticated` i claimu `is_guest=true`; osobna reguła akceptuje wyłącznie niepusty kod po przycięciu i tworzy bezpieczny segment trasy dołączenia.

#### 2. Stan i akcje komponentu

**File**: `src/PlanDeck/Web/PlanDeck.Client/Pages/Home.razor.cs`

**Intent**: Zastąpić redirect i wywołanie Hello obsługą adaptacyjnego wariantu, opcjonalnego logowania Microsoft i nawigacji CTA.

**Contract**: Code-behind przechowuje jawny stan loading/anonymous/registered/guest, pobiera `AuthenticationStateProvider`, sprawdza Microsoft auth przez `IAccountClientService` tylko dla anonima oraz udostępnia akcje: rejestracja, logowanie, Microsoft, projekty, nowy projekt, zespoły i `/join/{encodedCode}`. Obsługuje wyłącznie oczekiwane błędy capability check i loguje degradację opcjonalnego CTA.

#### 3. Strukturalne warianty widoku

**File**: `src/PlanDeck/Web/PlanDeck.Client/Pages/Home.razor`

**Intent**: Wprowadzić warunkowe szkielety trzech wariantów oraz semantyczny stan ładowania bez finalnego dopracowania publicznych sekcji.

**Contract**: Komponent pozostaje na `@page "/"`, używa lokalizatora i istniejących serwisów przez DI, renderuje dokładnie jeden wariant oraz nie zawiera `@code`. Wariant użytkownika oferuje szybkie akcje bez pobierania listy projektów; wariant gościa oferuje wyłącznie wejście kodem.

#### 4. Testy czystych reguł Home

**File**: `src/PlanDeck/Tests/PlanDeck.Unit.Tests/Client/HomePagePolicyTests.cs`

**Intent**: Zabezpieczyć klasyfikację wszystkich stanów użytkownika oraz reguły kodu sesji bez dodawania bUnit.

**Contract**: NUnit obejmuje anonimowego principal, zwykłego użytkownika, guest claim z różną wielkością liter, pusty/biały kod, trimming oraz kod wymagający bezpiecznego zakodowania w URL.

### Success Criteria:

#### Automated Verification:

- Testy reguł Home przechodzą: `dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj --filter "FullyQualifiedName~HomePagePolicyTests"`
- Projekt klienta kompiluje się: `dotnet build Web/PlanDeck.Client/PlanDeck.Client.csproj`

#### Manual Verification:

- Po wejściu na `/` anonim, zwykły użytkownik i gość widzą właściwy, pojedynczy wariant bez mignięcia niewłaściwej treści.
- Szybkie akcje użytkownika oraz pole kodu gościa prowadzą do oczekiwanych tras.

**Implementation Note**: Po ukończeniu fazy i automatycznej weryfikacji należy zatrzymać się na ręczne potwierdzenie zachowania trzech stanów przed przejściem do finalnego designu.

---

## Phase 2: Publiczne doświadczenie „The Estimation Table”

### Overview

Budujemy kompletną historię publicznego landingu, lokalizację oraz responsywny wygląd jasnego i ciemnego motywu.

### Changes Required:

#### 1. Pełna kompozycja publicznego landingu

**File**: `src/PlanDeck/Web/PlanDeck.Client/Pages/Home.razor`

**Intent**: Zastąpić strukturalny wariant anonima niebanalnym landingiem opartym na realnym przepływie produktu.

**Contract**: Widok zawiera hero z jednym `h1`, dekoracyjny stół z etapami Import/Vote/Sync, sekcję dwóch ról prowadzący/uczestnik, dostępne pole kodu sesji oraz kompaktowy rząd zaufania: local/Microsoft auth, izolacja organizacji, EN/PL i light/dark. Primary CTA prowadzi do rejestracji, secondary do logowania lub pola kodu, a Microsoft CTA jest renderowane wyłącznie po pozytywnym capability check.

#### 2. Lokalizacja treści Home

**Files**:

- `src/PlanDeck/Web/PlanDeck.Client/Resources/SharedResource.resx`
- `src/PlanDeck/Web/PlanDeck.Client/Resources/SharedResource.pl.resx`

**Intent**: Zapewnić pełny parytet EN/PL dla każdego tekstu użytkowego nowego landingu i paneli zalogowanych.

**Contract**: Klucze używają spójnego prefiksu `Home_`; oba pliki zawierają identyczny zestaw kluczy dla tytułu strony, hero, CTA, etapów, ról, pola kodu, rzędu zaufania, panelu użytkownika, panelu gościa i dostępnych etykiet dekoracji.

#### 3. Tokenizowane style i animacja

**File**: `src/PlanDeck/Web/PlanDeck.Client/wwwroot/css/app.css`

**Intent**: Nadać landingowi odrębną geometrię „stołu estymacji” bez tworzenia konkurencyjnego systemu motywu.

**Contract**: Klasy `pd-home-*` bazują na tokenach palety MudBlazor, zachowują czytelny kontrast i focus, nie używają koloru jako jedynego nośnika stanu oraz nie powodują globalnego overflow. Jednorazowa animacja odsłonięcia jest dekoracyjna; `prefers-reduced-motion: reduce` wyłącza transformacje i przejścia. Przy szerokości 375 px stół składa się do pionowej sekwencji etapów.

### Success Criteria:

#### Automated Verification:

- Parytet zasobów EN/PL przechodzi: `dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj --filter "FullyQualifiedName~LocalizationResourceParityTests"`
- Całe rozwiązanie kompiluje się: `dotnet build PlanDeck.slnx`

#### Manual Verification:

- Publiczny landing jest czytelny i atrakcyjny w jasnym i ciemnym motywie, bez niezamierzonych zmian układu.
- Przy 375 px wszystkie sekcje składają się pionowo, CTA pozostają dostępne i nie występuje poziome przewijanie strony.
- Nawigacja klawiaturą ma widoczny focus, nagłówki mają logiczną hierarchię, a reduced motion usuwa obrót/odsłonięcie kart.
- Teksty EN i PL są naturalne, mieszczą się w kontrolkach i nie komunikują funkcji spoza zakresu.

**Implementation Note**: Po ukończeniu fazy należy zatrzymać się na ręczną akceptację wyglądu obu motywów, mobile i reduced motion.

---

## Phase 3: Weryfikacja adaptacyjnego doświadczenia

### Overview

Aktualizujemy Page Object i zastępujemy stary test redirectu scenariuszami nowego kontraktu `/`.

### Changes Required:

#### 1. Page Object nowej strony Home

**File**: `src/PlanDeck/Tests/PlanDeck.E2e.Tests/Pages/HomePage.cs`

**Intent**: Zastąpić lokatory placeholdera stabilnym API testowym dla trzech wariantów i najważniejszych akcji.

**Contract**: Page Object używa wyłącznie lokatorów opartych o role, label i tekst, czeka na znany element po bootowaniu WASM, udostępnia warianty anonymous/registered/guest, CTA, pole kodu oraz pomocniczą kontrolę braku poziomego overflow. Nie używa CSS/XPath ani timeoutów arbitralnych.

#### 2. Scenariusze E2E Home

**File**: `src/PlanDeck/Tests/PlanDeck.E2e.Tests/HomePageTests.cs`

**Intent**: Zastąpić test `Home_RedirectsAuthenticatedUserToProjects` pokryciem nowego adaptacyjnego zachowania.

**Contract**: Niezależne testy weryfikują: anonim widzi hero i przechodzi kodem do `/join/{code}`; zwykły użytkownik pozostaje na `/`, widzi szybkie akcje i może przejść do projektów; uwierzytelniony gość widzi wariant uczestnika bez administracyjnych CTA. Dane kont i sesji są unikalne, a scenariusz gościa tworzy i aktywuje własną sesję przed dołączeniem.

#### 3. Wsparcie tworzenia stanu gościa

**Files**:

- `src/PlanDeck/Tests/PlanDeck.E2e.Tests/Pages/SessionsPage.cs`
- `src/PlanDeck/Tests/PlanDeck.E2e.Tests/Pages/JoinSessionPage.cs`

**Intent**: Rozszerzyć istniejące Page Object o minimalne akcje potrzebne do niezależnego scenariusza gościa zamiast omijać UI.

**Contract**: `SessionsPage` pozwala pobrać aktywny kod/link zaproszenia po aktywacji, a `JoinSessionPage` realizuje poprawne dołączenie i oczekuje przejścia do pokoju głosowania. Lokatory pozostają dostępnościowe i wielojęzyczne tam, gdzie istniejący zestaw testów tego wymaga.

#### 4. Mobilna kontrola layoutu

**File**: `src/PlanDeck/Tests/PlanDeck.E2e.Tests/HomePageTests.cs`

**Intent**: Zabezpieczyć znaną klasę regresji z obcinaniem nawigacji i długich etykiet na małych ekranach.

**Contract**: Osobny test ustawia viewport 375×812, sprawdza widoczność hero, pola kodu i głównego CTA oraz potwierdza, że `documentElement.scrollWidth` nie przekracza `clientWidth`.

### Success Criteria:

#### Automated Verification:

- Testy E2E Home przechodzą lokalnie przez Aspire: `dotnet test Tests/PlanDeck.E2e.Tests/PlanDeck.E2e.Tests.csproj --filter "FullyQualifiedName~HomePageTests"`
- Wszystkie testy jednostkowe przechodzą: `dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj`
- Całe rozwiązanie kompiluje się: `dotnet build PlanDeck.slnx`

#### Manual Verification:

- Publiczne CTA, kod sesji, szybkie akcje użytkownika i wariant gościa prowadzą do właściwych ekranów w realnej aplikacji uruchomionej przez AppHost.
- Landing zachowuje poprawny układ po zmianie języka i przełączeniu motywu bez przeładowania procesu aplikacji.

**Implementation Note**: Po automatycznej weryfikacji należy wykonać końcowy smoke test przez `dotnet run --project Aspire/PlanDeck.AppHost`.

---

## Testing Strategy

### Unit Tests:

- Klasyfikacja anonymous/registered/guest, w tym case-insensitive guest claim.
- Walidacja, trimming i bezpieczne budowanie trasy z kodu sesji.
- Istniejący test parytetu kluczy lokalizacji EN/PL.

### Integration Tests:

- E2E anonima: render hero, kod sesji i routing do istniejącego flow dołączania.
- E2E użytkownika: rejestracja/logowanie, pozostanie na `/`, widoczność szybkich akcji.
- E2E gościa: własna aktywna sesja, dołączenie, powrót na `/`, brak administracyjnych CTA.
- E2E mobile: viewport 375×812 i brak poziomego overflow.

### Manual Testing Steps:

1. Uruchomić pełną aplikację przez AppHost i otworzyć `/` bez zalogowania.
2. Sprawdzić EN/PL oraz jasny/ciemny motyw dla całego publicznego landingu.
3. Przejść klawiaturą przez CTA i pole kodu; sprawdzić focus oraz pusty/poprawny kod.
4. Włączyć systemowe reduced motion i potwierdzić brak animowanego odsłonięcia.
5. Zalogować zwykłe konto i potwierdzić panel szybkich akcji bez redirectu.
6. Dołączyć jako gość do aktywnej sesji, wrócić na `/` i potwierdzić minimalny wariant uczestnika.
7. Powtórzyć kluczowe kontrole przy 375×812 i upewnić się, że strona nie przewija się poziomo.

## Performance Considerations

Landing nie pobiera list projektów ani danych sesji. Jedynym dodatkowym wywołaniem dla anonima jest cache'owane sprawdzenie dostępności Microsoft auth. Dekoracja nie używa JavaScript ani ciągłej animacji; CSS powinien ograniczyć kosztowne blur i glow do niewielkich powierzchni oraz unikać animowania layoutu.

## Migration Notes

Brak migracji danych i backendu. Zmienia się kontrakt UX: zalogowany użytkownik pozostaje na `/` zamiast automatycznie trafiać do `/projects`; rollback polega na przywróceniu redirectu i poprzedniego testu E2E.

## References

- Related research: `context/changes/landing-page/research.md`
- Current Home: `src/PlanDeck/Web/PlanDeck.Client/Pages/Home.razor:1-27`
- Current redirect: `src/PlanDeck/Web/PlanDeck.Client/Pages/Home.razor.cs:7-14`
- Shared layout and theme: `src/PlanDeck/Web/PlanDeck.Client/Layout/MainLayout.razor:12-125`
- Theme palettes: `src/PlanDeck/Web/PlanDeck.Client/Layout/MainLayout.razor.cs:71-147`
- Microsoft auth pattern: `src/PlanDeck/Web/PlanDeck.Client/Pages/Account/Login.razor.cs:29-50`
- Join route: `src/PlanDeck/Web/PlanDeck.Client/Pages/JoinSession.razor:1-40`
- Existing UI policy pattern: `src/PlanDeck/Web/PlanDeck.Client/Pages/SessionPagePolicy.cs:7-35`
- Existing Home E2E contract: `src/PlanDeck/Tests/PlanDeck.E2e.Tests/HomePageTests.cs:13-20`
- E2E Page Object pattern: `src/PlanDeck/Tests/PlanDeck.E2e.Tests/Pages/HomePage.cs:5-35`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles.

### Phase 1: Adaptacyjny kontrakt strony głównej

#### Automated

- [x] 1.1 Testy reguł Home przechodzą — 9a73eb1
- [x] 1.2 Projekt klienta kompiluje się — 9a73eb1

#### Manual

- [x] 1.3 Trzy stany użytkownika renderują właściwy wariant bez mignięcia — 9a73eb1
- [x] 1.4 Szybkie akcje i pole kodu prowadzą do właściwych tras — 9a73eb1

### Phase 2: Publiczne doświadczenie „The Estimation Table”

#### Automated

- [x] 2.1 Parytet zasobów EN/PL przechodzi
- [x] 2.2 Całe rozwiązanie kompiluje się

#### Manual

- [x] 2.3 Landing jest czytelny w jasnym i ciemnym motywie
- [x] 2.4 Układ 375 px nie powoduje poziomego przewijania
- [x] 2.5 Focus, semantyka i reduced motion są poprawne
- [x] 2.6 Teksty EN/PL są kompletne i zgodne z zakresem produktu

### Phase 3: Weryfikacja adaptacyjnego doświadczenia

#### Automated

- [ ] 3.1 Testy E2E Home przechodzą lokalnie przez Aspire
- [ ] 3.2 Wszystkie testy jednostkowe przechodzą
- [ ] 3.3 Całe rozwiązanie kompiluje się

#### Manual

- [ ] 3.4 Krytyczne ścieżki działają w aplikacji uruchomionej przez AppHost
- [ ] 3.5 Zmiana języka i motywu zachowuje poprawny układ
