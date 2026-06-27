# Test Plan

> Phased test rollout for this project. Strategy is frozen at the top
> (§1–§5); cookbook patterns at the bottom (§6) fill in as phases ship.
> Read before writing any new test.
>
> Refresh: re-run `/10x-test-plan --refresh` when stale (see §8).
>
> Last updated: 2026-06-27

## 1. Strategy

Testy w tym projekcie trzymają trzy nienegocjowalne zasady:

1. **Cost × signal.** Najtańszy test, który daje realny sygnał dla ryzyka, wygrywa. Nie promujemy wszystkiego do e2e „bo bezpieczniej”.
2. **User concerns are first-class evidence.** Obawy zespołu z Phase 2 mają taką samą wagę jak PRD, roadmapa i hot-spoty.
3. **Risks are scenarios, not code locations.** Ten plan opisuje co może się zepsuć i dlaczego to prawdopodobne. Nie przypisuje awarii do konkretnych linii kodu; to dostarcza `/10x-research` w fazach rolloutu.

Hot-spot scope used for likelihood weighting: `src/PlanDeck/Core`, `src/PlanDeck/Web`.

## 2. Risk Map

Top scenariusze awarii, uporządkowane przez ryzyko = impact × likelihood:

| # | Risk (failure scenario) | Impact | Likelihood | Source (evidence — not anchor) |
|---|---|---|---|---|
| 1 | W trakcie głosowania sesja się rozłącza, a system gubi/duplikuje głosy albo ujawnia niespójny stan reveal. | High | High | PRD guardrails (real-time consistency + hidden-until-reveal); roadmap (F-02 vote-integrity nadal proposed); interview Q1; hot-spot dirs `src/PlanDeck/Web` i `src/PlanDeck/Core` (30d churn) |
| 2 | Uzgodniona estymata nie zapisuje się poprawnie (lokalnie lub do Azure DevOps) albo porażka zapisu nie jest jasno sygnalizowana. | High | High | PRD guardrails (no silent write-back failure); roadmap (S-08 loop closure); interview Q1; archived slice: ado-estimate-writeback |
| 3 | Zmiana w jednym module psuje inne części systemu (regresja między przepływami). | High | Medium | interview Q2; roadmap dependency graph i zakończone zależne slice’y; hot-spot dirs `src/PlanDeck/Web` i `src/PlanDeck/Core` |
| 4 | Import z Azure DevOps zwraca niepełne/błędne dane, przez co sesja powstaje na złym zestawie zadań. | High | Medium | PRD FR-003; interview Q4; archived slice: azure-devops-import |
| 5 | Tworzenie/konfiguracja sesji zapisuje nieprawidłowy stan (task selection / voting scale), który destabilizuje rundę głosowania. | Medium | Medium | PRD FR-005/FR-006; interview Q4; archived slice: create-configure-session |
| 6 | Abuse scenario: user/guest uzyskuje dostęp do sesji poza swoim zakresem (authorization/share-code scope leak). | High | Medium | PRD tenant isolation + guest-link constraints; roadmap guest-link unknowns around code scoping |

### Risk Response Guidance

| Risk | What would prove protection | Must challenge | Context `/10x-research` must ground | Likely cheapest layer | Anti-pattern to avoid |
|------|-----------------------------|----------------|--------------------------------------|-----------------------|-----------------------|
| #1 | Reconnect/reveal daje jeden spójny wynik rundy bez utraty i duplikacji głosów. | „Reconnect automatycznie naprawia stan.” | Granice sesji rundy, reguła idempotencji oddania głosu, semantyka reveal przy reconnect. | integration + targeted e2e | Happy-path bez reconnect i bez konfliktów czasowych. |
| #2 | Użytkownik dostaje jednoznaczny rezultat zapisu estymaty; brak silent drop. | „200 OK oznacza poprawny zapis biznesowy.” | Granica persistence vs write-back, mapowanie estimate field, obsługa błędów i retry. | integration + contract | Oracle z implementacji, brak negatywnych ścieżek zapisu. |
| #3 | Krytyczne przepływy pozostają poprawne po zmianie sąsiedniego modułu. | „Zmiana lokalna ma tylko lokalny efekt.” | Przepływ danych przez import → session → voting → write-back, kontrakty między warstwami. | integration smoke suite | Nadmierne mockowanie internali maskujące regresję. |
| #4 | Import utrzymuje poprawne mapowanie rekordów i zachowanie na danych brzegowych. | „Zewnętrzny payload jest zawsze stabilny.” | Reguły mapowania danych ADO, walidacja braków i niezgodnych pól, fallback/errored response. | contract + integration | Jeden payload happy-path jako jedyny oracle. |
| #5 | Utworzona sesja ma poprawny zestaw tasków i konfigurację skali, gotową do rundy. | „Zapis konfiguracji jest zawsze atomowy i spójny.” | Persisted shape konfiguracji, reguły walidacji konfiguracji, zachowanie przy częściowych błędach. | integration | Snapshoty UI bez asercji stanu domenowego. |
| #6 | Dostęp do sesji jest ograniczony przynależnością i poprawnym share-code scope. | „Authenticated == authorized.” | Reguły ownership/tenant scope, granice guest-link, negatywne ścieżki dostępu. | integration + negative-path e2e | Testowanie wyłącznie pozytywnych ścieżek authz. |

## 3. Phased Rollout

Każdy wiersz to osobna faza rolloutu z własnym change folderem.

| # | Phase name | Goal (one line) | Risks covered | Test types | Status | Change folder |
|---|---|---|---|---|---|---|
| 1 | Critical-path integrity | Objąć najtańszą sensowną ochroną niezawodność sesji i zapisu estymaty. | #1, #2, #5 | integration + targeted e2e | change opened | context/changes/testing-critical-path-integrity/ |
| 2 | Azure DevOps contract hardening | Ustabilizować jakość danych i mapowanie na granicy ADO. | #4, #2 | contract + integration | not started | — |
| 3 | Isolation & abuse hardening | Domknąć ryzyka autoryzacji/scope i negatywne ścieżki dostępu. | #6, #3 | integration + negative-path e2e | not started | — |
| 4 | Quality gates and selective AI-native checks | Zamknąć bramki jakości i dodać selektywne kontrole tam, gdzie dają sygnał. | cross-cutting | gates + selective AI-native review | not started | — |

## 4. Stack

| Layer | Tool | Version | Notes |
|---|---|---|---|
| unit + integration | NUnit + Microsoft.NET.Test.Sdk | NUnit 4.6.1 / Test SDK 18.7.0 | Znacząca baza testów (unit/integration) już istnieje. |
| e2e | Microsoft.Playwright.NUnit | 1.60.0 | E2E działają lokalnie przez Aspire lub zdalnie przez `BaseUrl`. |
| coverage | coverlet.collector | 10.0.1 | Dostępny w projektach testowych. |
| optional AI-native | Browser automation (Playwright tools) — checked: 2026-06-27 | n/a | Używać selektywnie dla krytycznych ekranów; nie zastępuje testów deterministycznych. |

**Stack grounding tools (current session):**
- Docs: none — brak dedykowanego docs MCP; checked: 2026-06-27
- Search: web/github search tools — dostępne do discovery i weryfikacji źródeł; checked: 2026-06-27
- Runtime/browser: Playwright browser tools — dostępne jako warstwa pomocnicza; checked: 2026-06-27
- Provider/platform: GitHub MCP tools — dostępne dla jakościowych przepływów repo/PR; checked: 2026-06-27

## 5. Quality Gates

| Gate | Where | Required? | Catches |
|---|---|---|---|
| build | local + CI | required | regressions kompilacji i integracji warstw |
| unit + integration | local + CI | required after §3 Phase 1 | regresje logiki i kontraktów aplikacyjnych |
| e2e on critical flows | CI on PR | required after §3 Phase 1 | uszkodzenia end-to-end (join/vote/reveal/save) |
| contract tests for ADO boundary | local + CI | required after §3 Phase 2 | drift mapowania danych i błędy granicy API |
| authz/abuse negative-path checks | local + CI | required after §3 Phase 3 | IDOR/scope leaks i błędy autoryzacji |
| post-edit hook | local (agent loop) | recommended after §3 Phase 4 | szybkie wykrycie regresji po edycjach |
| multimodal visual review | CI on PR | optional | wybrane regresje wizualne poza sygnałem deterministycznym |

## 6. Cookbook Patterns

How to add new tests in this project. Wzorce poniżej będą uzupełniane po dostarczeniu faz rolloutu.

### 6.1 Dodawanie testu integration dla przepływu sesji/głosowania

TBD — see §3 Phase 1 for disconnected-session and estimate-save protection pattern.

### 6.2 Dodawanie testu contract/integration dla granicy Azure DevOps

TBD — see §3 Phase 2 for import/write-back boundary pattern.

### 6.3 Dodawanie testu authz/abuse (negative path)

TBD — see §3 Phase 3 for tenant/guest scope denial pattern.

### 6.4 Dodawanie testu e2e dla krytycznego przepływu

TBD — see §3 Phase 1 for critical-path end-to-end pattern.

### 6.5 Selektywne AI-native kontrole jakości

TBD — see §3 Phase 4 for when to use vs when NOT to use browser/visual AI checks.

## 7. Negative Space (czego świadomie nie testujemy teraz)

- Brak explicit exclusions od użytkownika w Phase 2 (Q5 = „brak”).
- Tymczasowo nie finansujemy szerokich snapshotów UI bez wartości biznesowej.
- Test-budget priorytetowo idzie w ryzyka #1-#2-#6; obszary niskiego wpływu czekają na refresh.

## 8. Refresh Triggers

Uruchom `/10x-test-plan --refresh`, gdy wystąpi co najmniej jeden warunek:

- pojawia się nowe top-3 ryzyko z roadmapy lub wdrożonego slice’a,
- data `checked:` narzędzia w §4 jest starsza niż 3 miesiące,
- stack technologiczny zmienia granice testowania,
- §7 Negative Space przestaje odpowiadać realnym priorytetom zespołu.
