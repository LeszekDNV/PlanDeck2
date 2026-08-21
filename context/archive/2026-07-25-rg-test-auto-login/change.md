---
change_id: rg-test-auto-login
title: Automatyczne logowanie Test Owner w rg-test
status: archived
created: 2026-07-25
updated: 2026-08-21
archived_at: 2026-08-21T08:21:29Z
---

## Notes

Publiczny adres `rg-test` nadal serwuje starą rewizję z testowym schematem
uwierzytelniania, ponieważ najnowsza rewizja nie przechodzi startu.

Środowisko publikowane jako `Testing` uruchamia teraz serwer z
`ASPNETCORE_ENVIRONMENT=Testing`, ale bez testowego schematu uwierzytelniania.
Lokalne konta korzystają ze standardowego cookie middleware.
