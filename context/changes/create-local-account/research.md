---
date: 2026-07-23T21:40:12.731+02:00
researcher: GitHub Copilot
git_commit: 9ed96dfa5bf60bacabed38aa5d291cfce77bcb00
branch: main
repository: LeszekDNV/PlanDeck2
topic: "Lokalne konta użytkownikow, logowanie przez username/email, Entra ID multi-tenant i usuniecie obejsc testowych"
tags: [research, codebase, authentication, local-accounts, entra-id, multitenancy, e2e]
status: complete
last_updated: 2026-07-23
last_updated_by: GitHub Copilot
---

# Research: Lokalne konta i Entra ID multi-tenant

**Date**: 2026-07-23T21:40:12.731+02:00
**Researcher**: GitHub Copilot
**Git Commit**: 9ed96dfa5bf60bacabed38aa5d291cfce77bcb00
**Branch**: main
**Repository**: LeszekDNV/PlanDeck2

## Research Question

Jak dodac tworzenie lokalnych kont (email, imie, nazwisko, nazwa
uzytkownika), logowanie nazwa uzytkownika lub e-mailem oraz alternatywne
logowanie/rejestracje przez Entra ID z dowolnego tenanta organizacyjnego, a
jednoczesnie usunac obecne obejscia tozsamosci testowych z testow E2E i
integracyjnych?

## Summary

Zmiana jest wykonalna, ale nie jest tylko dodaniem pola `PasswordHash` do
`AppUser`. Obecny model utozsamia trzy rozne pojecia:

1. konto aplikacyjne,
2. zewnetrzna tozsamosc Entra `(tid, oid)`,
3. tenant/partycje danych PlanDeck.

`AppUser` jest filtrowany po `TenantId`, a `TenantId` pochodzi z claimu `tid`.
Przed zalogowaniem lokalnym nie istnieje jednak claim ani tenant, wiec zwykle
zapytanie EF nie moze nawet odnalezc konta. Lokalna tozsamosc nie ma tez
naturalnego `oid`. Potwierdzaja to filtr fail-closed i walidacja czlonka
([PlanDeckDbContext.cs:36-72](https://github.com/LeszekDNV/PlanDeck2/blob/9ed96dfa5bf60bacabed38aa5d291cfce77bcb00/src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/PlanDeckDbContext.cs#L36-L72),
[PlanDeckIdentity.cs:13-22](https://github.com/LeszekDNV/PlanDeck2/blob/9ed96dfa5bf60bacabed38aa5d291cfce77bcb00/src/PlanDeck/Web/PlanDeck.Server/Identity/PlanDeckIdentity.cs#L13-L22)).

Rekomendowany kierunek to rozdzielenie:

- globalnego konta uwierzytelniajacego,
- metod logowania (lokalne haslo i Entra),
- profilu uzytkownika,
- czlonkostwa w wewnetrznym tenantcie PlanDeck.

Dla lokalnych hasel nalezy wykorzystac ASP.NET Core Identity (co najmniej
`IdentityCore` z EF stores), zamiast budowac wlasny handler i kryptografie.
Identity dostarcza wersjonowane hashowanie, normalizacje, lockout, security
stamp i bezpieczne tokeny. Obecny member cookie moze pozostac wspolnym
wynikiem obu metod logowania.

Entra moze obslugiwac dowolny tenant organizacyjny przez wielotenantowa app
registration i authority `/organizations`. Nie nalezy uzywac `/common`, jesli
celem sa tylko organizacje, ani wylaczac walidacji issuera bez dedykowanego
walidatora tenant-aware.

Lokalne konta pozwola usunac sztuczny `TestAuthenticationHandler` i
deterministyczne selektory tozsamosci. Nie usuwaja jednak automatycznie
endpointow seed/cleanup E2E: te mozna usunac dopiero, gdy zwykle API produktu
potrafi deterministycznie utworzyc i posprzatac wszystkie potrzebne stany.

## Detailed Findings

### 1. Obecny przeplyw uwierzytelniania

- Serwer uzywa member cookie jako schematu aplikacji i OIDC jako challenge.
  Authority jest budowane z jednego `TenantId`, wiec konfiguracja jest dzis
  single-tenant
  ([ServiceCollectionExtensions.cs:84-153](https://github.com/LeszekDNV/PlanDeck2/blob/9ed96dfa5bf60bacabed38aa5d291cfce77bcb00/src/PlanDeck/Web/PlanDeck.Server/Extensions/ServiceCollectionExtensions.cs#L84-L153)).
- Po walidacji OIDC `AppUserProvisioner` wymaga `tid` i `oid`, tworzy lub
  aktualizuje `AppUser`, a nastepnie dodaje wewnetrzny `AppUserId` do principal
  ([AppUserProvisioner.cs:16-66](https://github.com/LeszekDNV/PlanDeck2/blob/9ed96dfa5bf60bacabed38aa5d291cfce77bcb00/src/PlanDeck/Web/PlanDeck.Server/Identity/AppUserProvisioner.cs#L16-L66)).
- Klient nie przechowuje tokenu Entra. Stan uwierzytelnienia pobiera przez
  gRPC, a cookie pozostaje po stronie hosta
  ([GrpcAuthenticationStateProvider.cs:1-59](https://github.com/LeszekDNV/PlanDeck2/blob/9ed96dfa5bf60bacabed38aa5d291cfce77bcb00/src/PlanDeck/Web/PlanDeck.Client/Services/GrpcAuthenticationStateProvider.cs#L1-L59)).
- `/auth/login` i `/auth/logout` sa juz endpointami HTTP. To wlasciwa granica
  rowniez dla lokalnego sign-in, poniewaz tylko host powinien wywolywac
  `HttpContext.SignInAsync`
  ([Program.cs:103-150](https://github.com/LeszekDNV/PlanDeck2/blob/9ed96dfa5bf60bacabed38aa5d291cfce77bcb00/src/PlanDeck/Web/PlanDeck.Server/Program.cs#L103-L150)).

### 2. Model danych blokujacy proste lokalne konto

- `AppUser` przechowuje obecnie `EntraObjectId`, display name, e-mail i status,
  ale nie ma username, imienia, nazwiska, hasla ani tabeli zewnetrznych loginow
  ([AppUser.cs:3-14](https://github.com/LeszekDNV/PlanDeck2/blob/9ed96dfa5bf60bacabed38aa5d291cfce77bcb00/src/PlanDeck/Core/PlanDeck.Application/Domain/AppUser.cs#L3-L14)).
- Unikalnosc jest wymuszana dla `(TenantId, EntraObjectId)` i
  `(TenantId, NormalizedEmail)`, nie dla globalnego loginu
  ([AppUserConfiguration.cs:33-39](https://github.com/LeszekDNV/PlanDeck2/blob/9ed96dfa5bf60bacabed38aa5d291cfce77bcb00/src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/Configurations/AppUserConfiguration.cs#L33-L39)).
- Globalny filtr `TenantId` ukrywa rekordy przy braku kontekstu, a zapis bez
  tenanta rzuca wyjatek. To poprawne dla danych biznesowych, lecz niewlasciwe
  dla globalnego lookupu loginu
  ([PlanDeckDbContext.cs:68-72](https://github.com/LeszekDNV/PlanDeck2/blob/9ed96dfa5bf60bacabed38aa5d291cfce77bcb00/src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/PlanDeckDbContext.cs#L68-L72),
  [PlanDeckDbContext.cs:134-152](https://github.com/LeszekDNV/PlanDeck2/blob/9ed96dfa5bf60bacabed38aa5d291cfce77bcb00/src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/PlanDeckDbContext.cs#L134-L152)).
- `ParticipantId` jest nadal powiazany z `oid`; po dodaniu innych providerow
  powinien uzywac stabilnego wewnetrznego identyfikatora konta
  ([HttpContextCurrentUserContext.cs:1-69](https://github.com/LeszekDNV/PlanDeck2/blob/9ed96dfa5bf60bacabed38aa5d291cfce77bcb00/src/PlanDeck/Web/PlanDeck.Server/Identity/HttpContextCurrentUserContext.cs#L1-L69)).

#### Rekomendowany model docelowy

| Obszar | Rola |
|---|---|
| `ApplicationUser` | Globalne konto: `Id`, `Email`, `UserName`, `FirstName`, `LastName`, aktywnosc i pola Identity. Bez `ITenantScoped`. |
| `UserLogin` | Provider + klucz providera. Dla Entra kluczem jest para `tid:oid`; nigdy sam e-mail. |
| `PlanDeckTenant` | Wewnetrzny tenant/workspace PlanDeck z wlasnym `Guid`, niezalezny od providera. |
| `TenantMembership` | Powiazanie `ApplicationUser` z tenantem i rola. |
| `EntraOrganization` | Opcjonalne mapowanie `EntraTenantId -> PlanDeckTenantId`, jesli tenant Entra ma automatycznie wyznaczac workspace. |

Zachowanie danych mozna migrowac bez przepisywania wszystkich encji:
istniejace wartosci `TenantId` pozostaja poczatkowo identyfikatorami tenantow
PlanDeck, a dla obecnych rekordow Entra tworzone sa mapowania i `UserLogin`.

#### Login username lub email

Lookup odbywa sie poza filtrem tenantowym, przez store Identity. Nalezy
zdefiniowac jednoznaczna gramatyke:

- username nie moze zawierac `@`, a wartosc z `@` jest interpretowana jako
  e-mail; albo
- jedna globalna tabela znormalizowanych identyfikatorow wymusza unikalnosc
  wspolnej przestrzeni username/e-mail.

Drugi wariant jest najbardziej odporny na kolizje. W obu przypadkach odpowiedz
dla nieznanego loginu i zlego hasla musi byc identyczna.

### 3. Rejestracja lokalna

Minimalny bezpieczny przeplyw:

1. Uzytkownik podaje e-mail, imie, nazwisko, username i haslo.
2. Serwer normalizuje i waliduje dane przez Identity.
3. Tworzy `ApplicationUser` oraz lokalne credentials w jednej transakcji.
4. Tworzy nowy tenant PlanDeck i membership `Owner` albo oczekujace
   czlonkostwo wynikajace z zaproszenia.
5. Wymaga potwierdzenia e-maila przed aktywowaniem zaproszen i dostepu do
   danych opartych na e-mailu.
6. Po spelnieniu warunkow wystawia istniejace member cookie.

Potwierdzenie e-maila jest istotne, poniewaz repozytorium automatycznie
akceptuje oczekujace zaproszenia przez dopasowanie `NormalizedEmail`
([AppUserRepository.cs:61-80](https://github.com/LeszekDNV/PlanDeck2/blob/9ed96dfa5bf60bacabed38aa5d291cfce77bcb00/src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/AppUserRepository.cs#L61-L80)).
Bez weryfikacji lokalny uzytkownik moglby zarejestrowac cudzy zaproszony adres.

Endpointy login/register powinny byc same-origin HTTP, chronione antiforgery,
rate limitingiem i lockoutem. Haslo nie powinno byc przesylane przez kontrakt
gRPC ani logowane. Nalezy rotowac cookie po logowaniu i uniewazniac sesje po
zmianie hasla.

### 4. Rejestracja i logowanie Entra z dowolnego tenanta

Jest to mozliwe dla kont organizacyjnych:

1. App registration: `Accounts in any organizational directory`.
2. Authority: `https://login.microsoftonline.com/organizations/v2.0`.
3. Tenant-aware issuer validation dla rzeczywistego issuera
   `https://login.microsoftonline.com/{tenant-guid}/v2.0`.
4. Identyfikacja loginu przez podpisane `tid` + `oid`.
5. Pierwsze logowanie pelni role rejestracji/provisioningu po consent.

`/organizations` jest lepsze niz `/common`, bo wyklucza osobiste konta
Microsoft. Obecne scope `openid`, `profile`, `email` pozwalaja zwykle na
self-service user consent, choc polityki organizacji uzytkownika moga wymagac
zgody administratora.

Nie nalezy laczyc istniejacego lokalnego konta z Entra tylko dlatego, ze e-mail
jest taki sam. Bezpieczne linkowanie wymaga:

- aktywnej sesji lokalnego uzytkownika i ponownego potwierdzenia hasla, albo
- jednorazowego, wygasajacego procesu potwierdzajacego obie tozsamosci.

Oficjalne zrodla:

- [Convert an application to multitenant](https://learn.microsoft.com/en-us/entra/identity-platform/howto-convert-app-to-be-multi-tenant)
- [Single- and multitenant apps](https://learn.microsoft.com/en-us/entra/identity-platform/single-and-multi-tenant-apps)
- [Claims validation](https://learn.microsoft.com/en-us/entra/identity-platform/claims-validation)
- [ID token claims](https://learn.microsoft.com/en-us/entra/identity-platform/id-token-claims-reference)

### 5. Warstwy i punkty integracji

#### Server

- Zachowuje wspolny member cookie.
- Dodaje HTTP endpoints dla local register/login oraz jawny endpoint OIDC
  challenge.
- Rejestruje Identity i serwisy przez `AddLocalServices`, zgodnie z obecna
  kompozycja DI
  ([ServiceCollectionExtensions.cs:163-195](https://github.com/LeszekDNV/PlanDeck2/blob/9ed96dfa5bf60bacabed38aa5d291cfce77bcb00/src/PlanDeck/Web/PlanDeck.Server/Extensions/ServiceCollectionExtensions.cs#L163-L195)).
- Buduje provider-neutral principal z wewnetrznym `ApplicationUserId`,
  `TenantId`, membership/rola i markerem metody logowania.

#### Application

- Orkiestruje rejestracje, utworzenie tenanta/membership, provisioning Entra i
  jawne linkowanie kont.
- Nie zalezy od `HttpContext` ani hostingowych typow gRPC.
- Utrzymuje walidacje biznesowa i transakcje rejestracji.

#### Infrastructure

- Implementuje EF stores Identity, mapowania tenantow, membership i migracje.
- Udostepnia kontrolowany globalny lookup auth; pozostale dane nadal korzystaja
  z fail-closed tenant filters.

#### Client

- Nowy ekran logowania: username/e-mail + haslo oraz przycisk "Kontynuuj z
  Microsoft".
- Nowy ekran rejestracji: dane lokalne albo "Utworz przez Microsoft".
- Obecny przycisk layoutu nie powinien juz bezposrednio uruchamiac OIDC
  ([MainLayout.razor.cs:12-15](https://github.com/LeszekDNV/PlanDeck2/blob/9ed96dfa5bf60bacabed38aa5d291cfce77bcb00/src/PlanDeck/Web/PlanDeck.Client/Layout/MainLayout.razor.cs#L12-L15)).
- UI pozostaje w MudBlazor, z code-behind i zasobami EN/PL.

### 6. Wplyw na testy

Obecne obejscie sklada sie z:

- `TestAuthenticationHandler` i cookie wyboru deterministycznej tozsamosci
  ([TestAuthenticationHandler.cs:1-142](https://github.com/LeszekDNV/PlanDeck2/blob/9ed96dfa5bf60bacabed38aa5d291cfce77bcb00/src/PlanDeck/Web/PlanDeck.Server/Identity/TestAuthenticationHandler.cs#L1-L142)),
- stalych Test Owner/Admin/Member
  ([TestMemberIdentities.cs:1-37](https://github.com/LeszekDNV/PlanDeck2/blob/9ed96dfa5bf60bacabed38aa5d291cfce77bcb00/src/PlanDeck/Web/PlanDeck.Server/Testing/TestMemberIdentities.cs#L1-L37)),
- seedera uzytkownikow
  ([TestAppUserSeeder.cs:1-96](https://github.com/LeszekDNV/PlanDeck2/blob/9ed96dfa5bf60bacabed38aa5d291cfce77bcb00/src/PlanDeck/Web/PlanDeck.Server/Testing/TestAppUserSeeder.cs#L1-L96)),
- fabryki kontekstow E2E
  ([E2eIdentityContextFactory.cs:1-73](https://github.com/LeszekDNV/PlanDeck2/blob/9ed96dfa5bf60bacabed38aa5d291cfce77bcb00/src/PlanDeck/Tests/PlanDeck.E2e.Tests/E2eIdentityContextFactory.cs#L1-L73)).

Bezpieczna migracja:

1. Dodac prawdziwe lokalne konta testowe przez seeding Identity, z unikalnym
   haslem przekazanym sekretem/configiem test runu.
2. Dodac helper logowania HTTP/browserowego, ktory pobiera realne cookie.
3. Przeniesc testy integracyjne i E2E z selektora `e2e-user` na prawdziwy
   login lokalny.
4. Usunac `TestAuthenticationHandler`, `TestMemberIdentities` i specjalne
   galezie login/logout dopiero po migracji wszystkich konsumentow.
5. Zachowac endpointy scenario seed/cleanup przejsciowo. Usunac je dopiero po
   wykazaniu, ze publiczne API potrafi utworzyc i posprzatac kazdy wymagany
   stan. Sam local auth tego nie gwarantuje.
6. Testing i Production nadal musza byc fail-closed; testowe konta i endpointy
   nie moga byc aktywowane konfiguracja produkcyjna.

Kazdy test powinien miec izolowane konto lub dane z unikalnym sufiksem oraz
cleanup. Wspolne deterministyczne konta sa dopuszczalne tylko, gdy testy nie
mutuja ich wspoldzielonego profilu/hasla i dane domenowe sa izolowane.

## Code References

- [`ServiceCollectionExtensions.cs:84-153`](https://github.com/LeszekDNV/PlanDeck2/blob/9ed96dfa5bf60bacabed38aa5d291cfce77bcb00/src/PlanDeck/Web/PlanDeck.Server/Extensions/ServiceCollectionExtensions.cs#L84-L153) - cookie/OIDC i single-tenant authority.
- [`Program.cs:103-150`](https://github.com/LeszekDNV/PlanDeck2/blob/9ed96dfa5bf60bacabed38aa5d291cfce77bcb00/src/PlanDeck/Web/PlanDeck.Server/Program.cs#L103-L150) - hostowe endpointy login/logout.
- [`AppUserProvisioner.cs:16-66`](https://github.com/LeszekDNV/PlanDeck2/blob/9ed96dfa5bf60bacabed38aa5d291cfce77bcb00/src/PlanDeck/Web/PlanDeck.Server/Identity/AppUserProvisioner.cs#L16-L66) - provisioning po `tid`/`oid`.
- [`AppUser.cs:3-14`](https://github.com/LeszekDNV/PlanDeck2/blob/9ed96dfa5bf60bacabed38aa5d291cfce77bcb00/src/PlanDeck/Core/PlanDeck.Application/Domain/AppUser.cs#L3-L14) - obecny profil powiazany z Entra.
- [`PlanDeckDbContext.cs:36-72`](https://github.com/LeszekDNV/PlanDeck2/blob/9ed96dfa5bf60bacabed38aa5d291cfce77bcb00/src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/PlanDeckDbContext.cs#L36-L72) - tenant context i global filters.
- [`AppUserRepository.cs:61-80`](https://github.com/LeszekDNV/PlanDeck2/blob/9ed96dfa5bf60bacabed38aa5d291cfce77bcb00/src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/AppUserRepository.cs#L61-L80) - auto-akceptacja zaproszen po e-mailu.
- [`TestAuthenticationHandler.cs:1-142`](https://github.com/LeszekDNV/PlanDeck2/blob/9ed96dfa5bf60bacabed38aa5d291cfce77bcb00/src/PlanDeck/Web/PlanDeck.Server/Identity/TestAuthenticationHandler.cs#L1-L142) - obecne obejscie auth.
- [`E2eScenarioEndpoints.cs:1-99`](https://github.com/LeszekDNV/PlanDeck2/blob/9ed96dfa5bf60bacabed38aa5d291cfce77bcb00/src/PlanDeck/Web/PlanDeck.Server/Testing/E2eScenarioEndpoints.cs#L1-L99) - testowe API seed/cleanup.

## Architecture Insights

1. **Tozsamosc nie jest tenantem.** Provider loginu nie powinien wyznaczac
   klucza konta ani byc jedyna reprezentacja organizacji w domenie.
2. **Auth lookup jest globalny, dane biznesowe sa tenant-scoped.** To sa dwie
   rozne granice zapytan i tylko druga powinna uzywac obecnego filtra EF.
3. **Jeden cookie principal dla wielu metod logowania.** Local i Entra roznia
   sie przed wystawieniem cookie; dalsza autoryzacja powinna byc
   provider-neutral.
4. **E-mail nie jest kluczem tozsamosci zewnetrznej.** Entra identyfikuje sie
   przez `(tid, oid)`, a linkowanie wymaga dowodu kontroli obu kont.
5. **Local auth jest funkcja security-critical.** Frameworkowe Identity jest
   bezpieczniejszym i prostszym dlugoterminowo wyborem niz wlasny password
   handler, nawet jesli poczatkowa migracja jest wieksza.
6. **Realne logowanie i deterministyczne dane to dwa osobne problemy testowe.**
   Local accounts zastepuja fake principal, lecz nie zawsze scenario setup.

## Historical Context (from prior changes)

- [`multitenant-persistence-baseline/plan.md`](https://github.com/LeszekDNV/PlanDeck2/blob/9ed96dfa5bf60bacabed38aa5d291cfce77bcb00/context/archive/2026-06-18-multitenant-persistence-baseline/plan.md) ustanowil fail-closed filtr po `tid` i zalozyl, ze `AppUser` reprezentuje Entra identity. Lokalne konta ujawniaja granice tego zalozenia.
- [`fix-test-environment-logout/plan.md`](https://github.com/LeszekDNV/PlanDeck2/blob/9ed96dfa5bf60bacabed38aa5d291cfce77bcb00/context/changes/fix-test-environment-logout/plan.md) dokumentuje, ze brak cookie w Testing oznacza Test Owner oraz ze logout wymagal specjalnego markera `anonymous`. Prawdziwe lokalne logowanie usuwa potrzebe tego cyklu.
- [`separate-production-auth-configuration/change.md`](https://github.com/LeszekDNV/PlanDeck2/blob/9ed96dfa5bf60bacabed38aa5d291cfce77bcb00/context/changes/separate-production-auth-configuration/change.md) wskazuje trwajaca potrzebe oddzielenia Testing od Production i zachowania fail-closed defaults.

## Related Research

Nie znaleziono wczesniejszego `research.md` bezposrednio o lokalnych kontach
lub architekturze Entra multi-tenant. Najblizszy kontekst znajduje sie w
planach wymienionych powyzej.

## Open Questions

1. Czy lokalna rejestracja zawsze tworzy nowy tenant PlanDeck, czy moze sluzyc
   wylacznie do dolaczenia przez zaproszenie?
2. Czy jeden `ApplicationUser` moze nalezec do wielu tenantow PlanDeck? To
   rekomendowany model, ale rozszerza obecne zalozenie jednego `tid`.
3. Czy linkowanie Local <-> Entra wchodzi do MVP, czy dwie metody rejestracji
   moga poczatkowo tworzyc oddzielne konta?
4. Czy self-service registration ma byc publiczna w Production, czy sterowana
   feature flaga / zaproszeniami?
5. Jaki kanal e-mail zostanie uzyty do potwierdzenia adresu i resetu hasla?
6. Czy organizacja Entra automatycznie mapuje sie 1:1 na tenant PlanDeck, czy
   uzytkownik wybiera/tworzy workspace po pierwszym logowaniu?
