---
change_id: create-local-account
title: Lokalne konta użytkowników i logowanie przez Entra ID
status: planned
created: 2026-07-23
updated: 2026-07-23
research: context/changes/create-local-account/research.md
---

# Plan: Lokalne konta użytkowników i logowanie przez Entra ID

## Goal

Zastąpić obecną tożsamość opartą wyłącznie na claimach Entra i testowych obejściach jednym modelem konta użytkownika, który:

- obsługuje lokalną rejestrację i logowanie nazwą użytkownika albo adresem e-mail,
- obsługuje logowanie i rejestrację przez organizacyjne konta Entra ID z dowolnego tenanta,
- pozwala jawnie połączyć zalogowane konto lokalne z kontem Entra,
- wymaga potwierdzenia e-maila, wspiera reset hasła i blokadę konta,
- przypisuje konto dokładnie do jednego tenanta PlanDeck,
- umożliwia testom integracyjnym i E2E używanie tych samych publicznych ścieżek co użytkownicy,
- usuwa testowe schematy uwierzytelniania, seedery i scenario endpoints.

## Scope and decisions

- Jedno konto PlanDeck należy do dokładnie jednego `PlanDeckTenant`.
- Rejestracja bez zaproszenia tworzy nowy tenant i profil właściciela.
- Rejestracja z ważnym zaproszeniem tworzy profil członka we wskazanym tenantcie.
- Istniejącego konta nie można przenieść do innego tenanta przez zaproszenie; zwracany jest błąd `account_tenant_conflict`.
- Nazwa użytkownika i e-mail są globalnie unikalne, niezależnie od tenanta.
- Nazwa użytkownika ma 3–32 znaki i nie może zawierać `@`; identyfikator logowania zawierający `@` jest traktowany jako e-mail.
- Potwierdzenie e-maila jest wymagane przed pierwszym pełnym dostępem do aplikacji.
- Reset hasła odbywa się przez jednorazowy token wysłany e-mailem.
- Publiczna rejestracja jest domyślnie włączona i może zostać wyłączona konfiguracją.
- Logowanie, rejestracja i linkowanie Entra są osobnymi, chronionymi intencjami.
- Konto Entra nigdy nie jest automatycznie łączone z istniejącym kontem na podstawie e-maila.
- Linkowanie Entra jest dostępne tylko dla zalogowanego użytkownika po ponownym podaniu poprawnego hasła lokalnego.
- Aplikacja używa jednego `PlanDeckDbContext` dla Identity i danych domenowych, dzięki czemu provisioning konta jest transakcyjny.
- Baza nie zawiera danych produkcyjnych: stare migracje i snapshot zostaną usunięte, a docelowy model otrzyma jedną nową migrację bazową bez backfillu i trybu zgodności.
- SMTP jest dostępne przez abstrakcję Identity `IEmailSender<ApplicationUser>`; lokalnie używany jest MailPit, a środowiska wdrożone dostarczają konfigurację zewnętrznego SMTP/provider.
- Testy nie otrzymują alternatywnego schematu auth ani prywatnych endpointów do seedowania. Potwierdzenie e-maila odczytują z kontrolowanej skrzynki testowej poza aplikacją.

## Target architecture

### Identity and tenant model

- `ApplicationUser : IdentityUser<Guid>` w `PlanDeck.Infrastructure` przechowuje globalną tożsamość, hash hasła, security stamp, lockout, potwierdzenie e-maila i zewnętrzne loginy.
- `AppUser` w `PlanDeck.Application` pozostaje tenantowym profilem domenowym. Jego `Id` jest jednocześnie kluczem obcym 1:1 do `ApplicationUser.Id`.
- `AppUser` przechowuje `TenantId`, imię, nazwisko, status aktywności i rolę tenantową `Owner` albo `Member`; nie duplikuje hasła, username ani znormalizowanego e-maila.
- `PlanDeckTenant` jest jawną encją nadrzędną wszystkich danych tenantowych.
- `TenantInvitation` przechowuje tylko hash tokenu, znormalizowany e-mail, tenant, czas wygaśnięcia, status i dane audytowe. Surowy token istnieje wyłącznie w wiadomości.
- Standardowa tabela loginów Identity przechowuje Entra pod providerem `MicrosoftEntra` i kluczem `"{tid}:{oid}"`.
- `PlanDeckDbContext` dziedziczy z `IdentityUserContext<ApplicationUser, Guid>` i zawsze wywołuje `base.OnModelCreating`.
- Encje Identity nie otrzymują tenantowego query filter. Encje domenowe zachowują fail-closed filtr po `TenantId`.
- Provisioning pierwszego tenanta używa wąskiego, jawnego scope'u infrastruktury, który ustawia tenant wyłącznie na czas jednej transakcji. Nie powstaje ogólny mechanizm omijania filtrów.

### Authentication boundary

- Cookie członka pozostaje jedynym schematem używanym przez chronione UI i gRPC.
- Principal jest budowany z `ApplicationUser` i aktywnego `AppUser`, a aplikacyjne claimy to co najmniej:
  - `plandeck_user_id`,
  - `plandeck_tenant_id`,
  - `plandeck_tenant_role`,
  - provider-neutral participant identifier oparty na `ApplicationUser.Id`.
- Autoryzacja nie wymaga już claimów Entra `tid` ani `oid`.
- Cookie jest unieważniane po zmianie hasła, odłączeniu loginu zewnętrznego, dezaktywacji profilu albo usunięciu konta.
- Lokalny login używa jednego komunikatu błędu dla nieistniejącego konta, złego hasła i niepotwierdzonego e-maila; lockout i rate limiting ograniczają zgadywanie.
- Wszystkie mutujące operacje auth są `POST`, chronione antiforgery i walidują lokalny `returnUrl`.

### Public account surfaces

- Strony Blazor:
  - `/account/login`,
  - `/account/register`,
  - `/account/confirm-email`,
  - `/account/forgot-password`,
  - `/account/reset-password`,
  - `/account/security`.
- Serwerowe handlery account:
  - lokalny login i logout,
  - lokalna rejestracja,
  - rozpoczęcie i zakończenie potwierdzania e-maila,
  - rozpoczęcie i zakończenie resetu hasła,
  - rozpoczęcie Entra login/register/link,
  - obsługa callbacku Entra i zakończenie linkowania.
- UI używa MudBlazor, lokalizowanych zasobów i code-behind `.razor.cs`.
- Konto z niepotwierdzonym e-mailem może ponowić wysyłkę wiadomości, ale nie uzyskuje dostępu członka.

## Phase 1: Identity foundation and tenant model

### Intent

Wprowadzić docelowy, provider-neutral model konta oraz jeden kontekst EF bez jeszcze udostępniania nowych ekranów.

### Work

- Dodać wymagane pakiety ASP.NET Core Identity EF i wybranego klienta SMTP do odpowiednich projektów, bez przenoszenia typów frameworka do `PlanDeck.Application`.
- Utworzyć `ApplicationUser : IdentityUser<Guid>` w `Core/PlanDeck.Infrastructure/Identity/`.
- Utworzyć `PlanDeckTenant`, `TenantRole`, docelowy `AppUser` i `TenantInvitation`; usunąć z `AppUser` zależność od `EntraObjectId`, `NormalizedEmail` i claimów dostawcy.
- Przebudować konfiguracje EF:
  - globalny unikalny indeks `ApplicationUser.NormalizedUserName`,
  - globalny filtrowany unikalny indeks `ApplicationUser.NormalizedEmail`,
  - relacja 1:1 `ApplicationUser`–`AppUser` po wspólnym `Guid`,
  - relacja `AppUser`–`PlanDeckTenant`,
  - unikalny hash aktywnego zaproszenia i indeks po znormalizowanym e-mailu/tenantcie,
  - ograniczenia długości oraz konwersje enumów.
- Zmienić `PlanDeckDbContext` na `IdentityUserContext<ApplicationUser, Guid>`, wywołać bazowe mapowanie Identity i zachować fail-closed query filters dla `ITenantScoped`.
- Dodać wewnętrzny provisioning scope i transakcyjny serwis tworzący w jednej transakcji `ApplicationUser`, `PlanDeckTenant` i `AppUser`.
- Zastąpić lookup użytkownika po `(tid, oid)` provider-neutral lookupem po `ApplicationUser.Id`; zapytania globalne kont wykonywać wyłącznie w dedykowanym repozytorium Identity.
- Przebudować `PlanDeckIdentity`, `HttpContextCurrentUserContext`, `CookieSessionValidator` i tworzenie principal tak, aby bazowały na aplikacyjnych claimach oraz aktywnym profilu.
- Zarejestrować `UserManager`, `SignInManager`, token providers, store EF i claims principal factory przez `AddLocalServices`.
- Ustawić wymagania hasła, lockout, wymagane potwierdzenie e-maila, czas życia tokenów oraz parametry cookie z konfiguracji.
- Usunąć wszystkie stare migracje i snapshot, następnie wygenerować jedną migrację bazową dla Identity, tenantów i obecnego modelu domenowego.
- Zaktualizować testy modelu EF, filtrów tenantowych, stampingu i migracji do nowego schematu.

### Automated acceptance

- `PlanDeckDbContext` tworzy schemat Identity i domeny z jednej migracji bazowej.
- Dwa konta nie mogą mieć tego samego znormalizowanego username ani e-maila.
- Zapis tenantowej encji bez aktywnego tenanta nadal kończy się błędem.
- Provisioning nowego właściciela atomowo tworzy konto, tenant i profil; awaria dowolnego kroku nie pozostawia częściowych danych.
- Principal lokalny i Entra ma identyczne claimy aplikacyjne, a `ICurrentUserContext` nie odczytuje `oid`.
- Nieaktywny albo brakujący `AppUser` unieważnia cookie.

### Manual acceptance

- Nowa baza startuje w Development bez ręcznych poprawek schematu.
- W logach i konfiguracji nie są emitowane tokeny, hasła ani sekrety SMTP.

## Phase 2: Local registration and sign-in

### Intent

Udostępnić bezpieczną lokalną rejestrację, login username/e-mail i logout, z pełnym provisioningiem tenantowym.

### Work

- Dodać request/response modele account z jednoznacznymi kodami wyników zamiast wyjątków prezentowanych bezpośrednio użytkownikowi.
- Zaimplementować normalizację identyfikatora logowania:
  - wartość z `@` wyszukuje `NormalizedEmail`,
  - pozostała wartość wyszukuje `NormalizedUserName`,
  - username zawierający `@` jest odrzucany podczas rejestracji.
- Zaimplementować lokalną rejestrację z e-mailem, imieniem, nazwiskiem, username, hasłem i opcjonalnym tokenem zaproszenia.
- Bez zaproszenia tworzyć tenant i profil `Owner`; z ważnym zaproszeniem tworzyć profil `Member` w tenantcie zaproszenia.
- Jeśli publiczna rejestracja jest wyłączona, pozwolić wyłącznie na rejestrację z ważnym zaproszeniem.
- Walidować token zaproszenia po hashu, e-mail i wygaśnięcie w transakcji; nie oznaczać go jako wykorzystany przed potwierdzeniem e-maila.
- Dla istniejącego e-maila lub username zwracać bezpieczny, lokalizowany wynik bez ujawniania dodatkowych danych konta.
- Zaimplementować lokalny login przez `SignInManager` z lockoutem i jednolitym błędem credentials.
- Dodać rate limiting dla rejestracji, loginu, resend confirmation i resetu hasła.
- Zastąpić GET `/auth/logout` mutującym POST z antiforgery; pozostawić lokalny, zweryfikowany `returnUrl`.
- Dodać integracyjne testy rejestracji owner/member, konfliktów globalnej unikalności, kill switcha, lockoutu, loginu username/e-mail i bezpiecznego logoutu.

### Automated acceptance

- Rejestracja bez zaproszenia tworzy konto w nowym tenantcie jako `Owner`.
- Rejestracja z ważnym zaproszeniem tworzy konto w tenantcie zapraszającego jako `Member`.
- Rejestracja z brakującym, zmienionym, wygasłym lub cudzym tokenem nie tworzy danych.
- Username i e-mail logują to samo konto; porównania używają normalizatorów Identity.
- Seria błędnych haseł aktywuje lockout, a komunikat nie ujawnia, czy konto istnieje.
- Wyłączona publiczna rejestracja blokuje self-signup, lecz nie rejestrację z ważnym zaproszeniem.
- Logout przez GET nie jest dostępny, a POST bez antiforgery jest odrzucany.

### Manual acceptance

- Użytkownik może przejść lokalny flow rejestracji i zobaczyć czytelną informację o wymaganym potwierdzeniu e-maila.
- Formularze zachowują bezpieczne wartości po błędzie, ale nigdy nie odtwarzają hasła.

## Phase 3: Email confirmation and password reset

### Intent

Dokończyć cykl życia lokalnego konta i oddzielić wysyłkę wiadomości od dostawcy.

### Work

- Zaimplementować `IEmailSender<ApplicationUser>` jako adapter SMTP/provider, używając silnie typowanej konfiguracji i sekretów poza repozytorium.
- Użyć istniejącego MailPit z AppHost jako lokalnego odbiornika; rozszerzyć konfigurację o host, port, TLS, credentials, sender address/name i publiczny base URL.
- Wysyłać absolutne, kodowane linki z tokenami Identity; nie logować surowych tokenów.
- Dodać potwierdzenie e-maila, resend confirmation i obsługę tokenów nieważnych, wykorzystanych lub wygasłych.
- Dopiero po potwierdzeniu e-maila:
  - aktywować dostęp członka,
  - atomowo oznaczyć zaproszenie jako przyjęte,
  - aktywować oczekujące członkostwa projektu/zespołu dla tego e-maila w tym tenantcie.
- Dodać forgot/reset password z jednakową odpowiedzią dla istniejącego i nieistniejącego e-maila.
- Po zmianie hasła zaktualizować security stamp i unieważnić pozostałe sesje.
- Dodać retry zgodny z istniejącą polityką, ale nie maskować trwałych błędów wysyłki; użytkownik otrzymuje jawny wynik, a operacja może zostać bezpiecznie ponowiona.
- Dodać lokalizowane szablony tekstowe/HTML z poprawnym kodowaniem danych użytkownika.
- Dodać testy adaptera e-mail, tokenów, resend, aktywacji zaproszenia, resetu i unieważnienia cookie.

### Automated acceptance

- Niepotwierdzone konto nie może zalogować się jako członek.
- Prawidłowy token potwierdza e-mail dokładnie raz i aktywuje właściwe zaproszenie.
- Token potwierdzenia nie może zaakceptować zaproszenia dla innego e-maila lub tenanta.
- Forgot password zawsze zwraca ten sam publiczny wynik.
- Prawidłowy reset zmienia hasło, unieważnia token oraz wcześniejsze sesje.
- Konfiguracja produkcyjna bez wymaganych ustawień poczty nie uruchamia się w success-shaped fallbacku.

### Manual acceptance

- MailPit odbiera wiadomości potwierdzenia i resetu podczas lokalnego uruchomienia Aspire.
- Linki działają po kliknięciu z wiadomości i wracają na właściwy ekran PlanDeck.

## Phase 4: Multi-tenant Entra and explicit account linking

### Intent

Obsłużyć organizacyjne konta Entra z dowolnego tenanta bez automatycznego łączenia po e-mailu.

### Work

- Skonfigurować app registration jako multi-tenant i authority `/organizations`.
- Włączyć tenant-aware issuer validation zgodną z Microsoft Identity Platform; nie używać stałego issuer jednego tenanta ani `/common` jako authority.
- Zachować walidację audience, nonce, correlation cookie i podpisu tokenu.
- Dodać chroniony stan OIDC z intencją `login`, `register` albo `link`, lokalnym `returnUrl`, opcjonalnym hashem zaproszenia i identyfikatorem bieżącego użytkownika dla linkowania.
- Entra login:
  - odczytuje `(tid, oid)`,
  - wyszukuje wyłącznie jawny external login,
  - loguje istniejące aktywne konto,
  - dla braku powiązania kieruje do wyboru rejestracji lub lokalnego loginu.
- Entra registration:
  - wymaga jawnej intencji register,
  - pobiera bezpieczne dane profilu z principal,
  - wymaga dostępnego e-maila i jego potwierdzenia przez PlanDeck przed aktywacją,
  - tworzy tenant `Owner` bez zaproszenia albo profil `Member` z zaproszeniem,
  - przy kolizji e-maila/username nie łączy kont, lecz kieruje do lokalnego loginu i późniejszego linkowania.
- Entra link:
  - jest dostępny na `/account/security` wyłącznie dla zalogowanego konta z lokalnym hasłem,
  - wymaga ponownego podania hasła bezpośrednio przed challenge,
  - po callbacku dodaje external login, aktualizuje security stamp i kończy bez zmiany tenanta,
  - odrzuca Entra identity już połączoną z innym kontem.
- Pozwolić na odłączenie Entra tylko wtedy, gdy konto zachowa lokalne hasło jako co najmniej jedną metodę logowania.
- Nie używać claimu e-mail jako trwałego identyfikatora Entra; trwałym kluczem pozostaje `tid:oid`.
- Dodać testy zdarzeń OIDC, walidacji issuerów, rozdzielenia intencji, kolizji, link/unlink i ochrony przed account takeover.

### Automated acceptance

- Konta z dwóch różnych organizacyjnych tenantów Entra mogą utworzyć niezależne konta PlanDeck.
- Ten sam `oid` z różnym `tid` nie koliduje.
- Entra login bez istniejącego external loginu nie przejmuje konta o tym samym e-mailu.
- Register callback nie może zostać użyty jako login callback ani link callback.
- Link wymaga aktywnej sesji, świeżego potwierdzenia hasła i zgodnego correlation state.
- Nie można połączyć jednej tożsamości Entra z dwoma kontami.
- Konto z wyłącznie jedną metodą logowania nie może jej odłączyć.

### Manual acceptance

- Ekran logowania pozwala wybrać konto lokalne albo Microsoft.
- Ekran rejestracji pozwala wybrać formularz lokalny albo Microsoft.
- Połączenie i odłączenie Entra jest czytelnie widoczne w ustawieniach bezpieczeństwa.

## Phase 5: Account UI and localization

### Intent

Udostępnić kompletne, dostępne i zlokalizowane UI dla nowych przepływów.

### Work

- Dodać sześć stron account i ich `.razor.cs`, zgodnie z wzorcem MudBlazor i bez `@code`.
- Dodać modele formularzy z DataAnnotations lub istniejącym wzorcem walidacji; mapować kody błędów serwera na lokalizowane komunikaty.
- Dodać wspólne komponenty dla:
  - walidacji hasła,
  - komunikatów statusu,
  - przycisków lokalnego/Entra flow,
  - bezpiecznego wyświetlenia celu zaproszenia.
- Zmienić `MainLayout`, aby login prowadził do `/account/login`, a logout wykonywał chroniony POST.
- Po pełnym reloadzie cookie ma być widoczne dla hostowanego klienta WASM; nie utrzymywać równoległego stanu auth w local storage.
- Dodać strony oczekiwania na potwierdzenie, sukcesu/błędu tokenu oraz możliwość ponownej wysyłki.
- Na `/account/security` pokazać username, e-mail, status potwierdzenia i połączone metody logowania, bez ujawniania identyfikatorów Entra.
- Dodać wszystkie user-facing strings do istniejących zasobów lokalizacji `en` i `pl`.
- Zapewnić etykiety pól, role, focus po błędzie, klawiaturową obsługę i busy states bez opóźnień czasowych.
- Dodać testy komponentów/serwera tam, gdzie istnieje odpowiedni harness, oraz przygotować page objects E2E dla account flow.

### Automated acceptance

- Żaden nowy widok nie zawiera `@code` ani hard-coded user-facing strings.
- Każdy formularz ma etykiety i prezentuje błędy walidacji bez utraty bezpiecznych danych.
- `returnUrl` poza aplikacją jest odrzucany.
- Layout nie odwołuje się już do starego GET `/auth/login` i `/auth/logout`.

### Manual acceptance

- Pełny flow rejestracji, potwierdzenia, loginu, resetu, linkowania i logoutu działa po polsku i angielsku.
- Widoki są używalne z klawiatury i przy typowych szerokościach mobilnych.

## Phase 6: Public operations required by tests

### Intent

Uzupełnić prawdziwe API produktu przed usunięciem scenario endpoints, aby setup i cleanup testów nie wymagał dostępu do bazy.

### Work

- Dodać `DeleteTeamAsync` do `ITeamService`, DTO, `TeamGrpcService`, klientowego wrappera i UI.
- Autoryzować usunięcie zespołu zgodnie z modelem produktu: tylko jego twórca może usunąć zespół, a operacja jest idempotentnie raportowana jako success/not found/forbidden bez wycieku między tenantami.
- W repozytorium usunąć zależne członkostwa zespołu w transakcji i nie usuwać użytkowników ani projektów.
- Utrzymać istniejące publiczne delete projektu i sesji jako mechanizmy cleanup; uzupełnić brakujące wrappery/page objects.
- Przebudować zaproszenia projektu i zespołu:
  - istniejące konto w tym samym tenantcie może zostać aktywowane zgodnie z obecną rolą zaproszenia,
  - nieznany e-mail tworzy pending membership i bezpieczne `TenantInvitation`,
  - konto należące do innego tenanta zwraca `account_tenant_conflict`,
  - akceptacja następuje dopiero po rejestracji i potwierdzeniu właściwego e-maila.
- Nie używać globalnego query filter bypass w serwisach projektu/zespołu; globalny lookup konta pozostaje w dedykowanej infrastrukturze Identity.
- Dodać integracyjne testy autoryzacji, cross-tenant isolation, cleanup i pełnego invitation flow.

### Automated acceptance

- Właściciel usuwa własny zespół przez publiczne gRPC, a inny członek otrzymuje forbidden.
- Próba usunięcia zespołu z innego tenanta zachowuje się jak not found.
- Projekt, sesję i zespół można posprzątać bez scenario endpointu i bez bezpośredniego SQL.
- Zaproszenie nie przenosi istniejącego konta między tenantami i nie aktywuje członkostwa przed potwierdzeniem e-maila.

### Manual acceptance

- Użytkownik może usunąć własny zespół z UI po potwierdzeniu operacji.
- Zaproszony nowy użytkownik przechodzi publiczny register/confirm/login i widzi właściwe zasoby tenantowe.

## Phase 7: Test migration and removal of authentication backdoors

### Intent

Przepisać testy na realne przepływy i całkowicie usunąć alternatywną tożsamość testową.

### Work

- Zastąpić w testach E2E `E2eIdentityContextFactory` helperami:
  - publicznej rejestracji właściciela,
  - odczytu linku z kontrolowanej skrzynki testowej,
  - potwierdzenia e-maila,
  - lokalnego loginu i logoutu,
  - wysłania zaproszenia przed rejestracją admina/członka,
  - wejścia gościa wyłącznie przez `/guest/join`.
- Dodać page objects `LoginPage`, `RegisterPage`, `EmailInbox`/mailbox adapter i `AccountSecurityPage`; lokatory tylko przez role, label lub tekst.
- Generować unikalne username i e-maile z suffixem uruchomienia; każdy test sam tworzy potrzebny stan.
- Sprzątać projekty, sesje i zespoły przez publiczne UI/gRPC w `finally`/teardown. Konta testowe są izolowane unikalnymi identyfikatorami, a środowisko testowe może być okresowo resetowane zamiast otrzymywać endpoint kasowania kont.
- Dla lokalnych E2E używać MailPit uruchomionego przez Aspire; dla zdalnego `BaseUrl` wymagać skonfigurowanej testowej skrzynki SMTP/provider dostępnej dla runnera.
- Przepisać testy integracyjne auth na Identity cookies i realny store; testy callbacku Entra używają frameworkowego harnessu OIDC, nie testowego member scheme.
- Usunąć:
  - `TestAuthenticationHandler`,
  - `TestMemberIdentities`,
  - `TestAppUserSeeder`,
  - `E2eScenarioEndpoints`,
  - `E2eScenarioService`,
  - `E2eScenarioClient`,
  - `E2eIdentityContextFactory`,
  - testy scenario endpointów,
  - flagi i konfigurację `Authentication__UseTestScheme`,
  - `E2E_SCENARIO_TOKEN` i wszystkie zależne sekrety/parametry,
  - mapowanie scenario endpoints z `Program.cs`,
  - warunkowe zasoby test-auth z `AppHost.cs`.
- Usunąć z AppHost i dokumentacji rozróżnienie bezpieczeństwa oparte na testowym auth; tryb Testing może pozostać targetem wdrożenia, ale korzysta z realnego Identity.
- Przeszukać repozytorium po nazwach usuwanych typów, flag, claimów `tid`/`oid` w logice aplikacyjnej i trasach `/auth/login|logout`; pozostawić claimy Entra wyłącznie w adapterze OIDC.
- Uruchomić testy jednostkowe, integracyjne i E2E przez Aspire oraz zdalny `BaseUrl`.

### Automated acceptance

- Repozytorium nie zawiera testowego schematu członka, scenario endpointów ani scenario tokenu.
- Żaden test nie seeduje `AppUser` bezpośrednio w bazie w celu ominięcia rejestracji.
- Testy członków logują się lokalnym kontem, a testy gościa używają `/guest/join`.
- Testy uruchamiają się niezależnie i nie współdzielą stałych username, e-maili ani danych domenowych.
- Pełny build, testy jednostkowe, integracyjne i E2E przechodzą bez test-auth.

### Manual acceptance

- Aspire uruchamia aplikację i MailPit bez `E2E_SCENARIO_TOKEN`.
- Na środowisku Testing nie istnieje endpoint ani konfiguracja pozwalająca podszyć się pod członka.

## Cross-cutting verification

- Sprawdzić, że wszystkie nowe zależności przepływają zgodnie z warstwami: Server → Application/Infrastructure, Client → Core.Shared, a Application nie referuje ASP.NET Core Identity.
- Sprawdzić brak PII, tokenów, haseł i sekretów w logach, telemetry, URL-ach innych niż jednorazowe linki oraz snapshotach testowych.
- Sprawdzić antiforgery, secure cookie flags, local return URLs, rate limiting i jednolite błędy na wszystkich endpointach account.
- Sprawdzić izolację cross-tenant dla lokalnych i Entra principals.
- Sprawdzić, że zmiana username/e-mail i security stamp nie zmienia domenowego `AppUser.Id` ani participant identity.
- Sprawdzić pełny `dotnet build PlanDeck.slnx` po każdej fazie oraz najmniejszy właściwy zestaw testów przed przejściem dalej.
- Po fazie 7 uruchomić cały `dotnet test PlanDeck.slnx`; lokalne E2E wymagają działającego Podmana i przeglądarki Playwright.

## Progress

- [x] Phase 1: Identity foundation and tenant model — 811b825
- [x] Phase 2: Local registration and sign-in — 6c21f1e
- [x] Phase 3: Email confirmation and password reset — 5cbb0b4
- [x] Phase 4: Multi-tenant Entra and explicit account linking — a7ba02b
- [x] Phase 5: Account UI and localization — 85e15e4
- [x] Phase 6: Public operations required by tests — adf1a0f
- [x] Phase 7: Test migration and removal of authentication backdoors


