---
change_id: create-local-account
title: Lokalne konta użytkowników i logowanie przez Entra ID
status: planned
updated: 2026-07-23
source: context/changes/create-local-account/plan.md
---

# Plan brief

## Outcome

PlanDeck otrzyma jeden model konta obsługujący lokalne credentials i organizacyjne Entra ID.
Każde konto należy do dokładnie jednego tenanta PlanDeck.
Testy używają publicznej rejestracji, loginu, zaproszeń i cleanup API zamiast backdoorów.

## Locked decisions

- Globalnie unikalne username i e-mail.
- Username ma 3–32 znaki i nie zawiera `@`.
- Login zawierający `@` oznacza e-mail; pozostały login oznacza username.
- Self-registration tworzy tenant i profil `Owner`.
- Registration z zaproszeniem tworzy profil `Member` w tenantcie zaproszenia.
- Konto nie może przyjąć zaproszenia do innego tenanta.
- Publiczna rejestracja jest domyślnie włączona i ma kill switch.
- Potwierdzenie e-maila i reset hasła są wymagane.
- SMTP jest ukryte za `IEmailSender<ApplicationUser>`; lokalnie działa MailPit.
- Entra używa multi-tenant authority `/organizations`.
- Entra login, register i link to osobne chronione intencje.
- Nie ma automatycznego linkowania po e-mailu.
- Link Entra wymaga aktywnej sesji i ponownego podania hasła.
- Stare migracje są zastępowane jedną nową migracją bazową.

## Target model

- `ApplicationUser : IdentityUser<Guid>` w Infrastructure: username, e-mail, hasło, lockout, stamp i external logins.
- `AppUser` w Application: tenantowy profil 1:1 ze wspólnym `Id`, dane osobowe, status i rola tenantowa.
- `PlanDeckTenant`: jawny właściciel danych tenantowych.
- `TenantInvitation`: hash tokenu, e-mail, tenant, expiry i status.
- Entra external login: provider `MicrosoftEntra`, key `"{tid}:{oid}"`.
- Jeden `PlanDeckDbContext` oparty na `IdentityUserContext<ApplicationUser, Guid>`.
- Identity jest globalne; domenowe `ITenantScoped` zachowuje fail-closed query filters.
- Principal używa `plandeck_user_id`, `plandeck_tenant_id` i provider-neutral participant ID.

## Phases

### 1. Identity foundation and tenant model
- Dodać Identity EF, model konta/tenanta/zaproszenia, provider-neutral principal i transakcyjny provisioning scope.
- Zastąpić stare migracje jedną migracją bazową.

### 2. Local registration and sign-in
- Dodać register, login username/e-mail i POST logout z antiforgery.
- Obsłużyć invitation registration, lockout, rate limiting, kill switch i jednolite błędy credentials.

### 3. Email confirmation and password reset
- Dodać provider SMTP, szablony, confirm/resend oraz forgot/reset.
- Aktywować zaproszenia po potwierdzeniu e-maila i unieważniać sesje po resecie hasła.

### 4. Multi-tenant Entra and account linking
- Skonfigurować `/organizations`, tenant-aware issuer validation i oddzielne OIDC intents.
- Linkować wyłącznie jawnie po ponownym haśle; bez linkowania po e-mailu.

### 5. Account UI and localization
- Dodać sześć stron `/account/*`, nowe flow layoutu i dostępne formularze MudBlazor.
- Użyć `.razor.cs` oraz lokalizacji `en`/`pl`.

### 6. Public operations required by tests
- Dodać autoryzowane `DeleteTeamAsync` i utrzymać publiczny cleanup projektów/sesji.
- Przebudować zaproszenia na pending membership i bezpieczny token tenantowy.

### 7. Test migration and backdoor removal
- E2E rejestruje realne konta, odczytuje pocztę, loguje się lokalnie i sprząta dane przez publiczne UI/gRPC.
- Goście używają `/guest/join`; test auth, seedery, scenario endpoints/clients i ich konfiguracja zostają usunięte.

## Completion gate

- Brak zależności logiki aplikacyjnej od claimów Entra `tid`/`oid`.
- Brak testowego schematu członka i scenario endpointów.
- Izolacja tenantów działa identycznie dla lokalnego i Entra principal.
- Build, testy jednostkowe, integracyjne i E2E przechodzą z realnym Identity.
