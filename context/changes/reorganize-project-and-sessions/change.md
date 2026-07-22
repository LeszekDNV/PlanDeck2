---
change_id: reorganize-project-and-sessions
title: Reorganize projects and project-owned sessions
status: implementing
created: 2026-07-22
updated: 2026-07-22
archived_at: null
---

## Notes

Po poprzednich zmianach dodaliśmy nadrzędną warstwę grupującą czyli projekt, który agreguje informacje dostępowe dotyczące Azure DevOps. Niestety zmiany te nie objęły Sesji. Sesje również muszą być agregowane w ramach projektu i nie mogą być tworzone niezależnie od projektu. Workflow wygląda tak:
1. Nowy użytkownik systemu tworzy projekt lub projekty, które agregują podstawowe informacje:
 - Adres ADO
- Token do ADO
- projekt w ADO
- Sesje
- Inne podstawowe informacje niezbędne do pracy w programie.

2. Po utworzeniu Projektu automatycznie staje się jego właścicielem. Wszystkie sesje korzystają z informacji zawartych w projekcie. 
3. Właściciel może nadać innym użytkownikom prawa do modyfikacji projektu i sesji w nim zawartych.

Część funckjonalności została już zaimplementowana. Najważniejsze jest przebudowanie wyglądu aplikacji aby promował Projekt nad Sessją oraz uzależnienie sesji od projektu w backendzie.
