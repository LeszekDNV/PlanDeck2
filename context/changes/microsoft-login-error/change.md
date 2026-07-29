---
change_id: microsoft-login-error
title: Naprawa logowania kontem Microsoft w środowisku Test
status: implemented
created: 2026-07-29
updated: 2026-07-29
archived_at: null
---

## Notes

Na środowisku Test po kliknięciu "Sigh In with a Microsoft account" na ekranie logowania przekierowuje na stronę `https://plandeck-server.wittymeadow-96369440.polandcentral.azurecontainerapps.io/account/entra/login?returnUrl=https%3A%2F%2Fplandeck-server.wittymeadow-96369440.polandcentral.azurecontainerapps.io%2F` i wyświetla :

```
404 - Page Not Found
Sorry, the content you are looking for does not exist.
```

Logowanie za pomocą konta SSO nie działa.
