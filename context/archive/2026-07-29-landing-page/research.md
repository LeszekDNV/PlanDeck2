---
date: 2026-07-29T17:49:49.0887729+02:00
researcher: GitHub Copilot
git_commit: 4a9f8e07edcf3d340fccbf675facdef524b381ac
branch: testing-entra-deployment-readiness
repository: LeszekDNV/PlanDeck2
topic: "Adaptacyjny landing page PlanDeck na /home"
tags: [research, codebase, blazor, mudblazor, landing-page, theming]
status: complete
last_updated: 2026-07-29
last_updated_by: GitHub Copilot
---

# Research: Adaptacyjny landing page PlanDeck na /home

**Date**: 2026-07-29T17:49:49.0887729+02:00
**Researcher**: GitHub Copilot
**Git Commit**: 4a9f8e07edcf3d340fccbf675facdef524b381ac
**Branch**: testing-entra-deployment-readiness
**Repository**: LeszekDNV/PlanDeck2

## Research Question

Chcę utworzyć efektowny landing page na /home. nie mam pomysłu co miałoby się na nim znaleźć. Przeanalizuj dotychczas wykonaną pracę i zaproponuj efektowny i niebanalny landing page. Weź pod uwagę że mamy motyw jasny i ciemny.

Uzgodniony kierunek: `/home` ma być adaptacyjny — publiczny landing dla osoby niezalogowanej i szybki start dla zalogowanego użytkownika.

## Summary

Obecny `/home` jest placeholderem z szablonu MudBlazor. Jedyną logiką produktową jest przekierowanie zalogowanego użytkownika niebędącego gościem do `/projects` ([Home.razor:1-27](../../../src/PlanDeck/Web/PlanDeck.Client/Pages/Home.razor), [Home.razor.cs:7-14](../../../src/PlanDeck/Web/PlanDeck.Client/Pages/Home.razor.cs)). Landing może więc zostać zaprojektowany od podstaw, ale musi świadomie zmienić istniejący kontrakt przekierowania.

Najsilniejszą, prawdziwie zaimplementowaną opowieścią produktu jest zamknięta pętla:

**Import z Azure DevOps → głosowanie na żywo → uzgodniona estymata → zapis do Azure DevOps.**

Rekomendowany koncept wizualny to **„The Estimation Table”**: hero wyglądający jak aktywny stół planning-pokera, a nie typowy SaaS-owy zestaw kart funkcji. Karta zadania przechodzi przez trzy stany — import, zakryte głosy, odsłonięty wynik — tworząc jedną wizualną linię procesu. W jasnym motywie ekran przypomina precyzyjny stół projektowy, a w ciemnym „war room” zespołu. To ten sam układ i hierarchia, zmieniają się wyłącznie tokeny powierzchni, obramowań, cieni i poświaty.

Publiczny wariant powinien prowadzić do rejestracji/logowania oraz eksponować dołączenie kodem bez konta. Wariant zalogowany powinien zachować projekt jako główny artefakt: szybkie utworzenie projektu, ostatnie projekty i przejście do zespołów. Nie należy obiecywać historii sesji, powiadomień Teams/email, sześciu języków ani automatycznego wyznaczania estymaty — te zakresy nie są gotowe.

## Detailed Findings

### Obecny `/home`, layout i routing

- `Home.razor` zawiera demonstracyjne „Hello, world!” i link do MudBlazor; nie ma treści produktowej ([Home.razor:1-27](../../../src/PlanDeck/Web/PlanDeck.Client/Pages/Home.razor)).
- `Home.razor.cs` przekierowuje zalogowanego użytkownika bez claimu `is_guest=true` do `/projects` ([Home.razor.cs:7-14](../../../src/PlanDeck/Web/PlanDeck.Client/Pages/Home.razor.cs)).
- Istniejący test E2E kontraktuje właśnie to przekierowanie, więc wariant dashboard-lite oznacza celową zmianę zachowania i testu ([HomePageTests.cs:13-21](../../../src/PlanDeck/Tests/PlanDeck.E2e.Tests/HomePageTests.cs)).
- `MainLayout` już rozdziela nawigację dla użytkownika, anonima i gościa oraz zapewnia menu mobilne; landing powinien korzystać z tego samego źródła stanu autoryzacji ([MainLayout.razor:12-121](../../../src/PlanDeck/Web/PlanDeck.Client/Layout/MainLayout.razor)).
- Projekty pozostają głównym punktem wejścia do zarządzania produktem. Adaptacyjny wariant zalogowany nie powinien tworzyć konkurencyjnej, globalnej hierarchii sesji.

### Rzeczywiście gotowa wartość produktu

- Import elementów pracy z Azure DevOps jest dostępny przez kontrakt gRPC i istniejący panel z filtrami typu, stanu i limitu ([IAzureDevOpsWorkItemService.cs:8-14](../../../src/PlanDeck/Core/PlanDeck.Core.Shared/Contracts/IAzureDevOpsWorkItemService.cs), [AdoImportPanel.razor:7-64](../../../src/PlanDeck/Web/PlanDeck.Client/Components/AdoImportPanel.razor)).
- Zadania można również dodawać ręcznie, pojedynczo i masowo ([Sessions.razor.cs:703-732](../../../src/PlanDeck/Web/PlanDeck.Client/Pages/Sessions.razor.cs)).
- Pokój głosowania obsługuje wejście uczestników, zakryte głosy, wspólne odsłonięcie, reset rundy i wybór estymaty ([PlanningRoomHub.cs:1-316](../../../src/PlanDeck/Web/PlanDeck.Server/Hubs/PlanningRoomHub.cs), [VotingRoom.razor:1-201](../../../src/PlanDeck/Web/PlanDeck.Client/Pages/VotingRoom.razor)).
- Uzgodniony wynik może zostać zapisany do Azure DevOps z obsługą rewizji i ograniczeń API ([Sessions.razor.cs:585-613](../../../src/PlanDeck/Web/PlanDeck.Client/Pages/Sessions.razor.cs), [ISessionService.cs:35](../../../src/PlanDeck/Core/PlanDeck.Core.Shared/Contracts/ISessionService.cs)).
- Gość dołącza linkiem lub kodem bez pełnego konta i otrzymuje uprawnienia ograniczone do wskazanej sesji ([GuestAuthentication.cs:1-67](../../../src/PlanDeck/Web/PlanDeck.Server/Identity/GuestAuthentication.cs), [JoinSession.razor.cs:16-69](../../../src/PlanDeck/Web/PlanDeck.Client/Pages/JoinSession.razor.cs), [GuestAccessGuard.cs:11-29](../../../src/PlanDeck/Core/PlanDeck.Application/Services/GuestAccessGuard.cs)).
- Projekty grupują sesje, połączenie ADO i członków z rolami Owner/Admin/Member; użytkownik może zapraszać członków i przekazywać własność ([IProjectService.cs:1-404](../../../src/PlanDeck/Core/PlanDeck.Core.Shared/Contracts/IProjectService.cs), [ProjectDetails.razor.cs:104-225](../../../src/PlanDeck/Web/PlanDeck.Client/Pages/ProjectDetails.razor.cs)).
- Konta lokalne i opcjonalny Microsoft Entra ID są gotowe, łącznie z łączeniem tożsamości ([AccountEndpointExtensions.cs:24-160](../../../src/PlanDeck/Web/PlanDeck.Server/Extensions/AccountEndpointExtensions.cs), [AccountEndpointExtensions.cs:252-349](../../../src/PlanDeck/Web/PlanDeck.Server/Extensions/AccountEndpointExtensions.cs)).

### Koncept „The Estimation Table”

Zamiast generycznego hero ze stockową ilustracją rekomendowany jest produktowy mikro-scenariusz:

1. **Hero — jedna żywa pętla pracy.** Po lewej krótki komunikat: „Od backlogu do uzgodnionej estymaty — bez opuszczania Azure DevOps”. Po prawej stylizowany stół z kartą zadania pośrodku, zakrytymi kartami uczestników i pionowym torem `Import → Vote → Sync`.
2. **Moment odsłonięcia.** Delikatna, jednorazowa animacja odwraca karty i podświetla wybraną wartość. Animacja jest dekoracyjna, nie blokuje treści i znika przy `prefers-reduced-motion`.
3. **Dwie ścieżki, jeden stół.** Krótka sekcja typu split-screen pokazuje prowadzącego („importuj, uruchom, zapisz”) i uczestnika („wejdź linkiem, wybierz kartę, gotowe”). To komunikuje różnicę ról bez kolejnej siatki feature cards.
4. **Pas dołączania.** Pełnoszeroki pasek „Masz kod sesji?” z pojedynczym polem i CTA do `/join/{code}`. Jest widoczny wysoko na stronie, ale wizualnie wtórny wobec założenia konta.
5. **Dowody dojrzałości.** Kompaktowy rząd: Local/Entra ID, izolacja organizacji, EN/PL, light/dark. Bez marketingowych akapitów i bez obietnic niegotowych funkcji.

Treść hero powinna opierać się na rezultacie, nie na mechanice planning pokera. Przykładowa hierarchia:

- Eyebrow: `PLANDECK · PLANNING POKER FOR AZURE DEVOPS`
- H1: `Od backlogu do wspólnej estymaty. W jednym rozdaniu.`
- Lead: `Importuj zadania, głosujcie na żywo i zapisz uzgodniony wynik bezpośrednio w Azure DevOps.`
- Primary CTA: `Rozpocznij planowanie`
- Secondary CTA: `Mam kod sesji`

Wszystkie teksty muszą otrzymać pary kluczy EN/PL w `SharedResource.resx` i `SharedResource.pl.resx`.

### Zachowanie adaptacyjne

| Stan | Rekomendowany `/home` |
|---|---|
| Anonim | Pełny landing, CTA rejestracji/logowania, warunkowe CTA Entra oraz pasek dołączenia kodem |
| Zalogowany użytkownik | Dashboard-lite: „Kontynuuj planowanie”, ostatnie projekty, „Nowy projekt”, link do zespołów |
| Zalogowany gość | Minimalny ekran uczestnika z polem kodu lub powrotem do sesji tylko wtedy, gdy istnieje pewne źródło identyfikatora aktywnej sesji |

Przycisk Microsoft należy pokazać dopiero po sprawdzeniu `AuthenticationCapabilitiesReply.MicrosoftAuthenticationAvailable`, ponieważ Entra jest opcjonalny zależnie od środowiska ([IAuthService.cs:49-53](../../../src/PlanDeck/Core/PlanDeck.Core.Shared/Contracts/IAuthService.cs)).

Jeżeli plan nie chce rozszerzać pobierania danych na `/home`, bezpieczniejszy MVP dla zalogowanego użytkownika to krótki panel szybkich akcji z przejściem do `/projects`, bez duplikowania listy i logiki `Projects.razor.cs`.

### Jasny i ciemny motyw

- Theme provider, przełącznik i zapamiętywanie preferencji już istnieją; landing nie powinien utrzymywać własnego stanu motywu ([MainLayout.razor:34-39](../../../src/PlanDeck/Web/PlanDeck.Client/Layout/MainLayout.razor), [MainLayout.razor.cs:71-135](../../../src/PlanDeck/Web/PlanDeck.Client/Layout/MainLayout.razor.cs)).
- Jasny wariant: powierzchnie „papier/karta”, subtelna siatka blueprint, chłodne fiolety jako akcent i twarde, krótkie cienie kart.
- Ciemny wariant: matowe powierzchnie „war room”, ta sama geometria, miękka fioletowa poświata tylko wokół aktywnego wyniku.
- Nie należy kodować kolorów bezpośrednio w markup. Style powinny opierać się na zmiennych/tokennach powiązanych z paletą MudBlazor.
- Efekt nie może zależeć wyłącznie od koloru: stany importu, głosowania i synchronizacji wymagają ikony, etykiety i kształtu.
- Układ powinien być sprawdzony co najmniej dla szerokości 375 px. Wcześniejsza zmiana ujawniła obcinanie nawigacji i długich etykiet na mobile.

### Dostępność i responsywność

- Hero musi używać semantycznego `h1`, a sekcje logicznych nagłówków; dekoracyjne karty powinny być ukryte przed czytnikiem lub otrzymać zwięzły odpowiednik tekstowy.
- CTA muszą mieć widoczny focus i jednoznaczne etykiety; formularz kodu powinien korzystać z label, nie tylko placeholdera.
- Animacja odsłonięcia respektuje `prefers-reduced-motion`.
- Na mobile wizualny stół składa się do poziomego, przewijalnego tylko wewnętrznie toru albo — preferowane — do pionowych trzech etapów. Strona nie może powodować poziomego scrolla.
- Obecny `app.css` nie zawiera systemu styli landing page, więc potrzebne będą odrębne klasy i media queries ([app.css:1-164](../../../src/PlanDeck/Web/PlanDeck.Client/wwwroot/css/app.css)).

### Zakres, którego nie należy komunikować jako gotowy

- Powiadomienia email lub Microsoft Teams o starcie sesji.
- Historia i archiwum zakończonych sesji.
- Języki inne niż polski i angielski.
- Automatyczne wyznaczanie estymaty przez medianę, modę lub algorytm.
- Integracje inne niż Azure DevOps.

## Code References

- `src/PlanDeck/Web/PlanDeck.Client/Pages/Home.razor:1-27` — aktualny placeholder strony głównej.
- `src/PlanDeck/Web/PlanDeck.Client/Pages/Home.razor.cs:7-14` — przekierowanie zalogowanego użytkownika do projektów.
- `src/PlanDeck/Web/PlanDeck.Client/Layout/MainLayout.razor:12-121` — nawigacja, autoryzacja i przełączniki UI.
- `src/PlanDeck/Web/PlanDeck.Client/Layout/MainLayout.razor.cs:71-135` — stan i palety obu motywów.
- `src/PlanDeck/Web/PlanDeck.Client/Components/AdoImportPanel.razor:7-64` — gotowy przepływ importu ADO.
- `src/PlanDeck/Web/PlanDeck.Server/Hubs/PlanningRoomHub.cs:1-316` — real-time planning room.
- `src/PlanDeck/Web/PlanDeck.Client/Pages/VotingRoom.razor:1-201` — doświadczenie głosowania.
- `src/PlanDeck/Web/PlanDeck.Client/Pages/Sessions.razor.cs:585-613` — write-back uzgodnionej estymaty.
- `src/PlanDeck/Web/PlanDeck.Client/Pages/JoinSession.razor.cs:16-69` — dołączanie gościa kodem.
- `src/PlanDeck/Core/PlanDeck.Core.Shared/Contracts/IProjectService.cs:1-404` — model projektów, ról i zaproszeń.
- `src/PlanDeck/Web/PlanDeck.Client/Resources/SharedResource.resx` — angielskie zasoby UI.
- `src/PlanDeck/Web/PlanDeck.Client/Resources/SharedResource.pl.resx` — polskie zasoby UI.
- `src/PlanDeck/Web/PlanDeck.Client/wwwroot/css/app.css:1-164` — obecne globalne style.

## Architecture Insights

- Warstwa prezentacji jest Blazor WebAssembly z MudBlazor. Logika komponentu musi pozostać w `Home.razor.cs`, a markup w `Home.razor`.
- Adaptacyjność można oprzeć na istniejącym `AuthenticationStateProvider` i claimie `is_guest`; nie jest potrzebny nowy mechanizm sesji.
- Wariant zalogowany powinien użyć istniejących interfejsów klienta, jeżeli ma pobierać projekty. Dla MVP lepsza jest kompozycja istniejących akcji niż kopiowanie logiki strony Projects.
- Landing dziedziczy `MudThemeProvider` z layoutu. Osobny `LandingLayout` zwiększa koszt i ryzyko rozjazdu nawigacji, lokalizacji oraz motywu; obecny `MainLayout` jest bezpieczniejszym punktem wyjścia.
- Wszystkie backendowe wywołania pozostają w istniejących wrapperach klienta. Landing nie powinien bezpośrednio tworzyć kanałów gRPC.

## Historical Context (from prior changes)

- `context/changes/reorganize-project-and-sessions/plan.md:235-246` — zalogowani użytkownicy wchodzą przez `/projects`; projekty są nadrzędne wobec sesji.
- `context/changes/reorganize-project-and-sessions/reviews/impl-review.md:164-170` — znana regresja mobilna przy 375×812; landing musi unikać podobnego obcinania.
- `context/changes/light-theme/plan.md:5-29` — motyw jest dostępny dla każdego, a brak zapisanej preferencji powinien respektować ustawienie systemowe.
- `context/changes/light-theme/plan.md:107-121` — klucze lokalizacji EN/PL muszą zachować parytet.
- `context/archive/2026-06-22-realtime-voting-round/plan.md` — reguły rundy i wspólnego odsłonięcia głosów.
- `context/archive/2026-06-24-guest-link-voting/plan.md` — zachowanie ścieżki `/join/{code}` i ograniczonego dostępu gościa.
- `context/archive/2026-06-24-azure-devops-import/research.md` — wcześniejsze badanie integracji importu ADO.
- `context/archive/2026-06-24-ado-estimate-writeback/research.md` — wcześniejsze badanie zapisu estymaty do ADO.

## Related Research

- [Reorganize projects and sessions](../reorganize-project-and-sessions/research.md)
- [Azure DevOps import](../../archive/2026-06-24-azure-devops-import/research.md)
- [Azure DevOps estimate write-back](../../archive/2026-06-24-ado-estimate-writeback/research.md)
- [Testing critical path integrity](../../archive/2026-06-27-testing-critical-path-integrity/research.md)

## Open Questions

1. Czy wariant zalogowany ma zastąpić istniejące przekierowanie do `/projects`, czy `/home` ma pozostać publicznym landingiem, a dashboard-lite być osobną trasą? Uzgodniony kierunek wskazuje na zastąpienie redirectu, ale jest to zmiana istniejącego kontraktu UX i E2E.
2. Czy formularz kodu sesji ma być osadzony bezpośrednio w hero, czy prowadzić do istniejącej strony dołączania? Bezpieczniejszy zakres MVP to walidowany kod i nawigacja do `/join/{code}`, bez duplikowania procesu.
3. Czy „stół estymacji” ma być czysto dekoracyjny, czy interaktywny? Rekomendacja: lekka, deterministyczna prezentacja bez stanu biznesowego; prawdziwa interakcja zwiększa koszt dostępności i testów, nie wzmacniając głównego CTA.
