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
