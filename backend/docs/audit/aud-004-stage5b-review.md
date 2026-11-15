# AUD-004 — etap 5B: kontrolowane równoległe importy

Data: 2026-11-14. Zakres: backend, bez zmiany HTTP, frontendu, schematu PostgreSQL, pakietów ani semantyki publikacji. Nie wykonano commita.

## Zmiany i odpowiedzialności

| Plik | Zmiana |
| --- | --- |
| `src/AnalyticDashboard.Infrastructure/Services/Import/ImportJobWorker.cs` | Stała liczba niezależnych pętli roboczych, nowy scope dla każdego przebiegu, wspólne oczekiwanie na zakończenie przez `Task.WhenAll`, anulowalne oczekiwanie na pustą kolejkę. |
| `src/AnalyticDashboard.Infrastructure/Services/Import/ImportJobOptions.cs` | Limit równoległości i budżety CPU, pamięci DuckDB oraz spill dla pojedynczego importu. |
| `src/AnalyticDashboard.Infrastructure/Services/Import/DuckDbCsvImporter.cs` | Konfiguracja prywatnej instancji DuckDB przed jej otwarciem, osobny katalog spill wewnątrz istniejącego workspace próby. |
| `src/AnalyticDashboard.Infrastructure/DependencyInjection.cs` | Walidacja nowych opcji podczas startu; zachowane scoped repozytoria, importer i processor. |
| `src/AnalyticDashboard.Api/appsettings.json` | Jawne, konserwatywne wartości domyślne. |
| `src/AnalyticDashboard.Infrastructure/Repositories/ImportJobRepository.cs` | Zmierzona optymalizacja wstępnego wyboru właściciela; właściwa atomowa operacja claim i blokady bez zmian. |
| `tests/AnalyticDashboard.IntegrationTests/Concurrency/ParallelImportWorkerTests.cs` | 12 nowych scenariuszy równoległości, anulowania, publikacji, heartbeatów, restartu i bezczynności. |
| `tests/AnalyticDashboard.IntegrationTests/Import/DuckDbImportResourceTests.cs` | 10 nowych scenariuszy semantyki DuckDB, rzeczywistego ograniczenia pamięci/spill i walidacji konfiguracji. |
| `tests/AnalyticDashboard.IntegrationTests/Performance/ImportClaimPerformanceTests.cs` | Powtarzalny pomiar rzeczywistego claim i planów PostgreSQL na izolowanej bazie. |
| `tests/AnalyticDashboard.IntegrationTests/Concurrency/ImportFinalizationTests.cs` | Przekazanie opcji do dwóch istniejących wywołań konstruktora importera. |
| `tests/AnalyticDashboard.IntegrationTests/Import/ImportArtifactOwnershipTests.cs` | Przekazanie opcji do czterech istniejących wywołań konstruktora importera. |

Dodatkowe artefakty: ten dokument, [pomiar przed zmianą](aud-004-stage5b-claim-before.json), [pomiar po zmianie](aud-004-stage5b-claim-after.json), [pełny diff etapu](aud-004-stage5b-parallel-imports.diff). Nie usunięto plików.

## Pętle robocze i zasoby

Worker uruchamia dokładnie `MaxConcurrentImports` pętli. Każda przetwarza najwyżej jeden import naraz i czeka na jego pełne zakończenie przed następnym claim. Nie powstaje nieograniczona kolejka zadań w pamięci. Nie ma dodatkowego dispatchera, semafora ani schedulera.

Każda iteracja tworzy nowy asynchroniczny scope DI, z własnym `ImportJobProcessor`, importerem, `IImportJobRepository`, `AppDbContext` i `LocalFileStorage`. Konwersja otwiera własne prywatne DuckDB `:memory:`. Istniejący processor tworzy osobne CTS konwersji i heartbeat, a każdy heartbeat korzysta z odrębnego scope i DbContext. Worker współdzieli wyłącznie fabrykę scope, odczytywaną konfigurację, TimeProvider i logger.

Konwersja nadal wykonuje się poza transakcją PostgreSQL. Test zatrzymuje import po fizycznym zapisaniu Parquet, przed finalizacją, i potwierdza zamknięte główne połączenie PostgreSQL oraz brak aktywnej transakcji. Osobny kontekst heartbeat otwiera połączenie tylko na czas odnowienia lease. Nie zmieniono puli połączeń.

Pusta kolejka, zajęci właściciele i błąd pętli powodują oczekiwanie jednej sekundy w danym slocie. Scope jest wtedy już zwolniony. Domyślnie są najwyżej dwa cykle claim na sekundę podczas bezczynności, niezależnie od kosztu pojedynczego cyklu; przy maksymalnej konfiguracji osiem. Pojedynczy cykl może wykonać kilka zapytań przy konkurencji o właścicieli/projekty. Test z kontrolowanym TimeProvider potwierdza brak kolejnego przebiegu przed upływem interwału. Oczekiwanie nie służy do synchronizacji wyścigów w testach.

## Konfiguracja i rzeczywista semantyka DuckDB

| `ImportJobs` | Domyślnie | Dozwolony zakres |
| --- | ---: | --- |
| `MaxConcurrentImports` | 2 | 1–8 na instancję aplikacji |
| `DuckDbThreads` | 1 | 1–4 na prywatną instancję DuckDB |
| `DuckDbMemoryLimitMb` | 256 | 16–4096 MiB na prywatną instancję |
| `DuckDbMaxTempDirectorySizeMb` | 1024 | 0–16384 MiB na prywatną instancję; 0 wyłącza spill |

Nazwy opcji kończą się na `Mb`, lecz wartości są jawnie przekazywane DuckDB jako **MiB**. Zmiana opcji wymaga restartu aplikacji. Dotychczasowe wartości lease 2 min, heartbeat 20 s i limit 3 prób pozostają bez zmian.

Sprawdzono faktycznie używany `DuckDB.NET.Data.Full 1.5.5`. Ustawienia `threads`, `memory_limit`, `max_temp_directory_size` i `temp_directory` mają zakres GLOBAL **instancji bazy DuckDB**. Provider tworzy nową instancję dla zwykłego `:memory:`; połączenia z `Duplicate()` współdzielą instancję. Test natywny potwierdza odrębne ustawienia i tabele dwóch prywatnych baz oraz współdzielenie przez duplikat. Importer nie używa duplikowania ani shared cache. Źródła: [konfiguracja DuckDB 1.5](https://duckdb.org/docs/1.5/configuration/overview), [ConnectionManager providera 1.5.5](https://raw.githubusercontent.com/Giorgi/DuckDB.NET/1.5.5/DuckDB.NET.Data/Connection/ConnectionManager.cs).

Domyślny budżet dwóch importerów wynosi więc łącznie 2 wątki zapytań DuckDB, 512 MiB zarządzanej przez DuckDB pamięci oraz 2 GiB spill. Nie oznacza to ograniczenia całego procesu do dwóch wątków ani 512 MiB RSS. Część alokacji natywnych omija buffer manager; dodatkowo działa runtime .NET oraz inne ścieżki analityczne aplikacji. Twarde ograniczenie pamięci/CPU procesu pozostaje zadaniem środowiska uruchomieniowego. [Dokumentacja problemów OOM](https://duckdb.org/docs/1.5/guides/performance/oom) potwierdza tę granicę.

Katalog spill jest unikalny dla próby i usuwany razem z jej dotychczasowym workspace po zamknięciu DuckDB. Budżet spill nie obejmuje źródłowego CSV, tymczasowej kopii CSV, wynikowego Parquet, uploadów, opublikowanych danych ani pozostawionych artefaktów. Nie jest limitem całego dysku. Nie zmieniono istniejącego procesu uploadu. Potrzebny jest odpowiedni zapas miejsca i monitoring wolumenu.

Test rzeczywistego importera używa deterministycznego CSV około 51 MB: konfiguracja 16 MiB bez spill odrzuca konwersję, a domyślna kończy ją z 50 000 wierszy i dwiema kolumnami. Niskie budżety mogą ograniczyć wcześniej obsługiwane duże importy; operator musi dobrać je do pojemności maszyny i typowych danych. Zachowano dotychczasowe klasyfikowanie błędów oraz ograniczoną liczbę prób.

## Integralność, właściciele i zamykanie aplikacji

Limit jednego Running na właściciela nadal egzekwuje PostgreSQL: transakcyjna blokada doradcza właściciela, świeże sprawdzenie Running po jej uzyskaniu, następnie projekt i job. Różne projekty tego samego właściciela nie omijają limitu. Wstępna selekcja nie stanowi gwarancji własności.

Nie zmieniono `AttemptNumber`, zegara PostgreSQL, lease, heartbeat, recovery, fencing, transakcyjnej finalizacji ani obsługi niepewnego commita w świeżym scope. Przed publikacją nadal obowiązują status, numer próby oraz kolejność blokad AUD-001. Fizycznie zapisany Parquet staje się Ready dopiero razem z publikacją DatasetVersion, CurrentVersionId i Completed w jednej transakcji.

Zatrzymanie hosta anuluje wszystkie sloty, linked CTS konwersji i heartbeatów. Processor oczekuje na zakończenie swojego heartbeat, a worker przez `Task.WhenAll` oczekuje na sloty. Scope i natywne połączenie zwalniają się podczas wychodzenia. `DuckDBCommand.Cancel()` pozostaje mechanizmem best effort; nie dodano zabijania wątków ani arbitralnego usuwania plików. Jeśli proces zostanie przerwany przed zakończeniem, lease/recovery oraz fencing zapewniają dotychczasową ścieżkę odzyskania.

Anulowana lub porzucona próba nie usuwa wcześniej opublikowanych ani niepewnie opublikowanych artefaktów. Przerwane zadanie może pozostać Running do wygaśnięcia lease; blokuje wtedy następny import tego właściciela. To zachowana, bezpieczna semantyka. Znane błędy trwałe nadal kończą się Failed, a nieoczekiwany błąd konwersji korzysta z istniejącego skrócenia lease i limitu prób.

## Pomiar ClaimNextPendingAsync

Środowisko: PostgreSQL **16.15** w Docker Testcontainers, runtime **10.0.11**, lokalne połączenie aplikacja–kontener. Każdy pomiar wykonano wyłącznie na nowej, izolowanej bazie testowej z aktualnymi migracjami. Nie uruchamiano migracji na istniejącej bazie.

Rozkład: 500 właścicieli, 500 projektów/datasetów, 45 000 wersji i jobów, w tym 25 000 historycznych Failed oraz 20 000 zadań kolejki. Wariant zajęty: po jednym Running u pierwszych 450 albo wszystkich 500 właścicieli; pozostałe zadania tych właścicieli nadal Pending. Kolejność celowo stawia zadania zajętych właścicieli przed dostępnymi. Wariant pusty pozostawia historię, lecz nie ma Pending. Wykonano ANALYZE, dwa przebiegi rozgrzewające i dziewięć pomiarów pełnego claim ze świeżym DbContext. Po każdym claim resetowano wyłącznie rekord testowy.

| Scenariusz | Pending / Running | Mediana przed → po [ms] | P95 próbki przed → po [ms] |
| --- | --- | ---: | ---: |
| Wszyscy dostępni | 20 000 / 0 | 15,51 → 12,41 | 30,02 → 27,96 |
| 90% właścicieli zajętych | 19 550 / 450 | 1462,62 → 103,50 | 1566,19 → 154,77 |
| Wszyscy właściciele zajęci | 19 500 / 500 | 56,57 → 61,59 | 74,49 → 81,74 |
| Pusta kolejka | 0 / 0 | 3,67 → 4,23 | 18,64 → 20,09 |

P95 to najbliższa ranga z dziewięciu pomiarów, zatem maksimum tej małej próbki. To mikrobenchmark, nie SLA ani prognoza przepustowości produkcji. Przebieg bazowy współdzielił środowisko z testami workera; plan SQL niezależnie potwierdza źródło kosztu. Małych różnic pozostałych scenariuszy nie należy interpretować jako dowodu przyspieszenia lub istotnej regresji.

Przechwycono faktyczne komendy EF wraz z kopią parametrów i wykonano `EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)`. Mutujący CTE wyjaśniono w transakcji z blokadą właściciela i rollback. Pełne plany i próbki są w dwóch plikach JSON wymienionych wyżej.

Przed optymalizacją wariant 90% miał skorelowany podplan wyszukiwania Running dla kolejnych kandydatów: 17 551 sprawdzeń kandydatów i **895 140** wykonań skanu indeksu `IX_import_jobs_DatasetVersionId`. Sam wybór właściciela zajmował w EXPLAIN 1657,92 ms; CTE claim około 1,38 ms.

Zmiana zastępuje skorelowane `Any()` zapytaniem `IQueryable<Guid>` właścicieli Running i `!Contains(OwnerId)`. Nie materializuje listy do pamięci aplikacji. PostgreSQL użył jednorazowego `hashed SubPlan`; wybór właściciela w tym wariancie zajął 140,19 ms, CTE około 0,94 ms. `Project.OwnerId` jest NOT NULL, więc filtrowanie przez NOT IN zachowuje semantykę. Właściwy warunek NOT EXISTS po blokadzie właściciela nadal działa w osobnym, świeżym poleceniu READ COMMITTED.

Pozostaje przechodzenie po starszych Pending zajętych właścicieli: plan nadal odwiedza 17 551 kandydatów i materializuje dostępne projekty. Nie dodano indeksu, denormalizacji OwnerId ani nowego protokołu kolejki. Obecny indeks Status/CreatedAtUtc/Id jest wykorzystywany. Dalsze zmiany wymagają rozkładu i planów z rzeczywistego obciążenia.

Ponowienie pomiaru: ustawić `APERTURE_IMPORT_CLAIM_REPORT` na ścieżkę pliku wynikowego i uruchomić klasę `AnalyticDashboard.IntegrationTests.Performance.ImportClaimPerformanceTests`. Bez tej zmiennej test publikuje wyniki tylko przez standardowe wyjście testów i nie nadpisuje dokumentacji. Test nie zawiera niestabilnych progów czasu.

## Testy i przegląd

Świeża kompilacja `dotnet build AnalyticDashboard.slnx --no-restore --nologo`: **0 błędów, 0 ostrzeżeń**.

Całe assembly jednostkowe: **22/22**, 0 błędów, 0 pominiętych, 0,199 s.
Całe assembly integracyjne: **407/407**, 0 błędów, 0 pominiętych, 51,227 s. Wszystkie testy PostgreSQL korzystają z istniejącego PostgreSQL Testcontainers. Nie wykluczono klas AUD-001, finalizacji, lease, recovery ani limitu właściciela.

Nowe testy kontrolują:
- równoczesne realne importy przy limitach 1, 2 i 3, brak nadmiarowego claim, unikalne DbContext i brak transakcji podczas konwersji;
- dwa projekty jednego właściciela i niezależną pracę drugiego;
- ponowne użycie slotu po sukcesie, trwałym błędzie, indywidualnym anulowaniu i błędzie SQL po ostatnim UPDATE przed commit;
- rollback całej publikacji i brak StorageKey/metadanych/CurrentVersionId po błędzie;
- odrębne konteksty dwóch heartbeatów, rzeczywiste odnowienie lease w PostgreSQL, recovery/reclaim jednej próby i anulowanie tylko starego workera;
- zachowanie artefaktów oraz poprawną publikację nowszej próby i drugiego, aktywnego importu;
- realny host zatrzymywany przy dwóch aktywnych importach, także po publikacji jednego; dołączenie scope/heartbeatów i skuteczny restart po recovery;
- ograniczony polling z zegarem kontrolowanym przez test;
- natywną semantykę prywatnych baz DuckDB, zastosowanie budżetów oraz odrzucenie błędnej konfiguracji.

Bariery wykorzystują TaskCompletionSource, kanały, kontrolowany TimeProvider i rzeczywiste transakcje PostgreSQL. Testy nie używają Task.Delay ani Thread.Sleep do wymuszania wyścigów. Testowy storage deleguje rzeczywiste odczyty i zapisy na dysk; zatrzymuje operację w jawnych punktach przed publikacją. Błąd finalizacji wstrzykiwany jest po rzeczywiście wykonanym UPDATE PostgreSQL, a nie zamiast transakcji.

Przegląd techniczny: izolacja zasobów, anulowanie, granice transakcji, zachowanie blokad właściciel → projekt → job, brak zmiany fencing i brak usuwania niepewnych artefaktów. Przegląd spójności: istniejący BackgroundService/scoped processor/options, brak nowych warstw i pakietów, konstruktory w testach dostosowane do nowej zależności. Zmiany istniejących testów ograniczono do konstruktora importera.

Diff przedstawia wyłącznie różnicę względem treści plików odczytanych na początku etapu 5B, wraz z nowymi testami i artefaktami. Nie obejmuje wcześniejszych, już obecnych etapów AUD-004. Plik diff nie zawiera samego siebie.

## Ograniczenia przed wdrożeniem

1. Równoległość jest lokalna dla procesu. Dwie instancje z limitem 2 mogą łącznie uruchomić 4 importy różnych właścicieli; limit Running właściciela pozostaje globalny w PostgreSQL.
2. `LocalFileStorage` pozostaje lokalnym katalogiem `storage` względem katalogu pracy. Etap 5B obsługuje równoległość jednej instancji, nie rozwiązuje umiejscowienia plików między hostami. Wspólna baza przy niezależnych dyskach bez affinity pozwala przejąć job bez dostępu do jego CSV; istniejąca obsługa braku pliku może wtedy zakończyć job jako Failed. Nie należy tak wdrażać workerów. Wymagane jest osobno zaprojektowane routowanie do storage lub dostępny storage; nie zakładamy współdzielenia katalogu.
3. Budżety DuckDB nie obejmują całego procesu ani wolumenu. Inne zapytania analityczne, uploady i kopie plików pozostają poza nimi. Wymagane jest strojenie dla maszyny oraz rezerwa pamięci/dysku.
4. Testy weryfikują rzeczywiste równoległe lifecycle i gwarancje bazy, ale nie są długotrwałym testem obciążeniowym dużych CSV ani pomiarem szczytowego RSS całej aplikacji.
5. Twarde zatrzymanie może pozostawić własne workspace/artefakty. Bezpieczne zachowanie pliku przy nieznanym wyniku commita oraz dotychczasowe granice późniejszej rekoncyliacji pozostają bez zmian; nie dodano sweepera.

