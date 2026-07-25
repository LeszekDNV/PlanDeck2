# Frame Brief: Automatyczne logowanie Test Owner w rg-test

> Framing step before /10x-plan. This document captures what is actually
> at issue, separated from what was initially assumed.

## Reported Observation

Po wejściu na
`https://plandeck-server.wittymeadow-96369440.polandcentral.azurecontainerapps.io/projects`,
również w nowym oknie incognito bez cookies, użytkownik jest automatycznie
zalogowany jako `Test Owner` zamiast zobaczyć okno logowania.

## Initial Framing (preserved)

- **User's stated cause or approach**: Nie wskazano przyczyny; oczekiwane jest
  standardowe logowanie.
- **User's proposed direction**: Ustalić, dlaczego środowisko `rg-test`
  automatycznie loguje jako `Test Owner`.
- **Pre-dispatch narrowing**: Zachowanie występuje również w incognito bez
  zapisanych cookies.

## Dimension Map

1. **Aktywna rewizja ACA** — publiczny URL może nadal obsługiwać starszy obraz.
2. **Backend authentication** — bieżący backend mógłby tworzyć principal bez
   sesji.
3. **Frontend identity state** — klient mógłby wyświetlać statyczną personę
   niezależnie od backendu.

## Hypothesis Investigation

| Hypothesis | Evidence | Verdict |
| --- | --- | --- |
| Publiczny URL serwuje starą rewizję | `0000024` ma stan `Unhealthy/ActivationFailed`, a `0000023` pozostaje `Healthy/RunningAtMaxScale`. Odpowiedź `/projects` ma `Last-Modified: Thu, 23 Jul 2026 19:21:11 GMT`. | STRONG |
| Bieżący backend tworzy Test Owner | Aktualny kod nie zawiera `TestAuthenticationHandler` ani tekstu `Test Owner`. | NONE |
| Frontend tworzy Test Owner | Aktualny klient nie zawiera `Test Owner`, `TestOwner` ani `UseTestScheme`. | NONE |

## Narrowing Signals

- Incognito wyklucza istniejące cookie członkowskie.
- Rewizja `0000023` ma `ASPNETCORE_ENVIRONMENT=Testing` oraz
  `Authentication__UseTestScheme=true`.
- Handler z obrazu `8d527384` wybiera `TestMemberIdentities.Owner`, gdy cookie
  `e2e-user` nie istnieje.
- Rewizja `0000024` wpada w `CrashLoopBackOff`, ponieważ puste są
  `Authentication:Microsoft:TenantId`, `ClientId` i `ClientSecret`.

## Cross-System Convention

Azure Container Apps nie przełącza skutecznie ruchu na rewizję, która nie jest
gotowa. Zdrowa starsza rewizja może nadal odpowiadać, co w tym przypadku
zachowuje wcześniejszy testowy mechanizm logowania.

## Reframed Problem Statement

> **The actual problem to plan around is**: najnowsza rewizja produkcyjnego
> backendu nie startuje bez konfiguracji Entra, dlatego publiczny adres nadal
> odpowiada ze starej rewizji `0000023`, której testowy handler domyślnie
> uwierzytelnia brak cookie jako `Test Owner`.

To nie jest problem sesji przeglądarki ani bieżącego frontendu. Automatyczne
logowanie jest oczekiwanym zachowaniem starego obrazu testowego, który nadal
pozostaje jedyną zdrową rewizją.

## Confidence

- **HIGH** — stan rewizji, log `CrashLoopBackOff`, zmienne środowiskowe starej
  rewizji i kod jej handlera wskazują ten sam łańcuch przyczynowy.

## What Changes for /10x-plan

Plan powinien dotyczyć uruchomienia nowej rewizji z właściwym trybem
uwierzytelniania dla `rg-test` i potwierdzenia, że ruch nie wraca do obrazu z
testowym handlerem.

## References

- ACA revision `plandeck-server--0000023`
- ACA revision `plandeck-server--0000024`
- `src/PlanDeck/Web/PlanDeck.Server/Extensions/ServiceCollectionExtensions.cs:69-81`
- `src/PlanDeck/Web/PlanDeck.Server/Program.cs:58`
- Historical commit `8d527384`:
  `Web/PlanDeck.Server/Identity/TestAuthenticationHandler.cs`
- Investigation tasks: `backend-auto-login`, `frontend-auto-login`
