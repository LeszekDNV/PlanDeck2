---
change_id: harden-deployment-pipeline
title: Harden the deployment pipeline
status: archived
created: 2026-07-21
updated: 2026-08-21
archived_at: 2026-08-21T08:34:00Z
---

## Notes

Repair the failing SQL migration deployment and establish one authoritative CI/CD path. Add required build and test gates, deployment smoke evidence, rollback and PITR rehearsal, RBAC checks, and production observability.

### Update 2026-08-21 — scope trim after archive review

Zrealizowane przez inne (zarchiwizowane) zmiany — poza zakresem tej zmiany:

- **SQL migration deployment naprawiony** — `fix-github-actions-deploy`: provisioning działa, workflow ma jawny parametr `reset_database` czyszczący tabele przed migracjami.
- **Odporność na wznawianie serverless Azure SQL** — `wait-for-azure-sql-readiness`: akcja `wait-for-azure-sql` w obu workflow przed migracjami.
- **Deployment gates** — `testing-entra-deployment-readiness`: fail-fast preflight konfiguracji Entra (`validate-azure-entra-config`) oraz gate gotowości rewizji ACA + publiczny HTTP 200 (`wait-for-container-app-readiness`) w `azure-dev.yml` i `azure-develop.yml`.
- **Advisory AI code review na PR-ach** — `ci-cd-code-review` (nie jest to jednak wymagany gate).

Pozostały zakres tej zmiany:

- **Wymagane gate'y build + test w CI** — workflowy deploy (`azure-dev.yml`, `azure-develop.yml`) nie uruchamiają `dotnet build`/`dotnet test`; testy biegną tylko w lokalnym pre-commit hooku.
- **Deployment smoke evidence** — poza gate'em HTTP 200 brak scenariuszowego smoke'a po wdrożeniu.
- **Rollback i próba PITR** — udokumentowana i przećwiczona procedura.
- **Kontrole RBAC** — weryfikacja uprawnień tożsamości wdrożeniowej i zasobów.
- **Observability produkcyjna** — alerty/monitoring dla środowiska produkcyjnego.
