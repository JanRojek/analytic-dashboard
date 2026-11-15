# Aperture — wdrożenie Direct COPY w importerze DuckDB

Data: 2026-11-15. Zakres: wdrożenie wariantu zalecanego w [raporcie 10](10-duckdb-import-capacity-experiments.md), bez zmian publicznego API, frontendu, storage, PostgreSQL i protokołu współbieżności. Bez migracji, nowych zależności i commita.

## 1. Wynik i zakres zmian

Produkcja wykonuje bezpośredni eksport `read_csv_auto → COPY Parquet`. Usunięto materializację `imported_dataset`, jej COUNT i eksport tabeli. Zachowano dokładnie wywołanie parsera `read_csv_auto($sourcePath, header = true)`: nie dodano opcji strictness, sample_size, separatora, NULL ani wymuszania typów.

Konfiguracja początkowa:
- `DuckDbMemoryLimitMb = 512` w ImportJobOptions i appsettings.json;
- `ROW_GROUP_SIZE 122880`, `COMPRESSION zstd`, `COMPRESSION_LEVEL 3`;
- jawne `preserve_insertion_order=true`;
- dotychczasowe `DuckDbThreads=1`, `MaxConcurrentImports=2`;
- dotychczasowy limit spill 1024 MiB na prywatną bazę importu.

Jawne nadpisania konfiguracji nadal obowiązują. Wdrożenie z zewnętrznym ustawieniem 256 MiB nie otrzyma automatycznie 512 MiB; wcześniejsze eksperymenty wykazały OOM szerokiego CSV przy 256 MiB i tej grupie. Nie podniesiono dopuszczalnych limitów konfiguracji ani liczby slotów.

Zmiany kodu:

| Plik | Zmiana |
| --- | --- |
| src/AnalyticDashboard.Infrastructure/Services/Import/DuckDbCsvImporter.cs | Direct COPY, jawne ustawienia writera i kolejności, metadane z gotowego Parquet |
| src/AnalyticDashboard.Infrastructure/Services/Import/ImportJobOptions.cs | Domyślne 512 MiB |
| src/AnalyticDashboard.Api/appsettings.json | Zgodne domyślne 512 MiB |
| tests/AnalyticDashboard.IntegrationTests/Import/DuckDbCsvCompatibilityTests.cs | 17 porównawczych przypadków regresyjnych na rzeczywistym DuckDB i filesystemie |
| tests/AnalyticDashboard.IntegrationTests/Import/CtasCsvImporterReference.cs | Zamrożony poprzedni importer, wyłącznie jako punkt odniesienia testów i pomiarów |
| tests/AnalyticDashboard.IntegrationTests/Performance/CsvImportPerformanceTests.cs | Jawny profil capacity i wybór direct/ctas w istniejącym benchmarku |

Referencja CTAS jest kopią implementacji odczytanej przed rozpoczęciem tego zadania, ze zmianą nazwy klasy, namespace i typu loggera. Ma dwóch rzeczywistych konsumentów: testy zgodności i benchmark. Nie jest rejestrowana w DI, kompilowana do produkcji ani dostępna przez przełącznik produkcyjny. Zastąpiła wstępny lokalny helper porównawczy; produkcja ma jedną ścieżkę konwersji.

## 2. Metadane, zasoby i publikacja

Po zakończeniu COPY:
1. `SELECT num_rows FROM parquet_file_metadata($parquetPath)` odczytuje liczbę wierszy ze stopki jednego gotowego pliku.
2. `SELECT * FROM read_parquet($parquetPath) LIMIT 0` wiąże schemat; `reader.FieldCount` daje liczbę kolumn bez materializowania wierszy danych.
3. Dopiero potem wynik trafia do storage i wraca jako dotychczasowy DuckDbCsvImportResult.

Nie ma ponownego pełnego skanu CSV ani Parquet do obliczania liczby wierszy. Metadane odpowiadają faktycznie utworzonemu artefaktowi. Zachowano obecny kontrakt int dla RowCount i jego sprawdzaną konwersję; ten etap nie rozszerza obsługi ponad Int32.MaxValue wierszy. Pozostałe metadane publikacji są nadal ustalane przez istniejący pipeline.

Nie zmieniono prywatnego workspace, osobnej bazy/połączenia DuckDB, kopiowania CSV, unikalnego klucza wyniku, kolejności SaveParquetAsync, rejestracji anulowania komendy ani finally usuwającego własny workspace. Nie dodano usuwania trwałych plików na podstawie wyjątku. Błędna konwersja może utworzyć częściowy plik wyłącznie we własnym workspace; nie dochodzi wówczas do zapisu wyniku w storage.

ImportJobProcessor, worker, repozytoria, claim, limit właściciela, lease, heartbeat, fencing, StorageCleanupJob i atomowa finalizacja DB pozostały bez zmian. Gotowy plik nadal poprzedza publikację Ready; rekoncyliacja niepewnego commita nadal chroni już opublikowany wynik. Pełny zestaw testów tych mechanizmów przeszedł.

## 3. Zgodność semantyki CTAS i Direct COPY

Testy uruchamiają oba rzeczywiste importery na tym samym zapisanym źródle. W poprawnych przypadkach odczytują oba Parquet i porównują:
- RowCount i ColumnCount;
- nazwy kolumn, natywne typy DuckDB oraz typy CLR;
- każdą wartość każdego wiersza w kolejności odczytu, bez sortowania;
- NULL niezależnie od tekstu, bitową reprezentację double/float, precyzję dat/czasów, Unicode i białe znaki.

To porównanie strumieni odczytu, nie sam COUNT ani fingerprint. W dwóch przypadkach 150 000 wierszy wymagane jest także powstanie wielu grup Parquet. Osobno porównano odrzucenie danych: ten sam natywny ErrorType, brak próby zapisu trwałego wyniku i zachowane źródło.

| Przypadki | Wynik |
| --- | --- |
| Puste/cytowane puste pola, NULL, tekst NULL, whitespace, kolumna NULL | Zgodny schemat, wszystkie wartości i kolejność |
| Cytowane separatory, escaped quotes, newline w polu | Zgodność |
| BOM, polski tekst, CJK, emoji, łączone znaki Unicode | Zgodność |
| BIGINT min/max, boolean, double, date, time, timestamp, timestamp with timezone, tekstowe identyfikatory | Zgodność typów i wartości |
| +0/-0, NaN, +/-Inf, bardzo duże i małe double | Zgodność bitowej reprezentacji po odczycie |
| Powtórzone/puste nagłówki, średnik, tabulator, CRLF | Zgodność wykrycia schematu i wartości |
| Sam nagłówek | Zgodność, 0 wierszy |
| 150 000 wierszy, wiele grup, 1 i 4 wątki | Zgodność wszystkich wartości i kolejności |
| Niepoprawny UTF-8; błędne cytowanie/liczba pól po próbkowaniu | Oba odrzucają jako InvalidInput |
| Tekst po 40 000 wierszy kolumny wykrytej jako liczba | Oba odrzucają jako Conversion |
| Błędne cytowanie/liczba pól we wczesnej próbce | Oba zachowują opisane niżej dotychczasowe autodetection |

**Ważna istniejąca cecha parsera:** dwa celowo nieregularne pliki z 2000 logicznych wierszy danych zostały przez oba importery zaakceptowane jako 1999 wierszy i trzy kolumny o nazwach `0`, `"valid`, `quoted value"`. Autodetection zmienia interpretację dialektu/nagłówka. Nie jest to nowa regresja Direct COPY i nie zostało po cichu zmienione na odrzucenie. Późne nieprawidłowe rekordy, poza początkowym próbkowaniem, są odrzucane. Ewentualne zaostrzenie parsowania wymaga osobnej decyzji i testów kontraktu.

17 przypadków przeszło przed zmianą produkcji oraz po niej. Końcowe wykonanie po wprowadzeniu wspólnej zamrożonej referencji również przeszło w pełnym suite. Szczegółowe wyniki: [direct-copy-semantics.json](direct-copy-semantics.json).

Zgodność potwierdzono dla natywnego DuckDB 1.5.5 i tego korpusu. Nie jest to dowód dla dowolnego CSV ani przyszłej wersji silnika. Tekst wewnętrznego wyjątku może zawierać inne SQL/ścieżki przy COPY niż przy CTAS; porównywany jest typ błędu i zachowanie, nie tekst diagnostyczny bajt po bajcie. Mapowanie błędów publicznego API nie zostało zmienione.

Układ Parquet zachowuje docelową wielkość grup 122880 i zstd 3. Zgodność typów/wartości/kolejności oraz istniejące testy analityki ograniczają ryzyko regresji odczytu. Raport 10 zawiera wcześniejsze pomiary odczytów tego układu; w tym etapie nie powtarzano całego benchmarku zapytań analitycznych. Nie deklaruje się identyczności binarnych plików ani identycznego podziału na grupy.

## 4. Pomiary rzeczywistego importera

Środowisko: Windows, .NET 10.0.11, DuckDB 1.5.5, 12 logicznych CPU. Te same deterministyczne źródła, LocalFileStorage, kopie źródła/wyniku i stanowisko pomiarowe dla obu importerów. **Obie ścieżki dostały 512 MiB**, 1 wątek i 1024 MiB spill; porównanie oddziela efekt usunięcia CTAS od podniesienia pamięci.

Dane: 500 MiB / 499 890 wierszy i 1 GiB / 1 023 642 wiersze, po cztery kolumny. Repeated_hash powtarza hash w długiej komórce, independent_hash_blocks ma mniej powtarzalny tekst. To syntetyczny korpus z raportu 10, nie pełna reprezentacja danych użytkowników.

Każda pozycja to trzy powtórzenia. Czas jest medianą całej partii; para oznacza dwa pliki wykonywane równolegle. Pamięć i spill to największe zaobserwowane wartości z trzech prób. Pamięć dotyczy całego procesu testowego, nie przyrostu względem baseline.

| Źródło | Importy w partii | CTAS [s] | Direct [s] | Prywatna pamięć CTAS → Direct [MiB] | Zaobserwowany spill CTAS → Direct [MiB] |
| --- | ---: | ---: | ---: | ---: | ---: |
| 500 MiB repeated | 1 | 4,396 | 2,806 | 670,5 → 514,3 | 232,4 → 0 |
| 500 MiB repeated | 2 | 5,681 | 3,444 | 1318,1 → 1006,0 | 446,5 → 0 |
| 1 GiB repeated | 1 | 8,835 | 5,522 | 841,2 → 529,3 | 537,4 → 0 |
| 1 GiB repeated | 2 | 11,682 | 6,701 | 1453,6 → 1014,1 | 1053,8 → 0 |
| 1 GiB independent blocks | 1 | 22,555 | 17,949 | 872,8 → 532,3 | 939,4 → 0 |
| 1 GiB independent blocks | 2 | 32,037 | 23,032 | 1475,2 → 1015,2 | 1878,9 → 0 |

Dla pary powtarzalnych 1 GiB czas spadł o około 42,6%; dla mniej powtarzalnych 1 GiB o 28,1%. Obie ścieżki ukończyły wszystkie próby: **po 18 partii i 27 konwersji, łącznie 54 sukcesy i 0 OOM**. Brak OOM przy 512 MiB nie unieważnia wcześniejszych wyników OOM przy 256 MiB.

Wynikowy Parquet na jeden import: około 20,01 MiB dla 500 MiB repeated, 40,92 MiB dla 1 GiB repeated i 529,65 MiB dla 1 GiB independent blocks (CTAS około 529,66 MiB). Direct nadal potrzebuje kopii źródła i wyniku. Zaobserwowany workspace pary 1 GiB osiągał około 2124 MiB dla repeated i 3107 MiB dla independent blocks; trwałe źródło i wynik w storage to dodatkowe miejsce.

Ograniczenia pomiaru:
- próbkowanie co 50 ms nie stanowi twardego pomiaru szczytu; maksymalna przerwa wyniosła około 228 ms dla Direct i 1454 ms dla CTAS;
- podczas znikania plików odnotowano 24 niepełne odczyty statystyk dysku dla Direct i 45 dla CTAS; wartości dyskowe są obserwacjami i mogą zaniżać szczyt. „0 spill” oznacza brak zaobserwowanego spill;
- proces i cache systemu nie były izolowane; serie wykonano kolejno, nie równolegle z testami PostgreSQL;
- memory_limit DuckDB nie jest limitem całego procesu. Para nadal wymaga około 1 GiB obserwowanej pamięci prywatnej, plus zapas aplikacji/OS;
- pełny produkcyjny importer używa istniejącego CopyToAsync. Wyników czasu nie należy bezpośrednio porównywać z laboratoryjną kopią plików w raporcie 10;
- brak progów wydajnościowych w CI.

Surowe wyniki: [Direct](direct-copy-production-performance.json), [zamrożony CTAS](direct-copy-ctas-reference-performance.json). Pierwszy raport Direct zapisano przed dodaniem pola Pipeline do serializacji; jego Profile=capacity i Options=512 MiB, a ścieżka Direct jest udokumentowana przebiegiem pomiaru. Następne uruchomienia zapisują również Pipeline.

### Powtórzenie pomiaru

Z katalogu backendu, po build:

```powershell
$env:APERTURE_CSV_IMPORT_PROFILE = "capacity"
$env:APERTURE_CSV_IMPORT_PIPELINE = "direct" # osobne wykonanie: "ctas"
$env:APERTURE_CSV_IMPORT_REPORT = Join-Path (Get-Location).Path "docs/audit/direct-copy-repeat.json"

dotnet test tests/AnalyticDashboard.IntegrationTests/AnalyticDashboard.IntegrationTests.csproj --no-build --no-restore --filter "FullyQualifiedName~Import_ShouldBeMeasuredWithLargeSources_SequentiallyAndInParallel" -- xUnit.Explicit=only xUnit.ParallelizeTestCollections=false

Remove-Item Env:APERTURE_CSV_IMPORT_PROFILE
Remove-Item Env:APERTURE_CSV_IMPORT_PIPELINE
Remove-Item Env:APERTURE_CSV_IMPORT_REPORT
```

Domyślny profil standard pozostaje dostępny. Benchmark nadal jest Explicit; standardowe dotnet test go nie uruchamia. Nie dodano kolejnego ciężkiego testu do zwykłego suite.

## 5. Weryfikacja i przegląd

- Świeży build: 0 błędów, 0 ostrzeżeń.
- Pełne `dotnet test AnalyticDashboard.slnx --no-restore --nologo`: **22/22 jednostkowe, 430/430 integracyjne**, 0 niepowodzeń. Integracje bazodanowe używają PostgreSQL Testcontainers.
- Trzy kosztowne metody pomiarowe pozostały Explicit/NotExecuted w standardowym suite.
- 17 nowych przypadków zgodności wchodzi w 430 integracyjnych; nie jest doliczane drugi raz.
- Benchmark capacity Direct i CTAS wykonano jawnie, osobno od pełnego suite.

Przegląd techniczny: sprawdzono parametry SQL, odczyt stopki i pustego schematu, anulowanie komendy, błąd przed trwałym Save, zachowanie identyfikatorów artefaktów, brak zmian finalizacji oraz porównanie pełnych wartości i kolejności. Usunięte produkcyjne CTAS nie ma pozostawionych konsumentów. Frozen reference ma jawnych konsumentów wyłącznie testowych.

Przegląd spójności: bez nowych serwisów, interfejsów, pakietów i wzorców produkcyjnych. Dotychczasowe Options, IFileStorage, ILogger i zakresy zasobów zachowano. Przy using nie wprowadzono inicjalizatorów obiektów IDisposable. Nie zmieniano niepowiązanych testów.

## 6. Ograniczenia przed uploadami 500 MiB–1 GiB

To gotowa optymalizacja wewnętrznego importera, a nie włączenie dużych uploadów przez HTTP.

1. **Przyjęcie pliku:** frontend nadal ogranicza wybór do 10 MiB, odczytuje tekst całego pliku do preview i korzysta z walidacji 20 000 wierszy; klient ma timeout 30 s. Nie zmieniono żadnego z tych zachowań. Backend nie definiuje własnego większego limitu request/form; wcześniejsza analiza wskazuje domyślne Kestrel 30 000 000 B i multipart 128 MiB. Konfigurację faktycznego reverse proxy/hostingu trzeba zweryfikować przed zmianą limitów.
2. **Budżet pamięci i dysku:** 512 MiB to budżet DuckDB na bazę importu, nie gwarancja maksymalnego RSS. Potrzebne są zapas dla dwóch slotów, przyjęcia uploadów i zapytań analitycznych oraz limity/quota plików. Źródłowe CSV pozostaje w storage; konwersja tworzy drugą kopię CSV, Parquet i ewentualny spill. Brak spill na tym korpusie nie oznacza, że zawsze będzie zbędny.
3. **Trwałość i odzyskiwanie miejsca:** istniejące ustalenia [raportu 09](09-import-polish-and-reliability.md) pozostają aktualne: niepewny commit przy początkowym tworzeniu metadanych może uruchomić kasowanie źródłowego CSV; retencja nieopublikowanych wyników i późny zapis starej próby po cleanup wymagają osobnych poprawek AUD-006–008. Finalizacja wyniku ma już ochronę z AUD-004 i została zachowana.
4. **Rozmieszczenie storage:** ten etap nie zapewnia współdzielenia lokalnych plików między hostami ani poprawnego routingu claim/source/cleanup w takim wdrożeniu.
5. **Reprezentatywność i parser:** należy potwierdzić budżety na rzeczywistych szerokich schematach i dużych komórkach. Zachowano istniejące autodetection błędnych wczesnych rekordów. Nie podniesiono Zstd level ani nie wprowadzono rozluźnienia parsera w celu poprawy benchmarku.
6. **Odczyty:** zachowano kompromis grup/kompresji z raportu 10. Przed innym układem Parquet lub kodekiem potrzebny jest osobny pomiar rzeczywistych zapytań analitycznych.

Pełny [diff do code review](direct-copy-implementation.diff) obejmuje wyłącznie to zadanie względem treści plików odczytanych przed zmianami, nowe testy, ten dokument i wyniki. Nie zawiera samego pliku diff ani wcześniejszych etapów AUD-004. Nie wykonano commita.

