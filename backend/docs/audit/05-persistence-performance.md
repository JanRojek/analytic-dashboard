# Audyt B — persistence, zapytania i koszty I/O

Data: 2026-11-06. Druga analiza diagnostyczna, bez zmian implementacji.

## Zakres i sposób oceny

Przeczytano raporty [01](01-architecture.md), [02](02-findings.md) i [03](03-refactoring-roadmap.md), a następnie ponownie prześledzono wszystkie siedem repozytoriów, wywołujące je handlery, AppDbContext i DI, konfiguracje EF oraz snapshot indeksów, oba adaptery Identity, seeder developerski, health check, pracownika importu, storage i czytniki DuckDB. Uwzględniono testy wskazujące oczekiwaną semantykę paginacji, odczytów, rollback i auth. Nie uruchamiano zapytań do bazy, migracji, testów, instalacji, EXPLAIN ani benchmarków. Liczby poniżej wynikają z wywołań w kodzie, a nie z pomiaru SQL.

Raport rozdziela trzy rzeczy: niepotrzebny transfer/materializację, zbędne podróże do bazy oraz odczyty niezbędne do zachowania kontraktu lub współbieżności. AsNoTracking nie ogranicza liczby kolumn. Any użyte wewnątrz predykatu IQueryable nie oznacza osobnego wywołania sieciowego na każdy rekord. Przeniesienie Select do SQL może zmniejszyć transfer, ale nie musi uzasadniać nowej rodziny portów i DTO dla kilku małych encji.

Pewność **Confirmed** oznacza tu potwierdzony kształt operacji, nie potwierdzone spowolnienie produkcji. Wszystkie nowe propozycje AUD-029–033 mają priorytet **Low**: są lokalnymi usprawnieniami lub kandydatami do pomiaru. Ważniejsze problemy integralności i kosztu pozostają pod dotychczasowymi ID; nie naliczono ich ponownie jako nowych odkryć.

## Wynik przekrojowy

Persistence jest w większości oszczędne i semantycznie poprawne: brak lazy loading i typowego N+1 w ścieżkach HTTP, odczyty biznesowe zwykle nie są śledzone, zapisy widgetów świadomie używają śledzonych encji i jednego commitu, a Project rename/delete oraz Dashboard delete wykorzystują operacje warunkowe wykonywane w bazie. Największe znane koszty nadal pochodzą z kopiowania Parquet, profilowania i nieograniczonej liczby wyników, opisanych w AUD-009–011, a nie z braku AsNoTracking w całym projekcie.

Najbardziej uzasadnione dodatkowe uproszczenia to dopasowanie informacji pobieranej wyłącznie do sprawdzenia istnienia oraz węższy odczyt statusu importu. Łączenie metadata reads ma sens po potwierdzeniu częstotliwości i opóźnienia DB; nie wymaga przebudowy całej warstwy persistence. Nie należy optymalizować kolejki przed ustaleniem atomowego przejęcia i recovery z AUD-004/007/008.

## Inwentarz repozytoriów i rzeczywistych konsumentów

Ścieżki `Repositories/...` w tabelach oznaczają dokładnie `src/AnalyticDashboard.Infrastructure/Repositories/...`. Ścieżki `Application/...` oznaczają `src/AnalyticDashboard.Application/...`. Numery odnoszą się do zastanych plików źródłowych.

### ProjectRepository

| Metoda i lokalizacja | Konsument / semantyka | Ocena operacji |
|---|---|---|
| AddAsync, `Repositories/ProjectRepository.cs:18-41` | CreateProjectHandler `Application/Projects/CreateProject/CreateProjectHandler.cs:24-49` | Add + jeden SaveChanges. Unikalność rozstrzyga DB, catch dotyczy konkretnego constraintu; odpięcie odrzuconej encji zapobiega ponowieniu tego INSERT w tym scope. Poprawne; nie dodawać uprzedniego Any na duplikat jako zamiennika indeksu. |
| GetByIdAndOwnerAsync, `:44-55` | GetProjectById `:18-33`, RenameProject `:20-56`; dodatkowo create dashboard/import/list datasets | Jeden nieśledzony odczyt całego małego Project. Uzasadniony dla szczegółów i rename; część konsumentów używa tylko null, zob. AUD-029. |
| RenameAsync, `:58-89` | RenameProjectHandler `:43-65` | Jedno ExecuteUpdate z owner i ID oraz obsługą konkretnego unique violation. Brak SaveChanges po ExecuteUpdate jest poprawny. Wcześniejsza encja z handlera jest no-tracking i służy normalizacji domenowej oraz odpowiedzi, nie zapisowi przez tracker. |
| DeleteByIdAndOwnerAsync, `:92-105` | DeleteProjectHandler `:19-33` | Jedno ExecuteDelete, bez zbędnego pobrania encji; affectedRows rozróżnia brak. Koszt/kolejność cleanup plików to istniejące AUD-006, nie argument za powrotem do load+Remove. |
| CountByOwnerAsync, `:108-114` | GetProjectsHandler `:29-44` | Agregacja SQL, bez materializacji encji. Count jest potrzebny obecnemu kontraktowi totalCount/totalPages i obcięciu page do ostatniej strony. |
| GetPageByOwnerAsync, `:117-130` | GetProjectsHandler `:48-61` | Filter/order/skip/take w DB, no-tracking. DTO używa trzech z czterech pól Project; sama projekcja usunęłaby tylko OwnerId i jedną alokację encji na wiersz. Przy max 100 nie uzasadnia osobnego refaktoru. |

Rename obecnie wykonuje SELECT + UPDATE, lecz zastąpienie pierwszego SELECT samym Any nie wystarczy: `RenameProjectHandler.cs:33,53-56` potrzebuje reguły `Project.Rename` i CreatedAtUtc w odpowiedzi. Jednokomendowe UPDATE RETURNING wymagałoby zmiany kontraktu/implementacji oraz zachowania normalizacji z `Domain/Entities/Project.cs:30-54`; nie jest bezkosztową zamianą nazwy metody.

### DashboardRepository i WidgetRepository

| Metoda i lokalizacja | Cel | Ocena |
|---|---|---|
| Dashboard AddAsync, `Repositories/DashboardRepository.cs:17-26` | CreateDashboardHandler `Application/Dashboards/CreateDashboard/CreateDashboardHandler.cs:35-46` | AddAsync + SaveChanges. GUID powstaje w domenie; samo Add nie wymaga tu I/O. Zamiana AddAsync na Add upraszcza zapis kosmetycznie, bez wykazanego zysku DB i bez osobnego ID. |
| Dashboard GetAllByProjectAndOwnerAsync, `:29-43` | GetDashboardsHandler `:18-30` | Jedno zapytanie no-tracking. Odpowiedź pomija tylko Revision spośród pól Dashboard; projekcja ma małą korzyść. Brak paginacji już AUD-021. |
| Dashboard GetForUpdateByIdAndProjectOwnerAsync, `:46-60` | UpdateDashboardWidgetsHandler `:35-53,195-199` | AsTracking jest konieczne dla oryginalnej Revision i jej UPDATE w końcowym SaveChanges. To nie SELECT FOR UPDATE ani blokada rekordu na całą operację. Nie zastępować Any/projekcją samej Revision bez zaprojektowania równoważnego tokena concurrency. |
| Dashboard GetByIdAndProjectOwnerAsync, `:63-77` | GetDashboardByIdHandler `:18-35` | No-tracking; odpowiedź wykorzystuje wszystkie pola encji. Osobna projekcja nie zmniejszyłaby liczby kolumn. |
| Dashboard DeleteByIdAndProjectOwnerAsync, `:80-95` | DeleteDashboardHandler `:18-23` | Warunkowy ExecuteDelete, jeden bezpośredni zapis, kaskada Widgets w bazie. Nie potrzebuje wstępnego load ani późniejszego SaveChanges. |
| Dashboard GetDashboardCurrentRevisionAsync, `:98-112` | Handler catch concurrency `Application/Widgets/UpdateDashboardWidgets/UpdateDashboardWidgetsHandler.cs:201-218` | Dobra projekcja nullable long: odróżnia usunięcie od Revision=0 i odczytuje aktualną wartość z DB mimo istniejącego śledzonego obiektu. Nie zamieniać na ponowne pobranie śledzonej encji. |
| Widget StageAdd/StageRemove, `Repositories/WidgetRepository.cs:17-20,84-87` | Synchronizacja całego zestawu `UpdateDashboardWidgetsHandler.cs:158-199` | Tylko staging; brak osobnych commitów jest zamierzony. Zachować wspólny SaveChanges z Revision. |
| Widget GetAllByDashboardProjectOwnerAsync, `:22-40` | GetWidgetsHandler `Application/Widgets/GetWidgets/GetWidgetsHandler.cs:18-41` | Jeden nieśledzony SELECT, odpowiedź wykorzystuje wszystkie pola, łącznie z layoutem. Wprowadzenie DTO projekcji zmieni głównie miejsce mapowania. |
| Widget GetAllForUpdateAsync, `:43-60` | UpdateDashboardWidgetsHandler `:132-192` | Tracking domyślny jest potrzebny update/delete. Wczytanie pełnego zbioru jest zgodne z full-replacement: trzeba ustalić również elementy pominięte w payloadzie. Paginacja tego odczytu zepsułaby usuwanie. |
| Widget GetByIdAndDashboardProjectOwnerAsync, `:63-81` | GetWidgetDataHandler `Application/Widgets/GetWidgetData/GetWidgetDataHandler.cs:23-63` | No-tracking, jeden SELECT z pełnym scope. Query danych używa większości konfiguracji; nie używa layoutu i CreatedAtUtc. Węższa projekcja jest możliwa, ale nie usuwa dodatkowego requestu ani pliku; najpierw AUD-010 i pomiar metadata reads. |

Zagnieżdżone `Projects.Any` i `Dashboards.Any` w tych predykatach są częścią zapytania IQueryable. Nie ma w kodzie pętli wykonującej jedno zapytanie o ownera na każdy widget. Usunięcie tych predykatów tylko dlatego, że handler wcześniej sprawdził dashboard, osłabiałoby samodzielny kontrakt portu i nadal nie rozwiązywałoby TOCTOU. Nie proponuje się tego jako optymalizacji.

### DatasetRepository i DatasetVersionRepository

| Metoda i lokalizacja | Cel / użyte dane | Ocena |
|---|---|---|
| Dataset AddAsync, `Repositories/DatasetRepository.cs:17-26` | Brak wywołań produkcyjnych/testowych | AUD-020: usunąć martwą ścieżkę, nie optymalizować ani dodawać do niej nowego UoW. |
| Dataset GetAllByProjectAndOwnerAsync, `:28-44` | GetDatasetsHandler `Application/Datasets/GetDatasets/GetDatasetsHandler.cs:38-93` | No-tracking, sortowanie w DB; bieżące wersje pobierane zbiorczo. Brak N+1. Dodatkowa projekcja może pominąć ProjectId, ale główny znany problem skali to AUD-021. |
| Dataset GetByIdAndProjectOwnerAsync, `:47-63` | Detail, rows, profile, query, status, delete | No-tracking. Zastosowania są różne: detail potrzebuje nazwy/dat, rows/query przede wszystkim CurrentVersionId, status tylko istnienia/ID. Nie zamieniać całej metody na Any; AUD-030/031 proponują węższe konkretne odczyty. |
| Dataset DeleteByIdAndProjectOwnerAsync, `:66-83` | DeleteDatasetHandler `:58-68` | Poprawny warunkowy SQL, ale pliki usuwane są wcześniej — AUD-001/006. Mniej SELECT nie naprawi kolejności ani trwałości cleanup. |
| Dataset PublishVersionAsync, `:86-132` | Brak wywołań | Tracking + Any Ready + SaveChanges to martwe API AUD-020. Nie ma korzyści z projektowania optymalnego UPDATE dla niewykorzystywanej ścieżki. |
| Dataset GetExistingDatasetIdsAsync, `:134-150` | UpdateDashboardWidgetsHandler `:85-102` | Dobra projekcja wyłącznie ID dla zbioru unikalnych datasetów. Handler musi wskazać konkretny brakujący ID, zatem pojedyncze bool Any nie zastąpi wyniku. Scalar projection nie wymaga AsNoTracking. |
| Version AddAsync, `Repositories/DatasetVersionRepository.cs:19-28` | Brak wywołań | AUD-020, nie dodawać równoległej ścieżki tworzenia zamiast atomowego PendingDatasetImportRepository. |
| Version GetByIdAsync, `:30-42` | Detail/rows/profile/query | No-tracking i warunek zgodności DatasetId są poprawne. Część klientów nie potrzebuje wszystkich kolumn, zob. AUD-031. |
| Version GetByIdForUpdateAsync, `:45-53` | Recovery `Infrastructure/Services/Import/ImportJobRecoveryService.cs:31-53` | Tracking potrzebny dla MarkFailed i wspólnego SaveChanges. Nazwa nie oznacza SQL row lock. Samo AsNoTracking uniemożliwi zapis bez dodatkowego attach/update. |
| Version GetByIdsAsync, `:56-70` | GetDatasetsHandler `:45-83` | Pusty zbiór kończy bez SQL, pozostałe ID pobierane w jednym nieśledzonym zapytaniu. Kontrakt wyniku używa wszystkiego poza StorageKey/CreatedAtUtc; węższa projekcja jest możliwa przy większych listach. |
| Version GetAllByDatasetIdAsync, `:73-80` | DeleteDatasetHandler `:39-55` | Pobiera wszystkie wersje, lecz klient wykorzystuje tylko StorageKey. W przyszłej naprawie AUD-001/006 przenieść węższe klucze do planu cleanup obejmującego także źródła. Nie optymalizować obecnego file-first kasowania jako oddzielnego bezpiecznego etapu. |

### ImportJobRepository i PendingDatasetImportRepository

| Metoda i lokalizacja | Cel | Ocena |
|---|---|---|
| GetNextPendingAsync, `Repositories/ImportJobRepository.cs:18-52` | Processor `Infrastructure/Services/Import/ImportJobProcessor.cs:31-50,88-104` | Trzy kolejne SELECT przy znalezionym jobie; wszystkie trzy encje są później mutowane i zasadnie śledzone. Ograniczenie liczby odczytów AUD-032 nie zastępuje atomowego przejęcia AUD-004. |
| GetByIdAsync, `:55-63` | Brak wywołań | Domyślnie śledzony odczyt jest martwym API AUD-020. Nie dodawać AsNoTracking wyłącznie dla zachowania tej metody. |
| GetLatestByDatasetIdAsync, `:66-79` | GetImportStatusHandler `Application/Datasets/GetImportStatus/GetImportStatusHandler.cs:22-47` | No-tracking. `datasetVersionIds` jest niewykonanym podzapytaniem, więc ta metoda ma jeden terminal SELECT, nie dwa. Pobiera pełny job mimo wąskiej odpowiedzi — AUD-030. |
| GetRunningAsync, `:82-87` | Recovery `ImportJobRecoveryService.cs:25-53` | Śledzona lista, następnie jeden odczyt wersji i SaveChanges na zadanie. Tracking potrzebny, lecz można ograniczyć liczbę odczytów i rozmiar partii, AUD-032. |
| SaveChangesAsync, `:90-95` | Processor i recovery | Commit całego wspólnego AppDbContext, również Dataset/Version, nie tylko ImportJobs. Jest to zamierzone w aktualnych wywołaniach; nie zastępować jednym ExecuteUpdate samego joba. |
| Pending CreateAsync, `Repositories/PendingDatasetImportRepository.cs:17-29` | ImportCsvDatasetHandler `:103-108` | Add Dataset + Version + Job, jeden SaveChanges. Ta granica jest poprawna: nie rozbijać na trzy repozytoryjne commity w imię ujednolicenia API. |

## Budżety operacji w głównych przepływach

W tabeli „SELECT” oznacza widoczne terminalne wykonanie zapytania LINQ. Nie obejmuje walidacji cookie/claims, połączeń health, komend wewnętrznych dostawcy i liczby fizycznych pakietów. SaveChanges jest granicą zapisu; może wysyłać wiele komend/batchy, zatem nie jest automatycznie jednym roundtripem. Brak zasobu/walidacja wcześniej kończą część przepływów.

| Przepływ udany | Odczyty PostgreSQL | Zapis / I/O | Źródła i wniosek |
|---|---|---|---|
| Project detail | 1 | brak | GetProjectByIdHandler `:18-33` + ProjectRepository `:44-55`; odpowiedź prawie pokrywa encję. |
| Project list | 2: Count + strona | brak | GetProjectsHandler `:29-61`; zachować totalCount i obcinanie page. Count i strona nie są jednym snapshotem, opisano w 01. |
| Project rename | 1 | 1 ExecuteUpdate | RenameProjectHandler `:20-56`; load służy regule domeny i odpowiedzi, UPDATE powtarza owner/ID. |
| Project / Dashboard delete | 0 | 1 ExecuteDelete | DeleteProjectHandler `:19-23`, DeleteDashboardHandler `:18-23`; nie dodawać odczytu wyłącznie dla odpowiedzi NotFound. |
| Dashboard create | 1 Project | 1 SaveChanges | CreateDashboardHandler `:24-43`; weryfikacja uprawnienia przed tworzeniem pozostaje zasadna, AUD-029 zawęża jedynie wynik. |
| Dashboard list/detail | po 1 | brak | GetDashboardsHandler `:18-30`, GetDashboardByIdHandler `:18-35`. |
| Widgets list | 1 | brak | GetWidgetsHandler `:18-41`; pełny model edytora, osobny problem spójności Revision to AUD-022. |
| Widgets replace | dashboard + zbiór dataset IDs + istniejące widgets: 3 terminalne odczyty | 1 SaveChanges całego zestawu | UpdateDashboardWidgetsHandler `:35-40,89-94,132-137,195-218`; conflict DB dodaje 1 scalar SELECT. Wariant pustej kolekcji i generowany SQL wymagają pomiaru dostawcy; to nie per-widget SELECT. |
| Dataset list | 2 albo 3 | brak | Project + datasets + zbiorcze current versions; ostatnie pomijane przy pustych ID (`DatasetVersionRepository.cs:60-63`). Nie jest 1+N. |
| Dataset detail | 1 albo 2 | brak | GetDatasetByIdHandler `:23-62`; drugi odczyt tylko gdy istnieje CurrentVersionId. |
| Dataset rows | 2 | pełna kopia Parquet + 1 komenda DuckDB | GetDatasetRowsHandler `:33-84`; rowCount jest już w metadanych — nie ma dodatkowego COUNT całego Parquet na stronę. |
| Dataset profile | 2 | Exists + kopia Parquet + schema + C agregacji + preview | GetDatasetProfileHandler `:31-79`, DuckDbDatasetProfileReader `:32-44,65-85`; C to liczba kolumn. AUD-011/033. |
| Dataset query | 2 | kopia Parquet + 2 komendy DuckDB | DatasetQueryExecutor `:46-86`, DuckDbDatasetQueryReader `:29-72`; schema i agregacja. AUD-009/010/031. |
| Widget data | 3: widget + dataset + version | jak query | GetWidgetDataHandler `:23-46` + DatasetQueryExecutor; dla W widgetów są to 3W metadata SELECT i W kopii pliku. To wzrost między requestami, nie EF lazy-loading N+1. |
| Import accepted | 1 Project | zapis CSV + 1 SaveChanges trzech rekordów | ImportCsvDatasetHandler `:28-36,95-108`; bez konwersji w żądaniu HTTP. |
| Processor z jobem | 3 | Running Save + konwersja + końcowy Save | ImportJobRepository `:21-46`, Processor `:46-50,88-104`; dwa etapy SQL są świadome, nie należy trzymać transakcji DB przez konwersję pliku. |
| Processor bez joba | 1 | brak, następnie opóźnienie 1 s | ImportJobRepository `:21-32`, ImportJobWorker `:9,57-67`; około jednego poll/query na sekundę na bezczynnego workera. To proste i adekwatne bez dowodu potrzeby brokera. |
| Recovery N Running | 1 + N odczytów | N SaveChanges + do N prób Delete pliku | ImportJobRecoveryService `:25-53`; działa raz przy starcie, nie w każdym pollu. AUD-032 jest optymalizacją warunkową. |
| Import status | 2 | brak | GetImportStatusHandler `:22-47`; kandydat 1 wąskiego autoryzowanego odczytu AUD-030. |
| Dataset delete | 2 | usunięcia plików + ExecuteDelete | DeleteDatasetHandler `:26-64`; najpierw naprawić AUD-001/006, dopiero upraszczać kształt odczytu. |

Nie należy równoleglić tych zależnych odczytów przez Task.WhenAll na tym samym scoped AppDbContext. Łączenie właściwych zapytań w jeden SQL lub zachowanie prostej sekwencji jest bezpieczniejszym kierunkiem; wiele kontekstów jedynie dla maskowania opóźnienia komplikuje snapshot i zapis.

## Nowe ustalenia i porównania

### AUD-029 — pełny Project pobierany wyłącznie dla istnienia

- **Kategoria / priorytet / pewność:** kształt odczytu; Low; Confirmed co do nieużywanych danych. Korzyść wydajnościowa niewymierzona; rekomendacja uproszczenia jest projektowa.
- **Porównane lokalizacje:** `src/AnalyticDashboard.Application/Dashboards/CreateDashboard/CreateDashboardHandler.cs:24-32`, `src/AnalyticDashboard.Application/Datasets/ImportCsvDataset/ImportCsvDatasetHandler.cs:28-36` i `src/AnalyticDashboard.Application/Datasets/GetDatasets/GetDatasetsHandler.cs:27-35` wykorzystują tylko null. Tymczasem `src/AnalyticDashboard.Application/Projects/GetProjectById/GetProjectByIdHandler.cs:18-33` i `src/AnalyticDashboard.Application/Projects/RenameProject/RenameProjectHandler.cs:20-56` rzeczywiście wykorzystują encję. Wspólna implementacja: `src/AnalyticDashboard.Infrastructure/Repositories/ProjectRepository.cs:44-55`.
- **Aktualna operacja i skutek:** SELECT pełnego Project, no-tracking, materializacja encji z nazwą i datą, mimo że pierwsze trzy ścieżki potrzebują bool. To mały stały narzut oraz nieprecyzyjny kontrakt intencji; nie błąd integralności i nie dodatkowy roundtrip sam w sobie.
- **Alternatywa i korzyść:** jeśli porządkujemy port przy tych przepływach, dodać jedną konkretną `ExistsByIdAndOwnerAsync` z AnyAsync i użyć wyłącznie w klientach istnienia. Zachować pełny odczyt tam, gdzie potrzebny. W GetDatasets osobne sprawdzenie nadal odróżnia nieistniejący/cudzy projekt (404) od własnego pustego (200 z pustą listą); samo usunięcie odczytu zmieniłoby kontrakt.
- **Granica poprawności:** Any nie rezerwuje projektu do następnego INSERT i nie zastępuje FK ani mapowania AUD-016. Nie przedstawiać go jako naprawy race. Nie usuwać ponownego owner predicate w zapytaniu listy.
- **Ryzyko / wysiłek / weryfikacja:** Low / Small. Utrzymać test własny pusty projekt kontra cudzy/brakujący oraz brak tworzenia dla cudzego projektu. Nie tworzyć ogólnego interfejsu „checker”; to ta sama odpowiedzialność istniejącego portu.

### AUD-030 — status importu potrzebuje sześciu pól, a odczytuje dwie encje

- **Kategoria / priorytet / pewność:** liczba i szerokość odczytów; Low; Confirmed dla kształtu, wpływ zależy od częstotliwości pollingu.
- **Porównane lokalizacje:** `src/AnalyticDashboard.Application/Datasets/GetImportStatus/GetImportStatusHandler.cs:22-47` używa Dataset tylko do kontroli istnienia/ID i joba tylko do ID, DatasetVersionId, Status oraz trzech dat. `src/AnalyticDashboard.Infrastructure/Repositories/DatasetRepository.cs:47-63` i `src/AnalyticDashboard.Infrastructure/Repositories/ImportJobRepository.cs:66-79` materializują pełne encje. Wzór już istnieje: `src/AnalyticDashboard.Infrastructure/Repositories/DashboardRepository.cs:104-112` oraz `src/AnalyticDashboard.Infrastructure/Identity/UserAccountTokenService.cs:94-100` pobierają tylko potrzebny scalar.
- **Aktualna operacja i skutek:** dwa kolejne SELECT; w drugim również SourceFileName, SourceStorageKey i ErrorMessage (limit 2000 w `src/AnalyticDashboard.Infrastructure/Data/Configurations/ImportJobConfiguration.cs:20-34`), których kontrakt statusu nie zwraca. AsNoTracking jest poprawne, lecz nie usuwa tych kolumn.
- **Alternatywa i korzyść:** jedna projekcja najnowszego joba powiązana przez wersję i dataset z project/owner predicate, bez materializacji encji. Zmniejsza 2→1 odczyty metadanych i transfer pól niewykorzystywanych przez polling. Można umieścić ją w istniejącym portcie odczytu statusu; nie potrzeba nowego read store.
- **Granica poprawności:** brak datasetu, cudzy dataset i brak joba już zwracają ten sam NotFound, więc ten konkretny przepływ nie wymaga osobnego existence roundtrip. Zachować sortowanie CreatedAtUtc DESC, Id DESC, zgodność dataset/project/owner i bezpieczny publiczny komunikat Failed. Samo Any zamiast pierwszego SELECT zawęzi transfer, ale pozostawi dwa odczyty.
- **Ryzyko / wysiłek / weryfikacja:** Low/Medium / Medium. Testy Pending, Failed, Completed, brak joba, inny projekt/owner i deterministyczny wybór najnowszego joba; w przyszłym pomiarze potwierdzić jeden SQL i brak poufnych pól w projekcji. Nie zmieniać API tylko z powodu implementacji.

### AUD-031 — odczyty dataset → current version można zawęzić i połączyć

- **Kategoria / priorytet / pewność:** metadata reads; Low; Confirmed dla dwóch odczytów, propozycja warunkowa wobec pomiarów.
- **Porównane lokalizacje:** `src/AnalyticDashboard.Application/Datasets/GetDatasetRows/GetDatasetRowsHandler.cs:33-54` i `src/AnalyticDashboard.Application/Analytics/QueryDataset/DatasetQueryExecutor.cs:46-77` potrzebują innych pól niż `src/AnalyticDashboard.Application/Datasets/GetDatasetProfile/GetDatasetProfileHandler.cs:31-78` oraz `src/AnalyticDashboard.Application/Datasets/GetDatasetById/GetDatasetByIdHandler.cs:23-68`. Wspólne pełne odczyty znajdują się w `src/AnalyticDashboard.Infrastructure/Repositories/DatasetRepository.cs:47-63` i `src/AnalyticDashboard.Infrastructure/Repositories/DatasetVersionRepository.cs:30-42`.
- **Aktualna operacja i skutek:** dwa no-tracking SELECT zależne od CurrentVersionId. Rows używa storage/status/rowCount, query storage/status, profile również nazwy i metadanych. Każdy odczytuje m.in. niewykorzystaną konfigurację wersji. Przy wielu widgetach mnoży to liczbę metadata SELECT, niezależnie od znanego kosztu pliku AUD-010.
- **Alternatywa i korzyść:** po pomiarze rozważyć pojedynczą, wąską projekcję datasetu z opcjonalną bieżącą wersją przez `CurrentVersionId` i zgodny `DatasetId`. Najpierw udostępnić ją potrzebującym jej ścieżkom rows/query/profile; nie tworzyć mechanicznie osobnego DTO dla każdej kolumny. Odczyt pojedynczego snapshotu metadanych zmniejszy 2→1 zapytania i okno między nimi, ale nie chroni pliku przed późniejszym DELETE.
- **Granica poprawności:** zwykły inner join z filtrem Ready zepsuje query: nieistniejący/cudzy dataset daje NotFound, istniejący bez Ready daje InvalidQuery (`DatasetQueryExecutor.cs:53-76`). GET detail zwraca dataset z null CurrentVersion, jeśli wersja nie jest gotowa. Zachować te rozróżnienia przez opcjonalną wersję lub jawny wynik; nie filtrować całego datasetu przypadkowo. Zachować sprawdzenie wersja należy do datasetu. Nie zastępować śledzonych odczytów workera tą projekcją.
- **Ryzyko / wysiłek / weryfikacja:** Medium / Medium. Testy bez wersji, Pending/Failed/Ready, błędny wskaźnik wersji, brak wymaganych metadanych, izolacja owner/project oraz publikacja/usunięcie między operacjami. Bez potrzeby wydajnościowej dwa proste odczyty pozostają akceptowalne; koszt plików ma pierwszeństwo.

### AUD-032 — odczyt pracy i recovery importu mają lokalne nadmiarowe roundtripy

- **Kategoria / priorytet / pewność:** metadata reads i tracking; Low; Confirmed dla liczby wywołań, opłacalność zależna od skali importów/restartu.
- **Porównane lokalizacje:** `src/AnalyticDashboard.Infrastructure/Repositories/ImportJobRepository.cs:21-46` robi trzy SELECT, a `src/AnalyticDashboard.Infrastructure/Services/Import/ImportJobRecoveryService.cs:25-53` robi listę Running plus N odczytów wersji. Dla porównania zwykła lista datasetów już zbiera ID i pobiera wersje partią: `src/AnalyticDashboard.Application/Datasets/GetDatasets/GetDatasetsHandler.cs:45-57` oraz `src/AnalyticDashboard.Infrastructure/Repositories/DatasetVersionRepository.cs:56-70`.
- **Aktualna operacja i skutek:** pending work item jest składany sekwencyjnie; recovery to 1+N odczytów oraz N commitów i utrzymanie całej śledzonej listy w jednym scope. Startup recovery nie jest kosztem każdego HTTP ani każdej iteracji workera.
- **Alternatywa i korzyść:** przy naprawie AUD-004 można zwrócić job/version/dataset jednym właściwym odczytem z zachowaniem śledzenia potrzebnych encji; najpierw ustalić atomowe przejęcie, by nie optymalizować wadliwego kontraktu. Recovery może pobierać pary job/version partiami w jednym zapytaniu lub wersje partią, zamiast pojedynczo; ograniczenie partii ograniczy tracker i czas startu dla dużego backlogu. Nie potrzeba dodawać nawigacji tylko w celu jednego join.
- **Granica poprawności:** wszystkie trzy encje processor mutuje (`ImportJobProcessor.cs:46,88-98`), zatem projekcja samych ID + AsNoTracking nie jest równoważna. Nie kopiować wprost nieśledzonego GetByIds z listy do recovery. Zachować końcowy wspólny SaveChanges Ready/publish/Completed; nie łączyć wszystkich N commitów recovery bez decyzji o atomowości i reakcji na jeden błąd pliku. Odczyt po join nadal nie jest atomowym przejęciem. Długiej transakcji obejmującej DuckDB/I/O nie proponuje się.
- **Ryzyko / wysiłek / weryfikacja:** Medium / Medium, zależne od wcześniejszego AUD-004. Sprawdzić tracking wszystkich mutowanych encji, liczbę komend, usunięcie powiązania podczas pracy, dwie instancje i przerwanie recovery. Dla pojedynczego rzadkiego importu zachowanie prostych trzech SELECT jest rozsądne; nie wykazano opóźnienia wymagającego osobnego projektu optymalizacyjnego.

### AUD-033 — profile wykonuje Exists, które nie zmienia obsługi brakującego pliku

- **Kategoria / priorytet / pewność:** I/O i prostota; Low; Confirmed.
- **Porównane lokalizacje:** `src/AnalyticDashboard.Application/Datasets/GetDatasetProfile/GetDatasetProfileHandler.cs:59-79` wywołuje ExistsAsync, potem reader; rows `src/AnalyticDashboard.Application/Datasets/GetDatasetRows/GetDatasetRowsHandler.cs:79-84` i query `src/AnalyticDashboard.Application/Analytics/QueryDataset/DatasetQueryExecutor.cs:80-86` od razu otwierają plik. Wszystkie używają `src/AnalyticDashboard.Infrastructure/Services/DuckDb/DuckDbParquetExecutor.cs:37-51`; lokalne Exists i OpenRead to `src/AnalyticDashboard.Infrastructure/Storage/LocalFileStorage.cs:52-84`.
- **Aktualna operacja i skutek:** dodatkowe File.Exists przed rzeczywistym OpenRead; brak pliku oznacza FileNotFoundException, bez dedykowanego mapowania na wynik HTTP. Plik nadal może zniknąć po Exists, więc pre-check nie gwarantuje powodzenia i nie zamyka race. Nie jest to istotny samodzielny problem wydajności lokalnego dysku.
- **Alternatywa i korzyść:** usunąć pre-check i oprzeć się na faktycznym OpenRead, co usuwa jedno sprawdzenie i zależność handlera profilu od IFileStorage. Jeśli produkt potrzebuje rozróżnionego wyniku „brak artefaktu”, obsłużyć kontrolowany błąd rzeczywistego otwarcia konsekwentnie dla rows/profile/query, zamiast dodawać wszędzie Exists. Nie zmieniać automatycznie na 404: metadane Ready z brakującym plikiem mogą oznaczać awarię wymagającą sygnału operatora.
- **Ryzyko / wysiłek / weryfikacja:** Low / Small. Zachować bezpieczny publiczny błąd i przydatny log; test braku pliku od początku oraz zniknięcia tuż przed otwarciem. Nie wymaga nowej abstrakcji storage. Testów nie dodawano w audycie.

## Identity, health i writerzy poza repozytoriami

W auth liczba wywołań UserManager nie jest automatycznie liczbą SQL. Store może znaleźć encję już śledzoną w tym scope, a operacje tworzenia/logowania mogą wykonywać własne odczyty walidacyjne i claims. Bez trace nie przypisano im pozornej dokładności „jeden SELECT”. Nie rekomenduje się globalnego no-tracking dla Identity.

| Miejsce | Odczyty/zapisy i ocena |
|---|---|
| `src/AnalyticDashboard.Infrastructure/Identity/UserAccountService.cs:25-118` Create | UserManager.CreateAsync obsługuje reguły, hashowanie i zapis; konkretne unique violations mapowane są w adapterze, encja odpinana. Nie zastępować bezpośrednim ExecuteInsert/bulk API ani uprzednim Any jako ochroną duplikatów. |
| `UserAccountService.cs:121-135` status potwierdzenia | FindById, ale wynik potrzebuje tylko null/EmailConfirmed. Wzór nullable bool projection istnieje w `UserAccountTokenService.cs:94-100`. Możliwe lokalne uproszczenie przy gorącym pollingu, z zachowaniem UserNotFound kontra NotConfirmed. Nie ma dowodu, że koszt uzasadnia równoległy port albo omijanie UserManager w pozostałym auth; nie nadano osobnego ID. |
| `UserAccountService.cs:138-160` complete registration sign-in | Pełny user jest potrzebny do tworzenia principal i cookie. Any/EmailConfirmed scalar nie zastępuje SignInManager. |
| `UserAccountService.cs:163-197` password sign-in | Pełny user potrzebny do hasła, potencjalnego rehash i principal. Krytyczny problem to AUD-002, nie tracking. Naprawa przez standardowy pipeline może zwiększyć liczbę operacji DB, co będzie uzasadnionym kosztem ochrony. |
| `UserAccountService.cs:200-219` current user | DTO wykorzystuje Id/Email/DisplayName/CreatedAtUtc. Wąska projekcja jest możliwa, lecz przy SecurityStampValidator może już istnieć tracked user; nie zakładać, że zamiana FindById na SELECT projekcji zawsze zmniejszy roundtripy. Zmierzyć cały request, nie samą metodę. |
| `UserAccountService.cs:222-264` lookup do resend/forgot | Odczyt przez email daje user Id/Email i ewentualnie EmailConfirmed. Następne generowanie tokena wywołuje FindById w tym samym scope. Nie stwierdzono na tej podstawie dodatkowego SQL: ważne jest współdzielenie store/kontekstu. Wynik chroni granicę Application przed Identity. |
| `UserAccountService.cs:267-269` sign-out | Operacja sesji przez SignInManager; brak jawnego zapisu encji w aplikacji. Nie dodawać repozytorium/token table bez wymagania revocation. |
| `src/AnalyticDashboard.Infrastructure/Identity/UserAccountTokenService.cs:16-52` generate tokens | Pobiera user przez ID i przekazuje do providerów. Nie zastępować scalar projection, bo token zależy od danych konta/providerów. Generowanie tokena nie jest w kodzie własnym INSERT do user_tokens. |
| `UserAccountTokenService.cs:54-120` confirm | UserManager.ConfirmEmailAsync jest writerem. Przy ConcurrencyFailure kod słusznie wykonuje świeży no-tracking nullable bool SELECT; nie usuwać go jako „powtórnego odczytu”. Odróżnia już potwierdzone konto od usuniętego. |
| `UserAccountTokenService.cs:123-208` reset password | Reset przez UserManager zmienia hasło/security stamp zgodnie z Identity. Po konflikcie pełny freshUser AsNoTracking jest przekazany VerifyUserTokenAsync (`:158-176`) — pełna encja ma tu sens, inaczej niż bool przy confirm. Nie ujednolicać mechanicznie do tej samej projekcji. |
| `src/AnalyticDashboard.Api/Development/DevelopmentAccountSeeder.cs:12-48`; warunek `src/AnalyticDashboard.Api/Program.cs:161-166` | Osobny scope, FindByEmail i ewentualne Create przez UserManager. Tylko Development i DevSeed:Enabled. Jednorazowy read-before-create jest czytelny i pomijalny wydajnościowo. Nie udowadnia atomowości wielu równoległych startów; unique constraint pozostaje ochroną, a ewentualna awaria seedu to zagadnienie środowiska developerskiego, nie nowa wada głównego persistence. |
| `src/AnalyticDashboard.Api/Program.cs:75-77,176` health | Provider NpgSql wykonuje read-only kontrolę połączenia niezależnie od repozytoriów. Nie ma własnego SQL biznesowego ani migracji w handlerze health. Częstotliwość probe, zewnętrzna ekspozycja i realna komenda pakietu nie były mierzone; nie dodawać cache/read modelu bez potrzeby. |
| `src/AnalyticDashboard.Api/Program.cs:84-87,173-174` stamp validation | Dodatkowa praca przy cookie requests jest konsekwencją interwału zero. Koszt i semantykę revocation odnotowano już w 01/02. Nie włączać tych ukrytych odczytów do tabeli repozytoriów jako N+1 ani nie zwiększać interwału bez decyzji o bezpieczeństwie. |

Handlery auth są delegatami powyższych operacji: Register `Application/Auth/Register/RegisterUserHandler.cs:49-84`, Confirm `Auth/ConfirmEmail/ConfirmEmailHandler.cs:19-23`, Reset `Auth/ResetPassword/ResetPasswordHandler.cs:19-24`, Login `Auth/Login/LoginUserHandler.cs:23-27`, Complete `Auth/CompleteRegistration/CompleteRegistrationHandler.cs:17-19`, CurrentUser `Auth/CurrentUser/GetCurrentUserHandler.cs:18-20`, RegistrationStatus `Auth/RegistrationStatus/GetRegistrationStatusHandler.cs:18-21`, Resend `Auth/ResendConfirmation/ResendConfirmationHandler.cs:28-39`, Forgot `Auth/ForgotPassword/ForgotPasswordHandler.cs:28-43`, Logout `Auth/Logout/LogoutUserHandler.cs:16`. Nie znaleziono innego produkcyjnego bezpośredniego dostępu do AppDbContext w endpointach biznesowych.

## Tracking, commit i operacje set-based — co zachować

`src/AnalyticDashboard.Infrastructure/DependencyInjection.cs:37-43` rejestruje scoped AppDbContext i mapuje IUnitOfWork na tę samą instancję. `src/AnalyticDashboard.Infrastructure/Data/AppDbContext.cs:45-54` tłumaczy concurrency wyjątek wyłącznie w jawnej implementacji IUnitOfWork; repozytoria wywołujące zwykłe SaveChanges nie przechodzą przez to tłumaczenie. Aktualna synchronizacja widgetów używa właściwej ścieżki, a Identity ma własne wyniki konfliktu. Nie ma dowodu, że należy wszędzie narzucić identyczny wyjątek.

- **Śledzić:** dashboard używany do Revision, istniejące widgety pod update/delete, job/version/dataset przetwarzany przez processor oraz job/version recovery. Są mutowane w tych samych scope. Zastąpienie wszystkich przez AsNoTracking bez równoważnego attach/original-values zepsuje zapis lub concurrency.
- **Nie śledzić:** zwykłe list/detail w repozytoriach i freshUser po błędzie resetu. Kod już tak robi. Projekcje ID/nullable Revision/EmailConfirmed nie zawierają encji, zatem nie wymagają dodatkowego AsNoTracking dla redukcji trackera.
- **Zachować granice zapisu:** pending Dataset+Version+Job (`PendingDatasetImportRepository.cs:23-29`), widget create/update/delete+Revision (`UpdateDashboardWidgetsHandler.cs:158-199`), ready+publish+completed (`ImportJobProcessor.cs:88-104`). Trzy niepowiązane commity byłyby regresją integralności.
- **Zachować oddzielne etapy:** Running jest utrwalany przed długą konwersją (`ImportJobProcessor.cs:46-60`), terminalny stan po niej. Nie trzymać blokad/transakcji DB podczas kopiowania i obliczeń. Problem porzuconego Running jest istniejącym AUD-007, nie dowodem, że etapy trzeba skleić długą transakcją.
- **Zachować set-based:** Project rename/delete, Dashboard delete i końcowy Dataset delete wykonują warunkowy zapis w DB. Nie wywołuje się po nich SaveChanges, bo operacja już została wykonana. Owner predicate i affectedRows mają realną rolę. Set-based API omija tracker; obecne ścieżki nie mieszają tych operacji z późniejszą mutacją tej samej tracked encji.
- **Nie zastępować widgetów bulk replace:** pojedyncze ExecuteDelete całego zestawu + ponowne INSERT zmieni identyfikatory/czasy, koszt zapisów i obsługę FK/Revision. Obecny diff zapisuje tylko wykryte zmiany encji i token dashboardu. Optymalizacja jest uzasadniona dopiero przy ustalonym limicie i pomiarze.

## Indeksy zestawione z rzeczywistymi zapytaniami

Ocena dotyczy zgodności kształtu, nie planu wykonywania. Snapshot potwierdza deklarowane indeksy (`src/AnalyticDashboard.Infrastructure/Migrations/AppDbContextModelSnapshot.cs:48-50,75-80,120-126,168-173,195-203,272-276,343-352`). Nie wykonywano EXPLAIN; sam brak pełnego indeksu sortującego nie dowodzi table scan ani problemu na małej tabeli.

| Zapytanie | Istniejąca ochrona / dopasowanie | Decyzja |
|---|---|---|
| Project owner + nazwa, create/rename | unique `(OwnerId,Name)` citext, `Configurations/ProjectConfiguration.cs:47-61` | Dobra ochrona także przy race. Nie dodawać indeksu na samo Name dla tych ścieżek. |
| Project owner, CreatedAt DESC, Id ASC i Count | `(OwnerId,CreatedAtUtc,Id)` z właściwymi kierunkami, `ProjectConfiguration.cs:63-77`; zapytanie `ProjectRepository.cs:112-130` | Dobre dopasowanie. Bardzo głęboki OFFSET może kosztować, lecz brak dowodu skali; keyset wymaga zmiany obecnej paginacji i nie jest quick win. |
| Dataset lista w projekcie | `(ProjectId,CreatedAtUtc DESC,Id ASC)`, `DatasetConfiguration.cs:41-52`; query `DatasetRepository.cs:33-44` | Dopasowane. Nie potrzeba pojedynczego indeksu ProjectId obok tego tylko dla prefiksu filtra. |
| Version po PK i DatasetId | PK Id; query `DatasetVersionRepository.cs:35-42` | Jeden wynik wyszukiwany po PK i sprawdzany pod kątem DatasetId. Dodatkowy `(Id,DatasetId)` nie jest automatycznie potrzebny. |
| Version lista datasetu / number | unique `(DatasetId,VersionNumber)` i `(DatasetId,CreatedAtUtc,Id)`, `DatasetVersionConfiguration.cs:48-67` | Prefiks DatasetId wspiera relację/listę. GetAllByDatasetId nie wymaga sortowania; nie dodawać kolejnego indeksu dla nieistniejącego ORDER BY. |
| Job Pending w kolejności FIFO | `(Status,CreatedAtUtc,Id)`, `ImportJobConfiguration.cs:53-59`; query `ImportJobRepository.cs:21-27` | Dobrze dopasowane. GetRunning również filtruje po prefiksie Status. Atomowe przejęcie to osobne AUD-004. |
| Najnowszy job przez wersje datasetu | unique DatasetVersionId + indeksy wersji, `ImportJobConfiguration.cs:49-59`, `ImportJobRepository.cs:70-79` | Brak idealnego indeksu dla sortowania po CreatedAt jobów całego datasetu nie uzasadnia denormalizacji DatasetId do joba: aktualny HTTP import tworzy nowy dataset i wersję 1 (`ImportCsvDatasetHandler.cs:75-84`). Najpierw rzeczywista liczba wersji i plan. |
| Dashboard lista ProjectId + CreatedAt DESC,Id | tylko ProjectId, `DashboardConfiguration.cs:30`; query `DashboardRepository.cs:34-43` | Kandydat do pomiaru dodatkowego sortowania. W przeciwieństwie do datasetów brak złożonego indeksu; dla kilku dashboardów jest to rozsądne. Przy wykazanym koszcie zastąpić indeks dopasowanym złożonym, uwzględnić koszt zapisu, nie dodawać obu mechanicznie. |
| Widget lista DashboardId + CreatedAt,Id | DashboardId i DatasetId, `WidgetConfiguration.cs:65-67`; query `WidgetRepository.cs:28-40,49-60` | Sortowanie w DB może być dodatkowe. Po limicie widgetów AUD-003 wynik jest mały. Dopiero pomiar uzasadnia `(DashboardId,CreatedAtUtc,Id)` zamiast zwykłego DashboardId. |
| Dashboard UPDATE z Revision | PK Id + token Revision, `DashboardConfiguration.cs:13,32-34` | Sam PK ogranicza rekord. Osobny indeks Revision nie poprawia sensownie tej kontroli concurrency. |
| Identity lookup | unique NormalizedEmail oraz standardowy NormalizedUserName, snapshot `:345-352` | Nie dodawać indeksów po surowym Email tylko dlatego, że publiczne API przyjmuje e-mail; UserManager używa normalizacji. |

## DuckDB i I/O — rozróżnienie uzasadnionych operacji od kosztów

| Miejsce | Ocena i związek z wcześniejszym audytem |
|---|---|
| `src/AnalyticDashboard.Infrastructure/Services/Import/DuckDbCsvImporter.cs:38-59,137-207` | Kopia CSV, CREATE TABLE, COUNT, LIMIT 0 dla liczby kolumn i COPY Parquet. To cztery komendy DuckDB, a nie PostgreSQL roundtripy. Materializacja tabeli pozwala ponownie użyć sparsowanego datasetu w kolejnych operacjach. Nie twierdzi się, że trzeba czterokrotnie parsować CSV. Ewentualny streaming/COPY directly wymaga zachowania metadanych i pomiaru. |
| `DuckDbCsvImporter.cs:62-90,237-254` | Cleanup artefaktu i temp w catch/finally. Nie usuwać w imię mniejszej liczby I/O; poprawić własność i wynik commit z AUD-004/008. |
| `src/AnalyticDashboard.Infrastructure/Services/DuckDb/DuckDbParquetExecutor.cs:30-70` | Wspólny lifetime temp i connection, try/finally poprawny. Pełna kopia przy każdym requestcie to już AUD-010. Baza in-memory nie dowodzi, że każda strona materializuje cały Parquet w pamięci. |
| `src/AnalyticDashboard.Infrastructure/Services/DuckDb/DuckDbRowReader.cs:17-48` | LIMIT/OFFSET wykonywane w DuckDB, parametryzowane. Nie pobiera całego wyniku do C# przed paginacją. SELECT * jest zgodny z preview całego wiersza, a konwersja do string jest częścią kontraktu. Duży OFFSET i brak jawnego ORDER BY wymagają testu oczekiwanej stabilności na niezmiennym pliku; nie stwierdzono odtworzonego błędu kolejności. |
| `src/AnalyticDashboard.Infrastructure/Services/Analytics/DuckDbDatasetQueryReader.cs:29-72,129-200` | Schema lookup przed dynamiczną agregacją ma rolę walidacji kolumn i typu. Nie usuwać go jako zbędnego query bez równoważnej kontroli. Schema można cache'ować po niezmiennej wersji, jeśli pomiar to uzasadnia. Liczność wyniku AUD-009 i kopia AUD-010 są pilniejsze od mikrooptymalizacji list schema. |
| `src/AnalyticDashboard.Infrastructure/Services/Profiling/DuckDbDatasetProfileReader.cs:65-85,147-168,230-248` | Oddzielna agregacja na kolumnę to AUD-011. Parquet może ograniczać odczyt do kolumn/statystyk; nie utożsamiać C komend z C pełnymi fizycznymi skanami wszystkich bajtów. Przed łączeniem agregacji zmierzyć szeroki plik i powtarzane GET. |
| `src/AnalyticDashboard.Infrastructure/Storage/LocalFileStorage.cs:19-49,52-101,104-152` | Stream CopyToAsync bez buforowania całego pliku w C#, CreateNew chroni przed zwykłym nadpisaniem, resolver waliduje klucz. OpenRead/Exists/Delete są krótkimi operacjami filesystemu opakowanymi w Task; samo zastąpienie przez Task.Run nie daje wykazanej korzyści. Klucze z GUID nie są dowodem bezpiecznego cleanup przy równoległych próbach — AUD-004. |

CancellationToken przekazywany jest do terminalnych zapytań EF, kopiowania i wywołań DuckDB. Nie zmierzono przerwania pracy natywnej DuckDB; nie zakładać, że async wrapper gwarantuje szybkie oddanie CPU. Nie dodawać timeoutów/cache/poolingu silnika wyłącznie z nazw klas. Przed zmianą trzeba określić limit pracy i zmierzyć równoległe odczyty; znane ustalenia AUD-003/009–011 pozostają właściwymi punktami wejścia.

## Weryfikacja przyszłych zmian i granice rekomendacji

W drugiej analizie nie wykonywano żadnej nowej weryfikacji wykonawczej. Wynik 19/19 z raportu 01 pozostaje wynikiem wcześniej skompilowanych testów domenowych i nie potwierdza planów SQL ani skuteczności projekcji. Integracje wymagają migracji w fixture i nie były uruchamiane.

Przy wdrażaniu powyższych propozycji wystarczą małe zmiany z konkretnym kryterium:

1. **AUD-029:** zgodne 404 dla cudzego/brakującego projektu, 200 pustej listy dla własnego pustego, zachowana ochrona FK. Korzyść to kształt SELECT i prostsza intencja, nie obietnica dużego przyspieszenia.
2. **AUD-030:** pojedynczy SQL statusu z owner/project scope, najnowszy job wybrany deterministycznie, wszystkie dotychczasowe stany i bezpieczna odpowiedź Failed. Istniejące punkty odniesienia: `tests/AnalyticDashboard.IntegrationTests/Datasets/GetImportStatusEndpointTests.cs:56,144,203`.
3. **AUD-031:** nie zmieniać NotFound/InvalidQuery/null-version ani izolacji. Liczyć SQL w przyszłym kontrolowanym teście z rzeczywistym providerem, a nie testować tekst implementacji LINQ. Mały detail i profil mają już bazowe testy (`GetDatasetByIdEndpointTests.cs:81,179,214`, `GetDatasetProfileEndpointTests.cs:30`); rows i odmiany query wymagają scenariuszy z macierzy 03.
4. **AUD-032:** dopiero w ramach bezpiecznego przejęcia/recovery; test dwóch scope, fault finalizacji i sprawdzenie wszystkich stanów DB oraz artefaktów. Utrzymać obecny sukces/failure processor i recovery (`tests/AnalyticDashboard.IntegrationTests/Import/ImportJobProcessorTests.cs:60,296`, `ImportJobRecoveryServiceTests.cs:24`). Mniej SELECT nie jest kryterium sukcesu, jeśli ginie tracking albo atomowość.
5. **AUD-033:** rzeczywiste otwarcie pliku jest źródłem prawdy; test braku i zniknięcia pliku, z takim samym bezpiecznym kontraktem błędu. Nie dodawać pre-checku we wszystkich czytnikach jako pozornego zabezpieczenia.

Nie rekomenduje się automatycznego przeniesienia każdego mapowania DTO do repozytorium, globalnego AsNoTracking, równoległych zapytań na wspólnym DbContext, bulk replace widgetów, nowego CQRS frameworka ani zestawu indeksów „na wszelki wypadek”. Aktualne kształty są często wystarczająco dobre; zmiana musi usuwać konkretną pracę lub upraszczać rzeczywisty kontrakt.



