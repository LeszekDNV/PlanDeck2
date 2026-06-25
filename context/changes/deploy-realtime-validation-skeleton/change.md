---
change_id: deploy-realtime-validation-skeleton
title: Pilot ACA + Azure SQL env validating the gRPC-Web/SignalR/SQL runtime stack
status: implementing
created: 2026-06-24
updated: 2026-06-25
archived_at: null
---

## Notes

F-03 z roadmapy. Postawić minimalne środowisko pilotażowe na Azure Container Apps + Azure SQL i zwalidować dokładny kontrakt runtime: ładowanie hostowanego Blazor WASM, wywołania unary gRPC-Web, rundę głosowania SignalR utrzymującą połączenie aż do reveal oraz dostęp do SQL przez managed identity. Lekkie/równoległe (nie blokuje lokalnego developmentu): single replica, single region, bez backplane Azure SignalR. Cel — wcześnie zderyzykować niewiadome z infrastructure.md (zachowanie gRPC-Web na ACA ingress, przetrwanie rooms SignalR przy zmianach rewizji/skalowania ACA z minReplicas=1 + sticky sessions).

## Validation summary (2026-06-25)

Pilot wdrożony do `rg-test` / Poland Central (azd env `test`) przez pipeline GitHub Actions (OIDC, bez sekretów). App: `https://plandeck-server.wittymeadow-96369440.polandcentral.azurecontainerapps.io/`.

Wszystkie pięć checków kontraktu runtime **przeszło** (szczegóły w `runbook.md`):
- (a) Rozgrzewka serverless SQL — `/health` 200 w ~6s (cold start z łącznością DB przez MI).
- (b) Hostowany Blazor WASM ładuje się czysto (tylko benign `favicon.ico` 404).
- (c) gRPC-Web unary — „Call server" → „Hello World!" przez ACA ingress.
- (d) Runda SignalR — dwóch uczestników (Test User + gość przez link `/join/<code>`); głosy ukryte jako „Voted" do reveal, po reveal spójne wartości (8/5) na obu klientach; reset rozgłaszany realtime.
- (e) Trwałość Azure SQL przez managed identity — sesja przetrwała pełny reload (świeży boot WASM).

Niewiadome z infrastructure.md zderyzykowane: gRPC-Web działa na ACA ingress bez dodatkowej konfiguracji; rooms SignalR przetrwały wymuszoną zmianę rewizji ACA (`0000010 → 0000011`) — po przełączeniu ruchu akcja Reset nadal rozgłaszała się między klientami (sticky sessions + single replica OK).

CI/CD: rozwiązano blocker NU1403 (rozbieżny content hash `Microsoft.NET.Sdk.WebAssembly.Pack` między SDK library-packs a nuget.org) wyłączając lockfile'e; SP otrzymał role subskrypcyjne (Contributor + User Access Administrator) wymagane przez subscription-scoped deployment azd/Aspire; akcje CI podbite na Node 24.
