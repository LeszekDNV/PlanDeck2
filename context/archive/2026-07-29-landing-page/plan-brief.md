# Adaptacyjny landing page PlanDeck — Plan Brief

> Full plan: `context/changes/landing-page/plan.md`
> Research: `context/changes/landing-page/research.md`

## What & Why

Główna trasa `/` przestanie być placeholderem i stanie się adaptacyjnym punktem wejścia do PlanDeck. Anonim zobaczy efektowną, ale prawdziwą opowieść produktu „Azure DevOps → głosowanie → zapis estymaty”, zwykły użytkownik dostanie szybki start, a gość prostą ścieżkę wejścia kodem.

## Starting Point

Obecny `Home` pokazuje demonstracyjne treści MudBlazor, a zalogowanego użytkownika automatycznie przekierowuje do `/projects`. Motyw, lokalizacja, auth, flow kodu gościa i wszystkie prezentowane możliwości produktu już istnieją; brakuje spójnej strony wejściowej.

## Desired End State

Jeden komponent na `/` renderuje trzy celowe warianty bez niepotrzebnego backendu. Publiczny landing jest niebanalny, responsywny i dostępny w EN/PL oraz light/dark, a zalogowani użytkownicy otrzymują działania odpowiednie do swojej roli.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Trasa | Zachować wyłącznie `/` | To istniejący kanoniczny punkt wejścia; alias `/home` nie daje wartości | Research |
| Zwykły użytkownik | Panel szybkich akcji | Unika kopiowania listy i logiki strony projektów | Plan |
| Gość | Minimalny panel z kodem | Klient nie ma pewnego identyfikatora ostatniej sesji | Plan |
| Kod sesji | Pole na Home → `/join/{code}` | Istniejący ekran zachowuje odpowiedzialność za nazwę i walidację | Plan |
| Koncept | „The Estimation Table” | Pokazuje realną, wyróżniającą pętlę wartości produktu | Research |
| Animacja | Jednorazowa i dekoracyjna | Daje efekt bez wprowadzania fałszywego demo i stanu biznesowego | Plan |
| Zakres treści | Hero, flow, role, kod, trust row | Opowiada kompletną historię bez niegotowych obietnic | Plan |
| Motyw | Tokeny istniejącego MainLayout | Jeden system theme state zapobiega rozjazdom | Research |
| Testy | Policy NUnit + E2E trzech stanów i mobile | Chroni nowy kontrakt bez nowej zależności bUnit | Plan |

## Scope

**In scope:**

- trzy warianty Home: anonim, użytkownik i gość;
- publiczny landing „The Estimation Table”;
- CTA lokalne i warunkowe Microsoft;
- wejście kodem do istniejącej trasy gościa;
- EN/PL, light/dark, reduced motion i mobile 375 px;
- testy policy, parytetu zasobów oraz E2E.

**Out of scope:**

- alias `/home`, osobny layout i nowy backend;
- lista „ostatnich projektów”;
- interaktywne demo głosowania;
- powiadomienia, historia sesji i automatyczna estymata;
- dodatkowe integracje i języki.

## Architecture / Approach

`Home.razor.cs` wylicza stan i obsługuje routing, a czysta `HomePagePolicy` izoluje testowalne reguły użytkownika i kodu. `Home.razor` renderuje jeden wariant, korzystając z MudBlazor; style `pd-home-*` w `app.css` opierają się na tokenach palety istniejącego `MainLayout`.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Adaptacyjny kontrakt | Trzy stany, routing i testowalna policy | Migotanie niewłaściwego wariantu podczas auth init |
| 2. Estimation Table | Kompletny landing EN/PL i light/dark | Przeładowany lub łamiący się układ mobile |
| 3. Weryfikacja | E2E anonima, użytkownika, gościa i 375 px | Koszt niezależnego przygotowania stanu gościa |

**Prerequisites:** Działający Podman i przeglądarka Playwright Chromium do lokalnych E2E.

**Estimated effort:** około 2–3 sesje implementacyjne w 3 fazach.

## Open Risks & Assumptions

- Microsoft auth pozostaje opcjonalny; błąd capability check nie może blokować landingu.
- Kod aktywnej sesji da się pozyskać w E2E przez istniejący UI po aktywacji; Page Object może wymagać małego rozszerzenia.
- Niestandardowe efekty glow/blur muszą pozostać lekkie na urządzeniach mobilnych.

## Success Criteria (Summary)

- Każdy z trzech typów użytkownika widzi właściwy punkt startowy na `/`.
- Landing komunikuje wyłącznie gotowe możliwości i działa w EN/PL, light/dark oraz przy 375 px.
- Krytyczne CTA, kod sesji, dostępność i nowy brak redirectu są zabezpieczone testami.
