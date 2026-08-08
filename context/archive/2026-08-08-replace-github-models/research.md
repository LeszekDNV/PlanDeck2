---
date: 2026-08-08T16:13:01.4842309+02:00
researcher: GitHub Copilot
git_commit: 58a4a8c78a0941bf2ac42ebf3d4bef39d8e51260
branch: docs/project-readme
repository: LeszekDNV/PlanDeck2
topic: "Replace GitHub Models with automatic GitHub Copilot code review on pull requests"
tags: [research, codebase, github-copilot, code-review, github-actions, rulesets]
status: complete
last_updated: 2026-08-08
last_updated_by: GitHub Copilot
---

# Research: Replace GitHub Models with automatic GitHub Copilot code review on pull requests

**Date**: 2026-08-08T16:13:01.4842309+02:00
**Researcher**: GitHub Copilot
**Git Commit**: 58a4a8c78a0941bf2ac42ebf3d4bef39d8e51260
**Branch**: docs/project-readme
**Repository**: LeszekDNV/PlanDeck2

## Research Question

Jak zastapic wycofywana integracje GitHub Models automatycznym code review
podpietym do pull requestow, zgodnie z lekcja "Code Review w erze AI:
standardy, DoD i Agent w pipeline", przy zalozeniu, ze preferowanym rozwiazaniem
jest natywny GitHub Copilot Code Review bez wlasnego klucza do modelu?

## Summary

GitHub Copilot Code Review jest dobrym zamiennikiem dla warstwy, ktora czyta
pull request i publikuje komentarze. Mozna wlaczyc je bez workflow i bez klucza
API przez branch ruleset, zaznaczajac:

- **Automatically request Copilot code review**;
- **Review new pushes**;
- opcjonalnie **Review draft pull requests**;
- poziom analizy **Balanced** dla repozytorium z wymaganiami
  architektonicznymi i bezpieczenstwa.

Nie jest to jednak zamiennik 1:1 dla obecnego pipeline'u. Copilot zawsze publikuje
review typu `Comment`, nigdy `Approve` ani `Request changes`. Nie udostepnia
ustrukturyzowanego wyniku, status checka, deterministycznego werdyktu ani
natywnych etykiet `passed`/`failed`. Nie moze wiec bez dodatkowego systemu
utrzymac dotychczasowej semantyki `ai-cr:passed`, `ai-cr:failed` i
`ai-cr:error`, ani blokowac merge.

Rekomendowany MVP jest dwuwarstwowy:

1. **Copilot Code Review jako recenzent doradczy** - automatyczny review na PR
   do `develop`, ponawiany po kazdym pushu, korzystajacy z istniejacych
   instrukcji repozytorium i nowego skilla z kryteriami Definition of Done.
2. **Deterministyczne GitHub Actions jako merge gate** - build, testy i inne
   obiektywne kontrole pozostaja required status checks. Werdykt AI nie jest
   bramka, bo natywny Copilot nie oferuje stabilnego kontraktu maszynowego.

To realizuje najwazniejsza zasade z lekcji: zaczac od gotowego agenta i zejsc do
wlasnej petli dopiero wtedy, gdy ograniczenia gotowca sa rzeczywistym problemem.

## Detailed Findings

### Current Repository State

Repozytorium jest publiczne, a jego domyslna galaz to `develop`. Istnieja dwa
aktywne rulesety:

- `Protect develop` dla `refs/heads/develop`;
- `Protect main` dla `refs/heads/main`.

Oba rulesety wymagaja pull requesta, ale obecnie:

- nie maja reguly automatycznego Copilot code review;
- wymagaja `0` zatwierdzen;
- nie wymagaja rozwiazania watkow review;
- nie deklaruja required status checks.

Repozytorium ma juz etykiety `ai-cr:review`, `ai-cr:passed`, `ai-cr:failed` i
`ai-cr:error`.

Pliki starego pipeline'u sa obecne w `HEAD`, lecz w bieżącym working tree sa
oznaczone jako usuniete. To odpowiada kierunkowi zmiany, ale usuniecie nie
powinno utracic wypracowanej polityki review. Wersja plikow w `HEAD` jest
identyczna z wdrozonym commitem
`e9b3628ba12918cd5852b2e38f6aa4e3a026290e`.

### Existing GitHub Models Pipeline

Stary workflow jest uruchamiany przez `pull_request_target` dla niedraftowych
PR-ow do `develop`, reaguje na otwarcie, push, ponowne otwarcie, przejscie do
ready-for-review i etykiete `ai-cr:review`
([workflow:3-18](https://github.com/LeszekDNV/PlanDeck2/blob/e9b3628ba12918cd5852b2e38f6aa4e3a026290e/.github/workflows/ai-code-review.yml#L3-L18)).

Ma poprawnie rozdzielone granice uprawnien:

- job `review`: `contents: read`, `models: read`;
- job `publish`: `contents: read`, `issues: write`,
  `pull-requests: write`
  ([workflow:20-22](https://github.com/LeszekDNV/PlanDeck2/blob/e9b3628ba12918cd5852b2e38f6aa4e3a026290e/.github/workflows/ai-code-review.yml#L20-L22),
  [workflow:65-68](https://github.com/LeszekDNV/PlanDeck2/blob/e9b3628ba12918cd5852b2e38f6aa4e3a026290e/.github/workflows/ai-code-review.yml#L65-L68)).

Zewnetrzne akcje sa przypiete do konkretnych SHA, zgodnie z zasada ograniczonego
zaufania z lekcji
([workflow:26-30](https://github.com/LeszekDNV/PlanDeck2/blob/e9b3628ba12918cd5852b2e38f6aa4e3a026290e/.github/workflows/ai-code-review.yml#L26-L30),
[workflow:50-56](https://github.com/LeszekDNV/PlanDeck2/blob/e9b3628ba12918cd5852b2e38f6aa4e3a026290e/.github/workflows/ai-code-review.yml#L50-L56)).

Composite action deklaruje model `openai/gpt-4.1-mini`, budzety tokenow i
ustrukturyzowane outputs
([action.yml:1-31](https://github.com/LeszekDNV/PlanDeck2/blob/e9b3628ba12918cd5852b2e38f6aa4e3a026290e/.github/actions/ai-code-review/action.yml#L1-L31)).
Runner:

- pobiera head PR jako dane i weryfikuje SHA;
- buduje ograniczony diff;
- redaguje potencjalne sekrety;
- laduje zaufana polityke, prompt i JSON Schema;
- wywoluje `https://models.github.ai/inference/chat/completions`;
- wymusza `response_format: json_schema`;
- waliduje wynik zaufanym skryptem
  ([review.ps1:125-183](https://github.com/LeszekDNV/PlanDeck2/blob/e9b3628ba12918cd5852b2e38f6aa4e3a026290e/.github/actions/ai-code-review/review.ps1#L125-L183),
  [review.ps1:245-290](https://github.com/LeszekDNV/PlanDeck2/blob/e9b3628ba12918cd5852b2e38f6aa4e3a026290e/.github/actions/ai-code-review/review.ps1#L245-L290),
  [review.ps1:320-344](https://github.com/LeszekDNV/PlanDeck2/blob/e9b3628ba12918cd5852b2e38f6aa4e3a026290e/.github/actions/ai-code-review/review.ps1#L320-L344)).

Validator wymaga kompletnej analizy, braku blockerow i wyniku co najmniej 7 dla
kazdego kryterium
([validator:98-107](https://github.com/LeszekDNV/PlanDeck2/blob/e9b3628ba12918cd5852b2e38f6aa4e3a026290e/.github/actions/ai-code-review/validate-review-result.ps1#L98-L107)).
Publisher aktualizuje jeden komentarz markerowy, sprawdza nieaktualny SHA i
zarzadza etykietami addytywnie
([publisher:19-25](https://github.com/LeszekDNV/PlanDeck2/blob/e9b3628ba12918cd5852b2e38f6aa4e3a026290e/.github/actions/ai-code-review/publish-review.ps1#L19-L25),
[publisher:350-405](https://github.com/LeszekDNV/PlanDeck2/blob/e9b3628ba12918cd5852b2e38f6aa4e3a026290e/.github/actions/ai-code-review/publish-review.ps1#L350-L405)).

Wniosek: provider jest mocno zwiazany tylko z `action.yml`, `review.ps1` i
workflow. Polityka, schema, validator oraz publisher sa niezalezne od modelu,
ale natywny Copilot nie ma interfejsu pozwalajacego ich dalej wykonywac.

### Native Copilot Code Review Capabilities

GitHub oficjalnie wspiera automatyczne code review przez repository ruleset.
Opcja **Review new pushes** uruchamia ponowny review po kazdym pushu. Drafty sa
osobna, opcjonalna decyzja:

- [Configuring automatic code review by GitHub Copilot](https://docs.github.com/en/copilot/how-tos/copilot-on-github/set-up-copilot/configure-automatic-review)
- [Using GitHub Copilot code review](https://docs.github.com/en/copilot/how-tos/use-copilot-agents/request-a-code-review/use-code-review)

Copilot potrafi korzystac z:

- `.github/copilot-instructions.md`;
- path-specific `.github/instructions/**/*.instructions.md`;
- `AGENTS.md`;
- agent skills w `.github/skills`;
- repozytoryjnych MCP servers.

Dla review GitHub rekomenduje nazwe skilla wskazujaca przeznaczenie, np.
`.github/skills/code-review`. Istniejace instrukcje repozytorium juz dokumentuja
stack, warstwy, gRPC, DI, MudBlazor, Razor code-behind, lokalizacje i obowiazek
pelnego buildu (`.github/copilot-instructions.md:1-87`). Brakuje w nich jednak
jawnej instrukcji, aby podczas review stosowac 15 kryteriow z poprzedniej
polityki.

Copilot Code Review uzywa specjalnie dostrojonej mieszanki modeli. Nie mozna
wybrac modelu ani uzyskac surowej odpowiedzi JSON:

- [About GitHub Copilot code review](https://docs.github.com/en/copilot/concepts/agents/code-review)

### Critical Product Limitation

GitHub dokumentuje wprost, ze Copilot zawsze pozostawia review typu `Comment`.
Nie wystawia `Approve` ani `Request changes`, nie liczy sie do wymaganych
zatwierdzen i nie blokuje merge:

- [Using GitHub Copilot code review - step 4](https://docs.github.com/en/copilot/how-tos/use-copilot-agents/request-a-code-review/use-code-review)

Konsekwencje:

| Existing requirement | Native Copilot |
| --- | --- |
| Automatic PR comment and inline findings | Supported |
| Automatic review after every push | Supported through ruleset |
| Repository-specific DoD | Supported as probabilistic instructions/skill |
| Manual re-review | Supported in UI and through reviewer-request REST API |
| `ai-cr:passed` / `ai-cr:failed` | Not supported |
| Structured JSON result | Not supported |
| Trusted deterministic verdict | Not supported |
| AI-based required status check | Not supported |
| Model selection and promptfoo model comparison | Not supported |

Nie nalezy parsowac komentarzy Copilota w celu odtworzenia `passed`/`failed`.
Format komentarza nie jest publicznym kontraktem, a brak komentarza nie oznacza
pozytywnego werdyktu. Taki parser bylby kruchy i moglby stworzyc falszywa bramke
bezpieczenstwa.

### Recommended Target Architecture

#### 1. Enable Native Automatic Review

Rozszerzyc ruleset `Protect develop`:

- `Automatically request Copilot code review`: enabled;
- `Review new pushes`: enabled;
- `Review draft pull requests`: disabled, zgodnie z dotychczasowym workflow;
- review effort: `Balanced`.

Ruleset `Protect main` moze otrzymac te sama regule tylko wtedy, gdy bezposrednie
PR-y do `main` sa wspieranym przeplywem. Dotychczasowy pipeline reviewowal
wylacznie `develop`, wiec MVP powinien zachowac ten zakres.

Ta konfiguracja jest ustawieniem repozytorium GitHub, a nie plikiem workflow.

#### 2. Migrate the Definition of Done into a Review Skill

Nie usuwac wiedzy z dotychczasowego `review-policy.md`. Przeniesc kryteria do
review-focused skilla, np.:

```text
.github/skills/code-review/
  SKILL.md
  references/
    review-policy.md
```

`SKILL.md` powinien:

- nakazac stosowanie 15 kryteriow i blocker overrides;
- priorytetyzowac konkretne, wysokiej pewnosci findingi;
- wymagac odnoszenia uwag do zmienionych plikow;
- traktowac build i testy jako osobne, deterministyczne sygnaly CI;
- nie probowac wystawiac `Approve`, `Request changes` ani merge decision;
- wskazywac istniejace `.github/copilot-instructions.md` jako kontekst
  architektoniczny.

Do `.github/copilot-instructions.md` dodac krotka instrukcje, aby przy code
review stosowac ten skill. Nie nalezy kopiowac wszystkich 15 kryteriow do
instrukcji ogolnych.

#### 3. Retire the Provider-Specific Pipeline

Usunac:

- `.github/workflows/ai-code-review.yml`;
- `.github/actions/ai-code-review/action.yml`;
- `review.ps1`;
- `review-prompt.md`;
- `review-result.schema.json`;
- `validate-review-result.ps1`;
- fixtures;
- `publish-review.ps1`;
- provider-specific README.

Biezacy working tree juz zawiera te usuniecia. Przed finalizacja nalezy jednak
przeniesc polityke review do skilla.

Po usunieciu publishera etykiety `ai-cr:passed`, `ai-cr:failed` i `ai-cr:error`
stana sie stale i mylace. Nalezy usunac je z repozytorium albo wyraznie
zarchiwizowac. Nie wolno pozostawic ich jako sygnalu merge.

#### 4. Keep Retry Only If It Adds Value

`Review new pushes` automatycznie obsluguje normalny retry po poprawkach.
Manualny retry jest dostepny w UI.

Jesli zespol chce zachowac etykiete `ai-cr:review`, maly workflow moze reagowac
na `pull_request_target:labeled`, bez checkoutu i bez sekretow modelu:

1. sprawdzic, czy etykieta to `ai-cr:review`;
2. wywolac REST API request reviewers z
   `copilot-pull-request-reviewer[bot]`;
3. usunac etykiete, aby mozna bylo uzyc jej ponownie.

API jest oficjalnie wspierane:

- [REST API endpoints for review requests](https://docs.github.com/en/rest/pulls/review-requests#request-reviewers-for-a-pull-request)

Jest to opcja, nie wymaganie MVP. Bez potrzeby biznesowej prostszy ruleset i
manualny przycisk re-review sa zgodne z KISS/YAGNI.

#### 5. Separate Advisory AI from Merge Gates

Copilot review powinien pozostac doradczy. Merge gate powinien opierac sie na
deterministycznych checkach:

- `dotnet build PlanDeck.slnx`;
- unit/integration tests;
- uzgodnione analizatory i progi;
- wymagane zatwierdzenie czlowieka dla zmian wysokiego ryzyka.

Obecne rulesety nie wymagaja zadnego approval ani status checka. To luka
governance niezalezna od migracji providera. Dodanie required checks powinno
byc osobna, jawna decyzja, poniewaz moze zablokowac merge przy problemach z CI.

### Security and Trust Boundaries

Natywny Copilot nie potrzebuje sekretu providera ani `models: read`, co usuwa
najbardziej wrazliwa czesc starego workflow.

Pozostaje wazne ograniczenie: podczas review Copilot czyta custom instructions,
`AGENTS.md` i skills z **head branch PR**, a nie z zaufanej base branch. W
publicznym repozytorium autor PR moze wiec zmienic instrukcje obowiazujace dla
wlasnego review. Poniewaz Copilot review jest tylko komentarzem, nie powinno to
tworzyc bramki bezpieczenstwa.

Jesli kryteria w `.github/**` maja byc elementem kontroli procesu, nalezy
dodatkowo rozwazyc:

- CODEOWNERS dla `.github/copilot-instructions.md` i `.github/skills/**`;
- wymagane zatwierdzenie czlowieka;
- deterministyczne required checks niezalezne od LLM.

### Cost and Availability

Copilot Code Review zuzywa AI credits, a funkcje agentowe moga korzystac z
GitHub Actions runners. Poziom `Balanced` zuzywa wiecej kredytow i czasu niz
`Lite`.

Przed implementacja trzeba potwierdzic, ze konto/repozytorium ma plan
obejmujacy Copilot Code Review i budzet AI credits. Dla indywidualnego
auto-review dokumentacja wymienia Copilot Pro, Pro+ lub Max. Dla organizacji
dostep jest zarzadzany politykami Business/Enterprise.

Copilot Coding Agent nie jest zamiennikiem dla Code Review. Coding Agent tworzy
i modyfikuje kod, natomiast Copilot Code Review analizuje istniejacy PR i
publikuje komentarze.

## Code References

- `.github/workflows/ai-code-review.yml:3-96` - stary trigger, granice
  uprawnien, model i publikowanie wyniku.
- `.github/actions/ai-code-review/action.yml:1-49` - provider-specific
  composite action i jego kontrakt.
- `.github/actions/ai-code-review/review.ps1:125-344` - bezpieczne zebranie
  diffu, GitHub Models API i structured output.
- `.github/actions/ai-code-review/validate-review-result.ps1:16-107` -
  zaufany zestaw kryteriow i deterministyczny werdykt.
- `.github/actions/ai-code-review/publish-review.ps1:19-437` - komentarz,
  etykiety, stale-result guard i retry.
- `.github/copilot-instructions.md:1-87` - istniejacy kontekst stacku,
  architektury i jakosci.
- `context/archive/2026-07-28-ci-cd-code-review/requirements.md:1-192` -
  pierwotne wymagania, 15 kryteriow, scoring i side effects.
- `context/archive/2026-07-28-ci-cd-code-review/research.md:66-239` -
  pierwotne granice bezpieczenstwa, model boundary i publishing.

## Architecture Insights

- Najtrwalszym zasobem starego rozwiazania jest polityka review, nie integracja
  z providerem. Powinna zostac przeniesiona do przenosnego skilla.
- Natywne Copilot Code Review redukuje kod utrzymaniowy, usuwa sekret/model API
  i eliminuje potrzebe wlasnego publishera.
- Cena tej prostoty to utrata kontroli nad modelem, schema output i werdyktem.
- Komentarze AI i deterministyczne CI rozwiazuja dwa rozne problemy. Laczenie
  ich przez parsowanie komentarzy byloby bledem architektonicznym.
- Ruleset jest poprawnym miejscem do wlaczenia auto-review; workflow jest
  potrzebny tylko dla opcjonalnego retry przez etykiete lub obiektywnych checkow
  CI.
- W publicznym repozytorium instrukcje z PR head sa niezaufane, dlatego AI
  review nie powinien samodzielnie sterowac merge.

## Historical Context (from Prior Changes)

- `context/archive/2026-07-28-ci-cd-code-review/requirements.md` zdefiniowal
  15 kryteriow, komentarz PR, etykiety pass/fail i retry przez `ai-cr:review`.
- `context/archive/2026-07-28-ci-cd-code-review/research.md` zaprojektowal
  fork-safe `pull_request_target`, rozdzielone permissions i fail-closed
  structured output.
- `context/archive/2026-07-28-ci-cd-code-review/plan.md` rozlozyl wdrozenie na
  kontrakt, composite action, workflow/publisher i rollout.
- Pipeline zostal wdrozony w commitach:
  - `80d72a5` - trusted review contract;
  - `10245f6` - fork-safe composite action;
  - `142be3c` - PR comments, labels i retry;
  - `9a4a2ab` - rollout na `develop`;
  - pozniejsze poprawki zakonczone przez `e9b3628`.
- Liczne poprawki API, schematu i normalizacji wynikow pokazuja koszt
  utrzymania wlasnej integracji. Natywny Copilot przenosi ten koszt do produktu
  GitHub, ale nie oferuje identycznego kontraktu.

## Related Research

- `context/archive/2026-07-28-ci-cd-code-review/research.md`
- `context/archive/2026-07-28-ci-cd-code-review/plan.md`
- `context/archive/2026-07-28-ci-cd-code-review/requirements.md`
- `context/foundation/test-plan.md`

## Open Questions

1. Czy konto ma plan i budzet AI credits pozwalajacy wlaczyc automatyczny
   Copilot Code Review dla tego repozytorium?
2. Czy auto-review ma dotyczyc tylko `develop`, czy rowniez bezposrednich PR-ow
   do `main`?
3. Czy zachowac `ai-cr:review` i maly workflow retry, czy wystarcza
   `Review new pushes` plus reczny re-review w UI?
4. Czy akceptujemy, ze natywny Copilot jest wylacznie doradczy i nie odtwarza
   `ai-cr:passed`/`ai-cr:failed`?
5. Ktore build/test checks maja stac sie required status checks w rulesecie
   `Protect develop`?
6. Czy dodac CODEOWNERS i wymagane human approval dla zmian w
   `.github/copilot-instructions.md` oraz `.github/skills/**`?
