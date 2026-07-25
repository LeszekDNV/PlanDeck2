---
change_id: fix-github-actions-deploy
title: Naprawa deploymentu GitHub Actions
status: implemented
created: 2026-07-25
updated: 2026-07-25
archived_at: null
---

## Notes

Pierwszy workflow wdrożeniowy zakończył się błędem podczas `azd provision`
przed migracją bazy i wdrożeniem aplikacji. Po naprawie manifestu provisioning
przeszedł, ale migracja ujawniła zerwaną historię EF: istniejąca baza zawierała
stary schemat, a kod nowy `InitialCreate`.

Środowisko `rg-test` ma rozpocząć od pustych danych. Workflow udostępnia więc
jawny parametr `reset_database`, który usuwa wszystkie tabele aplikacyjne przed
zastosowaniem bieżących migracji.
