# Aperture — importy: polish pass oraz dalsze prace AUD-006–008

Data: 2026-11-15. Stan po AUD-004, etapach 1–5B. Zmiany ograniczono do jakości testów, diagnostyki i pomiarów. Bez zmian API, frontendu, migracji, pakietów, limitów domyślnych ani protokołu claim/lease/fencing. Bez commita.

## 1. Zmienione pliki i korzyści

| Plik | Konkretna korzyść |
| --- | --- |
| `src/AnalyticDashboard.Infrastructure/Services/Import/ImportJobProcessor.cs` | Rozpoczęcie, wynik publikacji, anulowanie, skrócenie lease, weryfikacja commita i czas próby; bez surowych wyjątków mogących zawierać CSV. |
| `src/AnalyticDashboard.Infrastructure/Repositories/ImportJobRepository.cs` | Log zatwierdzonego recovery z JobId, AttemptNumber, nowym statusem i powodem; bez dodatkowych zapytań. |
| `src/AnalyticDashboard.Infrastructure/Services/Import/ImportJobWorker.cs` | Błąd slotu identyfikowany przez Slot i ErrorType bez ujawniania komunikatu wyjątku. |
| `src/AnalyticDashboard.Infrastructure/Services/Import/ImportJobRecoveryService.cs` | Bezpieczna diagnostyka awarii cyklu recovery. |
| `src/AnalyticDashboard.Infrastructure/Services/Import/DuckDbCsvImporter.cs` | Błąd usunięcia workspace zawiera jego losową nazwę i typ błędu, bez pełnej ścieżki i treści wyjątku. |
| `tests/AnalyticDashboard.IntegrationTests/Performance/ImportClaimPerformanceTests.cs` | Benchmark 45 000 zadań jest testem jawnym; dostosowany konstruktor repozytorium. |
| `tests/AnalyticDashboard.IntegrationTests/Performance/CsvImportPerformanceTests.cs` | Nowy jawny pomiar większych CSV: jedna/dwie konwersje, czas, CPU, pamięć, pliki tymczasowe i kontrolowane wyczerpanie budżetu. |
| `tests/AnalyticDashboard.IntegrationTests/Import/ImportDiagnosticsTests.cs` | 7 scenariuszy potwierdzających identyfikację próby, poprawne sygnały diagnostyczne, zachowanie DB i brak danych prywatnych w logach. |
| `tests/AnalyticDashboard.IntegrationTests/Concurrency/PostgresLockObserver.cs` | Jedna mała bariera obserwująca rzeczywistą blokadę PostgreSQL, z odstępem 10 ms między odczytami. |
| `tests/AnalyticDashboard.IntegrationTests/Concurrency/AtomicImportClaimTests.cs` | Wspólna bariera zamiast kopii intensywnie odpytującej bazę; nowy argument loggera. |
| `tests/AnalyticDashboard.IntegrationTests/Concurrency/ImportFinalizationTests.cs` | Jak wyżej; zachowane wszystkie scenariusze i asercje finalizacji. |
| `tests/AnalyticDashboard.IntegrationTests/Concurrency/ImportOwnerLimitTests.cs` | Wspólna bariera i logger; osobna obserwacja czasu PostgreSQL pozostaje lokalna. |
| `tests/AnalyticDashboard.IntegrationTests/Concurrency/ImportLeaseTests.cs` | Wyłącznie przekazanie NullLogger do istniejącego konstruktora repozytorium. |
| `tests/AnalyticDashboard.IntegrationTests/Concurrency/ParallelImportWorkerTests.cs` | Wyłącznie przekazanie NullLogger do istniejącego konstruktora repozytorium. |

Artefakty: ten raport, [surowe próbki CSV](import-polish-csv-performance.json), [pełny diff](import-polish.diff).

Wyszukano rzeczywiste użycia konstruktorów i usuwanych metod w solution. Usunięto trzy prywatne kopie WaitForBlockerAsync; wszystkie wywołania importowe używają wspólnego helpera. Npgsql pozostaje potrzebny w tych klasach do identyfikatorów połączeń i innych poleceń. Nie zmieniono asercji współbieżności ani protokołu blokad.

Nie ujednolicono na siłę seedów ani dwóch zegarów testowych: scenariusze przygotowują różne stany, a PulseTimeProvider i ManualClock mają różne funkcje. Dalsze wydzielanie ukrywałoby istotne warunki testów. Nie usunięto testu regresyjnego budżetu DuckDB z 50 000 wierszy; sprawdza egzekwowanie limitu, a nie mierzy przepustowość. Nie dodano testowego frameworka, pakietu benchmarkowego ani nowego projektu.

## 2. Standardowe testy i jawne benchmarki

Oba benchmarki w katalogu Performance mają `[Fact(Explicit = true)]` oraz `Category=Performance`. xUnit v3 domyślnie nie wykonuje testów jawnych; sam Trait nie zapewniał tego wcześniej. [Dokumentacja xUnit dla VSTest](https://xunit.net/docs/config-runsettings) opisuje opcję Explicit i domyślne off.

Standardowy przebieg z katalogu backendu:

```powershell
dotnet test AnalyticDashboard.slnx
```

Weryfikację wykonano bez filtra wykluczającego benchmarki:

```powershell
dotnet test AnalyticDashboard.slnx --no-restore --nologo --logger "trx;LogFilePrefix=import-polish" --results-directory "$env:TEMP/aperture-import-polish-results"
```

Wyniki: **22 testy jednostkowe zaliczone, 413 integracyjnych zaliczonych, 0 błędów**. W TRX oba benchmarki mają osobne wyniki **NotExecuted**; podsumowanie użytej wersji adaptera raportuje 413 wykonanych i nie dolicza testów jawnych do tego licznika. Zachowano wszystkie dotychczasowe scenariusze AUD-001/AUD-004. Testy DB używały wyłącznie PostgreSQL Testcontainers.

Świeży build `dotnet build AnalyticDashboard.slnx --no-restore --nologo`: **0 błędów, 0 ostrzeżeń**, w tym brak ostrzeżeń analizatorów. W trakcie dopracowania usunięto xUnit2029 w nowej asercji benchmarku.

Jawne uruchomienie obu benchmarków, z wyłączoną równoległością klas pomiarowych:

```powershell
dotnet test tests/AnalyticDashboard.IntegrationTests/AnalyticDashboard.IntegrationTests.csproj --no-restore --filter "Category=Performance" --logger "console;verbosity=normal" -- xUnit.Explicit=only xUnit.ParallelizeTestCollections=false
```

Wynik CSV do pliku:

```powershell
$env:APERTURE_CSV_IMPORT_REPORT = Join-Path (Get-Location).Path "docs/audit/import-polish-csv-performance.json"
dotnet test tests/AnalyticDashboard.IntegrationTests/AnalyticDashboard.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~CsvImportPerformanceTests" -- xUnit.Explicit=only xUnit.ParallelizeTestCollections=false
Remove-Item Env:APERTURE_CSV_IMPORT_REPORT
```

Opcjonalny eksport pełnych planów benchmarku kolejki nadal obsługuje `APERTURE_IMPORT_CLAIM_REPORT`. Bez zmiennych eksportu benchmarki piszą przez ITestOutputHelper i nie nadpisują dokumentacji. Jawny benchmark kolejki przeszedł; po dopracowaniu obserwatora plików osobno przeszedł pełny benchmark CSV. Początkowy przebieg CSV ujawnił OOM dużego wariantu oraz wyścig obserwacji usuwanego katalogu spill w Windows. Obserwator teraz oznacza niepełne odczyty, zamiast przerywać pomiar; budżetowy OOM jest jawnie raportowany tylko w wariancie stress. Inne błędy nadal przerywają benchmark, a warianty podstawowe wymagają sukcesu wszystkich importów.

## 3. Konfiguracja DuckDB i pomiary CSV

Nie zmieniono ustawień:
- 2 równoległe importy na instancję;
- 1 wątek zapytań na prywatną instancję DuckDB;
- 256 MiB memory_limit na instancję;
- 1024 MiB max_temp_directory_size na instancję;
- osobny workspace i katalog spill dla każdej próby.

Sprawdzono rzeczywiste DuckDB **v1.5.5**, .NET **10.0.11**, Windows, **12 logicznych procesorów**. Pomiar działa bez HTTP i PostgreSQL: obejmuje odczyt źródła ze storage, kopię roboczą, CSV → tabela → Parquet i zapis wyniku. Nie obejmuje generowania/uploadu źródła, claim, heartbeat, finalizacji SQL ani limitów uploadu HTTP. Produkcyjny worker ma osobne testy wszystkich tych granic.

Po rozgrzewce wykonano po trzy pomiary dla każdego wariantu i liczby importerów. Para oznacza dwa identyczne źródła konwertowane równocześnie przez osobne instancje importera i DuckDB. Mierzone są całe partie; czas pary nie jest czasem pojedynczego pliku. Dane są deterministyczne:
- tall_numeric: 1 000 000 wierszy, 5 kolumn, 51,34 MiB CSV;
- wide_text: 100 000 wierszy, 4 kolumny, 100,03 MiB CSV;
- wide_text_stress: 150 000 wierszy, 4 kolumny, 150,10 MiB CSV.

Tekst zawiera unikalny hash wiersza powtórzony 16 razy, więc dobrze kompresuje się wewnątrz wiersza. Nie jest to próbka niekompresowalnych danych produkcyjnych ani gwarancja maksymalnego obsługiwanego rozmiaru CSV.

| Źródło | Importy naraz | Mediana partii [s] | Mediana CPU procesu [s] | Sukcesy / próby | Przepustowość udanych danych [MiB/s] |
| --- | ---: | ---: | ---: | ---: | ---: |
| 51,34 MiB, tall_numeric | 1 | 1,365 | 1,828 | 3 / 3 | 37,62 |
| 51,34 MiB, tall_numeric | 2 | 1,523 | 3,234 | 6 / 6 | 67,42 |
| 100,03 MiB, wide_text | 1 | 1,161 | 1,453 | 3 / 3 | 86,15 |
| 100,03 MiB, wide_text | 2 | 1,309 | 2,922 | 6 / 6 | 152,84 |
| 150,10 MiB, stress | 1 | 1,190 | 1,438 | 0 / 3, OutOfMemory | 0 |
| 150,10 MiB, stress | 2 | 1,524 | 3,391 | 0 / 6, OutOfMemory | 0 |

CPU jest sumą czasu wszystkich wątków procesu, dlatego może przekraczać czas ścienny. W tym środowisku udane pary zwiększyły przepustowość około 1,8 raza. Nie jest to gwarancja skalowania innych maszyn i rozkładów danych.

| Źródło / równoległość | Maks. próbkowany Working Set [MiB] | Maks. pamięć prywatna [MiB] | Maks. workspace razem [MiB] | Maks. spill [MiB] |
| --- | ---: | ---: | ---: | ---: |
| tall_numeric / 1 | 154,27 | 156,77 | 52,79 | 0 |
| tall_numeric / 2 | 248,01 | 294,08 | 105,23 | 0 |
| wide_text / 1 | 334,10 | 395,09 | 179,08 | 78,91 |
| wide_text / 2 | 615,41 | 749,05 | 360,53 | 160,47 |
| stress / 1 | 421,54 | 465,36 | 274,04 | 124,22 |
| stress / 2 | 692,59 | 812,41 | 563,70 | 264,00 |

Kolumny przedstawiają maksimum z trzech próbek, nie sumę osobnych maksimów. Workspace obejmuje kopie CSV, lokalny Parquet i spill; spill stanowi jego część. Osobno zapisano w JSON wielkość storage i wyników Parquet. Dla wide_text wynik wynosił około 4,04 MiB na plik, dla tall_numeric 1,44 MiB; stress nie opublikował plików wynikowych.

Pamięć pochodzi z Process.WorkingSet64 / PrivateMemorySize64, próbkowanych nominalnie co 50 ms. To cały proces testowy, bez GC/cache reset między próbkami, nie wyłącznie alokacje DuckDB i nie twardy szczyt RSS. JSON zawiera baseline, liczbę obserwacji i maksymalny odstęp; było co najmniej 19 obserwacji na próbę, największy odstęp 75,76 ms.

Rozmiary plików są logicznymi długościami FileInfo.Length, nie pomiarem fizycznych sektorów ani liczby bajtów I/O. Sampler obserwuje nowo powstałe katalogi o prefiksie importera, z wyłączeniem istniejących przed próbą; benchmark należy uruchamiać bez innych importów w tym samym katalogu TEMP. Znikające pliki i stan delete-pending mogą uniemożliwić odczyt: odnotowano łącznie 4 niepełne odczyty katalogów. Dlatego maksima dyskowe są dolnymi oszacowaniami. Po każdej partii sprawdzono brak pozostawionych nowych workspace, a własny storage benchmarku usunięto.

**Wniosek:** zachować konserwatywne wartości domyślne. Udane pary wide_text potrzebowały około 749 MiB pamięci prywatnej całego procesu, mimo nominalnych 512 MiB budżetów. memory_limit nie obejmuje wszystkich alokacji; [dokumentacja DuckDB](https://duckdb.org/docs/1.5/guides/performance/oom) wyjaśnia tę granicę. Dysk musi pomieścić również źródła, kopie, wyniki i pozostawione artefakty. Twarde limity procesu/wolumenu pozostają zadaniem środowiska.

OOM stress wystąpił podczas COPY do Parquet, przy żądaniu dodatkowej alokacji około 100 MiB. Sam spill nie gwarantuje powodzenia każdego kształtu danych. Nie zwiększono domyślnej pamięci i nie zmieniono preserve_insertion_order, liczby/granulacji row group ani formatu wyniku. Ewentualne strojenie row group wymaga osobnego porównania pamięci, kompresji, czasu importu i odczytów analitycznych; nie jest częścią tego polish pass.

## 4. Diagnostyka i niezmienione gwarancje

Logi procesu identyfikują ImportJobId i AttemptNumber. Rozpoczęcie próby z numerem większym od 1 oznacza ponowienie; recovery dodatkowo podaje lease_expired albo attempt_limit_reached i zatwierdzony status Pending/Failed.

Logowane są:
- rozpoczęcie próby;
- potwierdzona publikacja lub rozpoznanie już zatwierdzonego wyniku, z liczbą wierszy/kolumn;
- utrata własności i odrzucenie operacji przez istniejące warunki fencing/lease;
- anulowanie wskutek zatrzymania hosta;
- niespodziewany błąd, skrócenie lease dla istniejącego recovery, nieudany heartbeat;
- rozpoczęcie weryfikacji niepewnej finalizacji w świeżym scope i jej potwierdzenie;
- zakończenie wykonywania próby oraz ElapsedMilliseconds mierzone monotonicznym TimeProvider.

„Finished processing” oznacza zakończenie wykonywania próby, także po błędzie/anulowaniu. Samo to zdarzenie nie jest potwierdzeniem Completed. Potwierdzenie sukcesu ma osobny komunikat dopiero po finalizacji albo skutecznej rekoncyliacji.

Scope logowania dołącza JobId/AttemptNumber do diagnostyki współpracujących komponentów, jeżeli provider wyświetla scopes. Istotne komunikaty procesora i repozytorium zawierają te pola także bez scopes. Nie włączono globalnego logowania scope w konfiguracji.

Nie przekazuje się surowych wyjątków ani ich Message do zmienionych logów importowych: błąd parsera może zawierać CSV, a błąd pliku pełną ścieżkę lub nazwę użytkownika systemu. Pozostają ErrorType i, dla konwersji, enum DuckDbErrorType. To świadomy kompromis: mniej informacji niż pełny stack trace, ale bez niekontrolowanych danych wejściowych. Nie zmieniono zapisywanego ErrorMessage joba ani odpowiedzi HTTP. Nie jest to audyt wszystkich loggerów aplikacji i middleware.

Testy diagnostyki sprawdzają realny PostgreSQL i natywną konwersję, w tym rzeczywisty recovery/reclaim do AttemptNumber=2 przed próbą publikacji starego workera. Sprawdzają też brak prywatnej nazwy pliku, klucza i wartości CSV w komunikatach oraz brak dołączonego obiektu wyjątku.

Nie zmieniono SQL claim, odnowienia/porzucenia lease, recovery ani finalizacji; logger recovery działa po commit. Nie dodano zapytań do mierzenia/logowania statusu. Kolejność właściciel → projekt → job, brak transakcji podczas konwersji, osobne konteksty heartbeat, anulowanie i zachowywanie niepewnych artefaktów pozostają bez zmian.

## 5. Oddzielna analiza AUD-006–008: aktualny stan

### AUD-006 — częściowo rozwiązany, pozostaje retencja i rekoncyliacja artefaktów

**Rozwiązane względem starego audytu:** usunięcie datasetu oraz projektu atomowo rejestruje StorageCleanupJob; cleanup usuwa cały prefiks datasets/{DatasetId}, a więc CSV i wszystkie próby Parquet niezależnie od formatu. Przy Pending/Running usunięcie jest odrzucane pod blokadą projektu. Dowody: DatasetDeletionStore.cs:59–109, ProjectDeletionStore.cs:48–96, StorageCleanupProcessor.cs:40–75, StorageCleanupQueue.cs:18–23 w projekcie Infrastructure.

**Potwierdzone pozostałości:** importer tworzy unikalny wynik (DuckDbCsvImporter.cs:30–31), zachowuje go po niepewnej finalizacji/utracie własności, a recovery nie usuwa plików. Nie ma sprzątania poszczególnych nieopublikowanych prób podczas życia datasetu. CSV po Completed/Failed nadal istnieje; samo to nie jest błędem bez decyzji o retencji. Nagłe zabicie procesu może pozostawić workspace poza storage; finally i log błędu nie stanowią trwałego cleanup. Obecny StorageCleanupJob umie wskazać wyłącznie cały dataset.

**Ryzyko późnego zapisu po DELETE — mechanizm potwierdzony w kodzie, wymaga dedykowanego testu przeplotu:** stary worker może pracować mimo wygaśnięcia lease, recovery osiąga Failed, DELETE przestaje widzieć Pending/Running, cleanup usuwa prefiks i swój rekord, a stara próba dopiero potem wykonuje SaveParquetAsync. Może odtworzyć katalog już usuniętego datasetu. Fencing uniemożliwia publikację w PostgreSQL, lecz nie cofnie zapisu pliku. Dowody: ImportJobRepository.cs:405–496; DuckDbCsvImporter.cs:43–63 i 204–223; StorageCleanupProcessor.cs:74–75; LocalFileStorage.cs:30–45. Nie stwierdzono na tej podstawie możliwości usunięcia Parquet nadal wskazywanego przez żywy dataset; problemem jest osierocenie późnego wyniku.

### AUD-007 — pierwotny problem rozwiązany przez AUD-004

ImportJobRecoveryService działa przy starcie i cyklicznie podczas pracy. ImportJobRepository.RecoverNextExpiredAsync używa zegara PostgreSQL, warunków statusu/próby i blokad. Wygasłe próby są ponawiane albo kończone po limicie. Nieoczekiwany błąd konwersji skraca lease dopiero po zatrzymaniu heartbeat (ImportJobProcessor.cs:73–107).

Błąd finalizacji lub zapisu Failed może pozostawić Running do wygaśnięcia lease, lecz nie bezterminowo przy działającym recovery i dostępnej bazie. To bezpieczne zachowanie przy niepewnym wyniku, nie potrzeba drugiego retry systemu. Aktualne testy ImportLeaseTests, ImportOwnerLimitTests i ParallelImportWorkerTests pokrywają te gwarancje. Nie proponuje się osobnej naprawy AUD-007. Szybsze odzyskiwanie po udowodnionym rollback jest opcjonalnym usprawnieniem, nie powodem do automatycznego ponowienia finalizacji.

### AUD-008 — finalizacja zabezpieczona; przyjęcie uploadu nadal wymaga naprawy

**Finalizacja zabezpieczona:** po błędzie CompleteAsync processor sprawdza dokładny JobId, AttemptNumber, status, StorageKey i metadane w świeżym scope. Nie wykonuje ponownie publikacji i nie usuwa wyniku, gdy nie uda się potwierdzić commita. Dowody: ImportJobProcessor.cs:214–258, ImportJobRepository.cs:253–273; testy CommitReconciliation_ShouldUseAFreshContext_AndNeverRepublish i Processor_ShouldVerifyActualCommittedResult_WhenCommitAcknowledgementThrows.

**Potwierdzone niebezpieczne zachowanie przy tworzeniu metadanych:** ImportCsvDatasetHandler.cs:111–141 obejmuje zapis źródła i CreateAsync jednym try/catch, a catch bezwarunkowo wywołuje DeleteAsync źródła. PendingDatasetImportRepository.cs:23–30 wykonuje SaveChangesAsync bez późniejszej rekoncyliacji. Jeśli commit metadanych się powiódł, ale jego potwierdzenie zaginęło, handler może usunąć CSV wskazywany przez skutecznie zapisany job. Nieznany wynik commita jest ryzykiem protokołu; w tym polish pass nie dodano nowego fault injection tej ścieżki. Zachowanie catch jest potwierdzone analizą implementacji.

## 6. Mały plan naprawy AUD-006–008 — jeszcze niewdrożony

1. **Najpierw ochrona źródła przy tworzeniu metadanych (AUD-008).** Oddzielić porażkę zapisu pliku od wyniku zapisu SQL. Przez istniejący IPendingDatasetImportRepository uzyskać wynik znany/nieznany i weryfikację dokładnych, już wygenerowanych identyfikatorów w świeżym kontekście. Nie ponawiać nieidempotentnego CreateAsync i nie usuwać CSV po samym wyjątku. Cleanup dopuszczać po udowodnionym braku zatwierdzenia; przy nieznanym stanie zachować plik. Pliki: ImportCsvDatasetHandler, IPendingDatasetImportRepository, PendingDatasetImportRepository. Testy Testcontainers: wyjątek po rzeczywistym commit, niedostępność weryfikacji, pewny rollback FK, anulowanie. Akceptacja: committed Pending nigdy nie traci źródła, brak drugiego joba. Nie wymaga nowej infrastruktury.

2. **Inwentaryzacja i decyzja o retencji (AUD-006), początkowo bez kasowania.** Rozróżnić CSV, referencjonowany Parquet wszystkich Ready DatasetVersion, nieopublikowane próby i workspace. Nie ograniczać sprawdzania referencji do Dataset.CurrentVersionId: poprzednia Ready wersja nadal jest opublikowana. Dla każdego kandydata zapisać powód i dowód kwalifikacji. Nie traktować samego wieku, braku CurrentVersion ani wygaśnięcia lease jako dowodu osierocenia. Nie usuwać nieznanego katalogu uploadu tylko dlatego, że jeszcze nie ma metadanych — upload może poprzedzać SQL.

3. **Mały cleanup konkretnych artefaktów terminalnych datasetów (AUD-006).** Rozszerzyć istniejący StorageCleanupJob o jawny, ograniczony rodzaj celu i StorageKey; nie dodawać drugiej kolejki. To przyszła, osobna migracja. Kandydat musi być w kanonicznym prefiksie właściwego datasetu. Rejestracja i ponowna kontrola przed fizycznym usunięciem powinny respektować blokadę projektu, brak Pending/Running i wszystkie referencje wersji/jobów. Przy nieznanym stanie DB plik pozostaje. Całego prefiksu wolno używać tylko dla zatwierdzonego usunięcia datasetu. Testy: stary Ready zachowany, aktualny Ready zachowany, aktywna próba pominięta, niewskazany terminalny Parquet usunięty, błąd filesystemu i ponowienie, wyścig z publikacją. Retencję CSV po Completed/Failed trzeba wcześniej zatwierdzić.

4. **Osobno domknąć późne zapisy i workspace po awarii.** Deterministycznie zatrzymać stary importer przed SaveParquetAsync, wyczerpać próby, usunąć dataset i wykonać cleanup, następnie wznowić stary zapis. Rozszerzyć trwałą intencję usunięcia/rekoncyliację w istniejącym mechanizmie, tak aby późno odtworzony prefiks był ponownie zauważony. Nie usuwać tombstone tylko na podstawie wygaśnięcia lease: nie dowodzi to zakończenia starego procesu. Dla lokalnych workspace najpierw potrzebny jest dowód własności i zakończenia próby; sama nazwa/pora utworzenia nie wystarcza. Testować także DELETE projektu, host shutdown, utratę commita i retry cleanup. Nie obiecywać ograniczonego czasu odzyskania całego miejsca bez polityki maksymalnego życia prób albo trwałości tombstone.

W powyższym planie decyzji wymagają: retencja źródłowego CSV/historii oraz okres i warunek wygaszania obserwacji po DELETE. Wdrożenie wielu hostów z lokalnymi dyskami wymaga osobno poprawnego umiejscowienia source/output/cleanup; ten plan nie zakłada wspólnego filesystemu. Nie wolno wdrażać ogólnego sweepera usuwającego pliki według wieku i braku rekordu, bez rozstrzygnięcia tych granic.

## 7. Przegląd końcowy i świadomie pozostawiony zakres

Przegląd techniczny objął cały diff: brak zmian SQL/protokołu współbieżności, zachowanie join heartbeat przed cooldown, brak sprzątania niepewnych wyników, log sukcesu dopiero po potwierdzonej finalizacji, bezpieczne zasoby benchmarku i kontrolowane wyjątki pomiarowe.

Przegląd spójności: istniejące ILogger<T>, TimeProvider, Options, scoped processor i wzorce Testcontainers; brak nowych serwisów produkcyjnych i interfejsów. Wspólny helper testowy rozwiązuje konkretną duplikację i polling. Usunięte metody nie pozostawiły konsumentów ani zbędnych zależności.

Nie zmieniono DeleteDataset, DeleteProject, przyjęcia uploadu, StorageCleanupJob/worker, polityki retencji, protokołu plików, domyślnych budżetów ani możliwości równoległości. Nie wprowadzono sweepera, outbox, retry frameworka ani migracji. Raporty i plany stage 5B pozostały historycznymi artefaktami wcześniejszego etapu.

Pełny diff przedstawia tylko zmiany tego polish pass względem stanu odczytanego na jego początku; nie zawiera wcześniejszych zmian AUD-004 ani samego pliku diff.
