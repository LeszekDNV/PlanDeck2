# Sumaryczny raport architektoniczny — moduł 4 / 10xArchitect

## 1. Opisane projekty

| Repozytorium | Stack i skala | Artefakty |
| --- | --- | --- |
| `LeszekDNV/PlanDeck2` | Warstwowa aplikacja .NET 10: Blazor WebAssembly + MudBlazor, ASP.NET Core, code-first gRPC/gRPC-Web, SignalR, EF Core/SQL i Aspire. Artefakty opisują pięć warstw runtime (`Application`, `Core.Shared`, `Infrastructure`, `Server`, `Client`) oraz testy NUnit/Playwright; analiza ACL wskazuje 44 pliki produkcyjne znające bezpośrednio lub pośrednio stos gRPC. | L5: `context/domain/01-domain-distillation.md`, `02-invariant-aggregate-refactor.md`, `03-anti-corruption-layer.md`. |

**L2:** BRAK artefaktu — podano wyłącznie placeholder `{ścieżka-do-repo-map.md}`.  
**L3:** BRAK artefaktu — podano wyłącznie placeholder `{ścieżka-do-research.md}`.  
**L4:** BRAK artefaktu — podano wyłącznie placeholder `{ścieżka-do-plan.md}`.  
Nie przypisano znalezionych w repo plików `research.md` ani `plan.md` do L3/L4, ponieważ artefakty nie identyfikują ich jako wskazanych wejść.

## 2. Mapa projektu (L2)

**BRAK artefaktu.** Nie można rzetelnie podać wniosków z mapy, stref ryzyka, lokalnych centrów, entry pointów ani unknowns bez wskazania właściwego pliku L2.

## 3. Analiza ficzera (L3)

**BRAK artefaktu.** Nie da się ustalić badanego przepływu, powiązać go ze strefą ryzyka L2 ani opisać inputu, zmian stanu i odpowiedzi. Nie można też wybrać 2–3 ryzyk technical debt na podstawie L3.

**ast-grep: BRAK potwierdzenia.** W jednoznacznie dostępnych artefaktach L5 nie ma udokumentowanego wyniku ast-grep, dlatego raport nie przypisuje żadnemu ryzyku takiego potwierdzenia.

## 4. Plan refaktoryzacji (L4)

**BRAK artefaktu.** Nie można wskazać wybranej opcji, zakresu świadomie wyłączonego ani faz i sposobu ich weryfikacji bez właściwego planu L4. Pliki `context/domain/02-invariant-aggregate-refactor.md` i `03-anti-corruption-layer.md` mają wprawdzie typ `refactor-plan`, ale należą do jawnie wskazanego zbioru L5; bez konkretnej ścieżki L4 nie zostały przeklasyfikowane.

## 5. Domena wg DDD (L5)

**Ubiquitous language.** Kluczowe pojęcia to: **sesja planistyczna** (kontener projektu, zadań, skali i przebiegu estymacji), **runda głosowania** (ukryte głosy → reveal → ręczny wybór), **zadanie sesji** (ADO lub ad hoc, z uzgodnioną estymatą), **gość** (uczestnik jednej aktywnej sesji, bez praw moderatorskich) oraz **write-back** (zapis wyniku do ADO z kontrolą rewizji). Najważniejszy rozjazd model–kod: runda nie jest trwałą encją; aktywne zadanie, głosy, reveal i rewizja żyją w pamięci procesu. Ponadto kod pozwala zapisać estymatę bez serwerowego sprawdzenia reveal i aktywnego zadania, a lifecycle sesji ma tylko `Draft` i `Active`, mimo wymagań historii wyników.

**Niezmiennik #1.** Uzgodniona estymata może zostać zapisana wyłącznie dla aktualnie aktywnego zadania, po ujawnieniu rundy, jako wartość ze skali; przejście i wynik muszą być atomowe i wersjonowane. Plan L5 przypisuje ten niezmiennik do agregatu z rootem **`PlanningSession`**, zawierającego wewnętrzne `VotingRound`, `SessionTask`, aktywne zadanie i rewizję. `VotingRound` nie jest osobnym rootem, ponieważ reguła „dla aktywnego zadania” przecina granicę rundy i sesji.

**Anti-Corruption Layer.** Największy przeciek to stos code-first gRPC (`protobuf-net.Grpc` + `Grpc.Core`). Artefakt wykazuje jego zasięg przez **5 granic odpowiedzialności**: wire contract, Application, host, transport klienta i UI — łącznie 44 pliki produkcyjne w 4 projektach. Docelowy ACL rozdziela `Grpc.Contracts`, `Grpc.ServerAdapter` i `Grpc.ClientAdapter`; Application ma udostępniać porty przypadków użycia i neutralne błędy, a widoki oraz porty klienta nie powinny znać `RpcException`, `StatusCode` ani wire DTO.

## 6. Decyzje, które należą do mnie

Artefakty zapisują rekomendacje AI: priorytet dla trwałej rundy, root `PlanningSession` oraz wydzielenie ACL dla gRPC. Dokumentują też rozstrzygnięcia: obecność użytkownika pozostaje poza agregatem, write-back ADO jest osobnym procesem po zatwierdzeniu wyniku, a `VotingRound` nie staje się niezależnym rootem. **BRAK artefaktu**, który przypisuje te decyzje autorowi i wyjaśnia, co zostało rozstrzygnięte samodzielnie przez właściciela projektu. Z tego powodu raport nie dopisuje motywacji ani podziału odpowiedzialności między AI i człowieka.
