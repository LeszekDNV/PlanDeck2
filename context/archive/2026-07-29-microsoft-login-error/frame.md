# Frame Brief: Microsoft login failure in Testing

> Framing step before /10x-plan. This document captures what is *actually*
> at issue, separated from what was initially assumed.

## Reported Observation

Na środowisku testowym akcje "Sign in with a Microsoft account" oraz
"Create account with Microsoft" zwracaja blad aplikacji 4xx/5xx. Lokalnie w
srodowisku Development oba przeplywy dzialaja poprawnie.

## Initial Framing (preserved)

- **User's stated cause or approach**: W konfiguracji srodowiska testowego moze brakowac wartosci wymaganych przez Microsoft Entra.
- **User's proposed direction**: Zbadac konfiguracje srodowiska testowego i ustalic, czego brakuje.
- **Pre-dispatch narrowing**: Po kliknieciu aplikacja zwraca blad 4xx/5xx, a nie blad wyswietlany przez Microsoft.

## Dimension Map

The observation could originate at any of these dimensions:

1. **Client and server routing** — akcja moglaby kierowac do nieistniejacej lub niewlasciwej trasy.
2. **Deployment inputs** — workflow moglby nie dostarczac wartosci aplikacji Entra oczekiwanych przez AppHost. <- initial framing
3. **ACA revision activation** — nowa rewizja moglaby uruchamiac sie bez wymaganej konfiguracji i mimo to otrzymac ruch.
4. **Entra app registration** — aplikacja moglaby nie miec redirect URI odpowiadajacego publicznemu hostowi testowemu.

## Hypothesis Investigation

| Hypothesis | Evidence | Verdict |
| --- | --- | --- |
| Client or server route is wrong | Endpointy sa mapowane warunkowo w `AccountEndpointExtensions.cs:225-293`; lokalny Development wykonuje challenge poprawnie. | NONE |
| Deployment lacks Entra application inputs | Oba workflowy przekazuja tylko `AZURE_CLIENT_ID` i `AZURE_TENANT_ID` tozsamosci pipeline'u (`.github/workflows/azure-dev.yml:45-54`, `.github/workflows/azure-develop.yml:41-50`). AppHost oczekuje osobnych `AZURE_ENTRA_TENANT_ID`, `AZURE_ENTRA_CLIENT_ID`, `AZURE_ENTRA_CLIENT_SECRET`, po czym zamienia brak na puste wartosci (`AppHost.cs:89-103`). Repozytorium nie ma zmiennych `AZURE_ENTRA_*`. | STRONG |
| Active ACA revision starts with invalid configuration | Rewizja `plandeck-server--0000027` ma 100% ruchu i stan Unhealthy. Jej TenantId, ClientId i ClientSecret sa puste, a Required ma wartosc true. Log startowy konczy proces w `MicrosoftAuthenticationOptions.Validate()` komunikatem o brakujacej konfiguracji. | STRONG |
| Test Entra registration or redirect URI is missing | W dostepnym tenantcie nie znaleziono rejestracji z `https://plandeck-server.wittymeadow-96369440.polandcentral.azurecontainerapps.io/signin-oidc`. `PlanDeck (dev)` zawiera tylko `https://localhost:7443/signin-oidc`; `plandeck-pipeline-oidc` nie ma web redirect URI. | STRONG |

## Narrowing Signals

- HTTP 500 pojawia sie przed przekierowaniem do `login.microsoftonline.com`, wiec blad nie pochodzi z interakcji uzytkownika z Microsoftem.
- Aktywna rewizja nie przechodzi startu aplikacji, poniewaz `Required=true` poprawnie odrzuca trzy puste wartosci.
- GitHub Actions zakonczyl sie sukcesem, ale workflow konczy sie po `azd deploy` i nie sprawdza zdrowia nowej rewizji (`azure-develop.yml:170-172`).
- Zdrowa rewizja `0000026` pozostaje aktywna, lecz ma 0% ruchu; rewizja `0000027` jest Unhealthy i otrzymuje 100% ruchu.

## Cross-System Convention

Tozsamosc deploymentu GitHub OIDC i aplikacja logowania uzytkownikow to dwa
odrebne obiekty Entra. Dane `AZURE_CLIENT_ID`/`AZURE_TENANT_ID` pipeline'u nie
powinny byc traktowane jako konfiguracja OIDC aplikacji. Aplikacja webowa
potrzebuje osobnej rejestracji, sekretu oraz jawnego redirect URI dla kazdego
publicznego hosta. Deployment powinien rowniez odrzucic rewizje, ktora nie
osiagnela gotowosci.

## Reframed Problem Statement

> **The actual problem to plan around is**: Testing nie ma kompletnej, odrebnej konfiguracji aplikacji Microsoft Entra ani testowego redirect URI, a pipeline publikuje puste wartosci jako wymagane i nie wykrywa, ze nowa rewizja ACA nie uruchomila sie.

Pierwotna hipoteza o brakujacej konfiguracji byla poprawna, ale zakres jest
szerszy niz jedna zmienna. Brakuje calego kontraktu deploymentowego dla
aplikacji Entra oraz bramki gotowosci po wdrozeniu. Kod aplikacji zachowuje sie
zgodnie z zalozeniem fail-fast i ujawnia ten brak podczas startu.

## Confidence

- **HIGH** — log startowy, szablon aktywnej rewizji, konfiguracja workflow oraz
  niezalezne sprawdzenie rejestracji Entra wskazuja ten sam lancuch przyczynowy.

## What Changes for /10x-plan

Plan powinien dotyczyc kompletnego kontraktu Entra dla Testing: osobnej
rejestracji aplikacji webowej, bezpiecznego dostarczenia trzech wartosci do
AppHost oraz kontroli gotowosci rewizji po deploymentcie. Nie nalezy planowac
zmian w przyciskach ani trasach, ktore sa juz poprawne.

## References

- Source files: `.github/workflows/azure-dev.yml:45-54,174-176`, `.github/workflows/azure-develop.yml:41-50,170-172`, `src/PlanDeck/Aspire/PlanDeck.AppHost/AppHost.cs:89-103`, `src/PlanDeck/Web/PlanDeck.Server/Identity/MicrosoftAuthenticationOptions.cs:19-33`, `src/PlanDeck/Web/PlanDeck.Server/Extensions/ServiceCollectionExtensions.cs:70-99`
- Runtime: ACA revisions `plandeck-server--0000026` and `plandeck-server--0000027`
- Entra registration: `PlanDeck (dev)` has only `https://localhost:7443/signin-oidc`
- Investigation tasks: `deployment-config-check`, `entra-registration-check`
