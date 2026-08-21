# Frame Brief: Awaria deploymentu GitHub Actions

> Framing step before implementation. This document separates the observed
> deployment failure from its initially unknown cause.

## Reported Observation

Ostatni workflow GitHub Actions `Deploy PlanDeck pilot (ACA)` zakończył się
błędem i deployment nie został wykonany.

## Initial Framing (preserved)

- **User's stated cause or approach**: Przyczyna nie została wskazana.
- **User's proposed direction**: Sprawdzić ostatni nieudany deploy i naprawić
  rzeczywisty błąd.
- **Pre-dispatch narrowing**: Workflow zakończył się błędem i deployment nie
  został wykonany.

## Dimension Map

Obserwacja mogła pochodzić z następujących warstw:

1. **Workflow i uwierzytelnienie** — checkout, SDK, azd albo OIDC mogły nie
   przygotować runnera do wykonania deploymentu.
2. **Generowanie manifestu Aspire** — AppHost mógł odczytać zasób lub endpoint
   niedostępny podczas publish/manifest mode.
3. **Provisioning Azure** — ARM/Bicep albo uprawnienia mogły odrzucić właściwe
   wdrożenie infrastruktury.
4. **Migracje lub deployment aplikacji** — późniejsze kroki mogły zatrzymać
   publikację po poprawnym provisioningu.

## Hypothesis Investigation

| Hypothesis | Evidence | Verdict |
| --- | --- | --- |
| Workflow/OIDC nie przygotował runnera | Kroki checkout, setup-dotnet, setup-azd, `azd auth login` i `azure/login` zakończyły się sukcesem w runie `30134353805`. | NONE |
| AppHost odczytuje endpoint za wcześnie | `azd provision` przerwał generowanie manifestu z `The endpoint https is not allocated`; stack wskazuje `AppHost.cs:77`. Kod używa `planDeckServer.GetEndpoint("https").Url` podczas budowy modelu aplikacji. | STRONG |
| Azure odrzucił provisioning | Błąd wystąpił przed wygenerowaniem manifestu i przed wysłaniem deploymentu do Azure. | NONE |
| Migracja lub deploy aplikacji zawiodły | Kroki firewall, migracji i `azd deploy` zostały pominięte po wcześniejszej awarii. | NONE |

## Narrowing Signals

- Awaria dotyczy kroku `Provision infrastructure`, ale zachodzi lokalnie na
  runnerze podczas `dotnet run --publisher manifest`.
- Wyjątek ma bezpośredni stack trace do odczytu endpointu w `AppHost.cs`.
- Ostatni udany commit nie odczytywał `.Url` endpointu w publish mode.

## Cross-System Convention

W modelu Aspire wartości zależne od alokacji endpointu nie mogą być materializowane
synchronicznie podczas generowania manifestu. Pozostałe ustawienia publish mode w
tym AppHost pochodzą z konfiguracji albo są deklarowane jako element modelu,
zamiast odczytywać `AllocatedEndpoint`.

## Reframed Problem Statement

> **The actual problem to plan around is**: AppHost materializuje URL endpointu
> `plandeck-server` przed jego alokacją podczas generowania manifestu Aspire.

Workflow, OIDC i Azure nie są źródłem awarii. Deployment zatrzymuje się lokalnie
na runnerze, ponieważ konfiguracja `EmailSettings__PublicBaseUrl` wywołuje `.Url`
na `EndpointReference` w publish mode.

## Confidence

- **HIGH** — komunikat wyjątku, stack trace, bieżący kod i porównanie z ostatnim
  udanym deploymentem wskazują tę samą przyczynę; alternatywne warstwy nie
  rozpoczęły jeszcze pracy albo zakończyły się sukcesem.

## What Changes for Implementation

Poprawka powinna dotyczyć wyłącznie sposobu deklarowania publicznego base URL w
modelu Aspire oraz regresyjnej walidacji generowania manifestu. Nie wymaga zmian
OIDC, uprawnień Azure ani migracji.

## References

- GitHub Actions run: `30134353805`
- `src/PlanDeck/Aspire/PlanDeck.AppHost/AppHost.cs:77`
- `.github/workflows/azure-dev.yml:57-74`
- Previous successful deployment commit: `8d527384`
- Investigation tasks: `deploy-apphost-hypothesis`,
  `deploy-workflow-hypothesis`

## Follow-up Deployment

Run `30135191412` potwierdził naprawę pierwotnej przyczyny: provisioning
zakończył się sukcesem. Następny krok zatrzymał się na migracji, ponieważ baza
`rg-test` zawierała tabele ze starej historii migracji, a bieżący kod rozpoczyna
historię od nowego `20260724073135_InitialCreate`.

Decyzja operacyjna: środowisko testowe rozpoczyna od zera. Ręczne uruchomienie
workflow z `reset_database=true` usuwa wszystkie tabele użytkownika przed
zastosowaniem bieżącego `InitialCreate`; reset nie wykonuje się przy zwykłym
pushu.
