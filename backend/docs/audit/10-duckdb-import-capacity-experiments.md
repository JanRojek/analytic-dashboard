# Aperture — eksperymenty pojemności i wydajności importera DuckDB

Data: 2026-11-15. Cel: CSV 500 MiB i 1 GiB przy kontrolowanym zużyciu pamięci oraz zachowaniu jakości odczytów Parquet.

**Wynik:** konwersje 500 MiB i 1 GiB zostały wykonane poprawnie, także parami, bez tabeli pośredniej i z ograniczeniami pamięci DuckDB. Nie oznacza to gotowej obsługi takich uploadów w aplikacji. Kod produkcyjny, jego limity, publiczne API, PostgreSQL i frontend pozostały bez zmian. Nie wykonano commita.

## 1. Najważniejsze wnioski

1. OOM szerokiego CSV 150 MiB powtórzył się zarówno z tabelą, jak i przy bezpośrednim COPY, jeśli zachowano memory_limit=256 MiB i domyślną grupę około 122 880 wierszy. **Wszystkie 49 zarejestrowanych OOM wystąpiły podczas write_parquet.** Usunięcie tabeli jest korzystne, ale samo nie usuwa tego wąskiego gardła.
2. Grupy 16–64 tys. wierszy umożliwiły eksport 1 GiB przy 256 MiB memory_limit. Podniesienie tego limitu do 1 GiB nie było konieczne dla bezpośredniego eksportu z małymi grupami.
3. Bardzo małe grupy pogarszają pełne agregacje. Dla 1 GiB grupa 16k zwiększyła czas agregacji z około 4,14 do 7,48 ms przy czterech wątkach odczytu; przy jednym z 10,32 do 21,28 ms. Nie należy wybierać jej jako uniwersalnego ustawienia wyłącznie na podstawie udanego importu.
4. **Konserwatywny kandydat do osobnej implementacji:** bezpośredni COPY, memory_limit=512 MiB, domyślne ROW_GROUP_SIZE=122880, zstd level 3, preserve_insertion_order=true, 1 wątek na import i dotychczasowe 2 importy na instancję. Wykonał 1 GiB także dla danych słabiej kompresowalnych, zachowując podobną wydajność agregacji jak obecny układ Parquet.
5. Zstd level 1 jest obiecującym kolejnym krokiem. Na danych o niezależnych blokach hash skrócił eksport 1 GiB z około 11,58 do 3,26 s przy tej samej domyślnej grupie i 512 MiB. Nie uzyskano jednak jednolitego przyspieszenia wszystkich zapytań; wymaga akceptacji na rzeczywistym korpusie.
6. preserve_insertion_order=false nie rozwiązało samo OOM domyślnych grup. Przy czterech wątkach i małych grupach przestawiło 141 457 z 150 042 wierszy. Obecne stronicowanie opiera się na LIMIT/OFFSET bez ORDER BY.
7. Obsługę dużych plików blokują również UI, timeout klienta, limity Kestrel/formularza oraz budżet miejsca na źródłowe CSV, kopie robocze i Parquet. Podniesienie samego RAM nie uruchomi funkcjonalności 500 MiB/1 GiB.

## 2. Metodyka, zakres zmian i odtwarzalność

Środowisko: Windows, .NET runtime 10.0.11, DuckDB.NET.Data.Full / natywne DuckDB 1.5.5, 12 logicznych CPU, około 32 GB zainstalowanego RAM (system widział 31,32 GiB), około 13,43 GiB wolnego RAM na początku. Na woluminie testowym było około 511 GiB wolnego miejsca. Nie kontrolowano innych procesów użytkownika ani nie opróżniano cache systemu.

Dodano wyłącznie jawny benchmark w `tests/AnalyticDashboard.IntegrationTests/Performance/CsvImportTuningExperiments.cs`. Istniejąca klasa CsvImportPerformanceTests została oznaczona partial, aby współdzielić dotychczasową obserwację rozmiarów plików i pozostawić wszystkie metody pomiarowe poza produkcją.

Stanowisko:
- tworzy prywatne wejście, workspace, spill, plik Parquet i jego kopię odpowiadającą zapisowi do storage;
- używa osobnej prywatnej bazy DuckDB i połączenia na każdą konwersję;
- atomowo zwalnia barierę rozpoczęcia obu konwersji przed równoległą pracą;
- ustawia na próbę memory_limit=256/512/1024 MiB, max_temp_directory_size=1024 MiB oraz badane parametry;
- nie łączy się z PostgreSQL i nie publikuje DatasetVersion; testy regresyjne produkcyjnego pipeline'u uruchomiono osobno;
- ma miękki monitor bezpieczeństwa: 5 GiB pamięci prywatnej procesu i 16 GiB plików partii, z anulowaniem aktywnych komend. Nie jest to twardy limit OS. Żaden monitor nie zadziałał; największa obserwowana pamięć prywatna wyniosła około 2534 MiB;
- usuwa wyłącznie własne katalogi, z kontrolą ich absolutnych ścieżek.

Przeprowadzono **208 partii, 292 konwersje, 90 konfiguracji**: 243 konwersje udane i 49 OOM. OOM jest wynikiem pomiaru, nie ukrytym sukcesem importu. Nie było innych zaakceptowanych błędów. W 75 udanych wariantach wykonano dodatkowe próby odczytów.

Dane:
- 150 MiB, 150 042 wiersze;
- 500 MiB, 499 890 wierszy;
- 1 GiB, 1 023 642 wiersze;
- po 4 kolumny: Id BIGINT, Category VARCHAR, Payload VARCHAR długości 1024, Value DOUBLE;
- wariant repeated_hash powtarza jeden 64-znakowy hash 16 razy w wierszu;
- wariant independent_hash_blocks ma 16 niezależnych bloków hash w wierszu, ograniczając powtarzalność tekstu.

Każdy plik osiąga co najmniej docelową liczbę bajtów; przekroczenie wynosi najwyżej jeden pełny wiersz. Wariant słabiej kompresowalny nadal składa się z tekstu szesnastkowego — nie jest niekompresowalną losową zawartością. Oba są syntetyczne, bez NULL, wielowierszowych pól i nieregularnych danych. Nie dowodzą obsługi każdego CSV o danej liczbie bajtów, szczególnie bardzo szerokich pojedynczych wierszy/komórek.

Screening 150 MiB: 3 powtórzenia na konfigurację. Pozostałe serie: 2 powtórzenia. Mediana dwóch powtórzeń jest ich średnią; pełne min/max i próbki pozostają w artefaktach. Brak benchmarkowych progów czasowych w CI.

Sprawdzono liczby wierszy z metadanych Parquet, schemat, sumę Id, łączną długość Payload i 64-bitowy fingerprint zbioru wszystkich kolumn. Fingerprint był zgodny we wszystkich układach/compression dla danego źródła. Jest dodatkową kontrolą eksperymentalną, nie dowodem kryptograficznym. Przy preserve=true wszystkie sprawdzone wyjścia miały zero przemieszczeń kolejności.

### Uruchomienie

Z katalogu backendu, po build:

```powershell
$env:APERTURE_DUCKDB_TUNING_MODE = "screen"
$env:APERTURE_DUCKDB_TUNING_REPORT = Join-Path (Get-Location).Path "docs/audit/duckdb-tuning-screen.json"

dotnet test tests/AnalyticDashboard.IntegrationTests/AnalyticDashboard.IntegrationTests.csproj --no-build --no-restore --filter "FullyQualifiedName~Tuning_ShouldCompareBoundedConversionAndParquetReads" -- xUnit.Explicit=only xUnit.ParallelizeTestCollections=false

Remove-Item Env:APERTURE_DUCKDB_TUNING_MODE
Remove-Item Env:APERTURE_DUCKDB_TUNING_REPORT
```

Tryby i wyniki:
- [screen](duckdb-tuning-screen.json): 84 partie, 150 MiB; pamięć, CTAS/direct, grupy, kodeki, porządek, wątki;
- [scale](duckdb-tuning-scale.json): 52 partie, 500 MiB/1 GiB; CTAS, direct, 256/512/1024 MiB, jedna/dwie konwersje;
- [balance](duckdb-tuning-balance.json): 16 partii, grupy 32k i zstd 1/3;
- [tradeoff](duckdb-tuning-tradeoff.json): 24 partie, grupy 64k przy 256/512 MiB oraz domyślna grupa przy 512 MiB;
- [entropy](duckdb-tuning-entropy.json): 24 partie, 1 GiB mniej powtarzalnego tekstu;
- [codec](duckdb-tuning-codec.json): 8 partii, zstd level 1 z domyślną grupą i 512 MiB, oba rodzaje 1 GiB.

Zestawienie wszystkich 90 konfiguracji: [CSV z wynikami zbiorczymi](duckdb-experiments-summary.csv). Do ponowienia trybu należy ustawić odpowiednią nazwę i odrębną ścieżkę raportu. Ogólny filtr klasy CsvImportPerformanceTests obejmuje teraz również nowy eksperyment; do selektywnego uruchomienia używać nazwy metody jak powyżej.

## 3. Pamięć, grupy i tabela pośrednia

### Screening OOM 150 MiB

| Przepływ i grupa | 256 MiB | 512 MiB | 1024 MiB |
| --- | --- | --- | --- |
| Tabela pośrednia, 122880 | 0/9 sukcesów | 9/9 | 9/9 |
| Bezpośredni COPY, 122880 | 0/9 | 9/9 | 9/9 |
| Tabela pośrednia, 16384 | 9/9 | nie badano | nie badano |
| Bezpośredni COPY, 16384 | 9/9 | nie badano w screen | nie badano w screen |

Dziewięć prób oznacza 3 pojedyncze i 3 pary. Nie porównuje się czasu nieudanego importu z udanym jako „przyspieszenia”.

ROW_GROUP_SIZE jest celem/progiem, nie dokładnym górnym limitem pamięci. Dla bezpośredniego COPY grupy ustawione na 16384 miały do 17326 wierszy; domyślne dochodziły do około 124 tys. Bajty zależą od szerokości kolumn i buforów writera. [DuckDB: Parquet Tips](https://duckdb.org/docs/1.5/data/parquet/tips) opisuje kompromis między kompresją, pamięcią grup i narzutem odczytu metadanych.

Zwiększenie pamięci umożliwiło aktualny CTAS + duże grupy, ale utrzymuje koszt materializacji i znacznie większą pamięć procesu. Dla małych grup bezpośredniego COPY zwiększenie limitu 256 → 512 → 1024 MiB dawało niewielkie różnice czasu i nie było warunkiem powodzenia.

### 500 MiB — dane repeated_hash

Czas dotyczy całej partii: jeden plik albo dwa równoległe pliki. Pamięć to największy próbkowany wynik procesu dla pary.

| Przepływ | Limit na import | Grupa | Jeden [s] | Para [s] | Szczyt prywatny pary [MiB] |
| --- | ---: | ---: | ---: | ---: | ---: |
| CTAS, zstd 3 | 1024 MiB | 122880 | 1,938 | 2,178 | 1939 |
| CTAS, zstd 3 | 256 MiB | 16384 | 2,157 | 2,601 | 730 |
| Direct, zstd 3 | 256 MiB | 16384 | 1,372 | 1,635 | 630 |
| Direct, zstd 3 | 256 MiB | 65536 | 1,759 | 1,801 | 747 |
| Direct, zstd 3 | 512 MiB | 122880 | 2,073 | 1,802 | 1008 |

Para może wyglądać szybsza od pojedynczej próby ze względu na małą próbkę i różny stan cache. Nie jest to dowód na przyspieszenie pojedynczego importu przez dołożenie drugiego.

### 1 GiB — dane repeated_hash

| Przepływ | Limit na import | Grupa | Jeden [s] | Para [s] | Prywatny szczyt pary [MiB] | Spill pary [MiB] |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| CTAS, zstd 3 | 1024 MiB | 122880 | 4,591 | 6,737 | 2534 | 484 |
| CTAS, zstd 3 | 256 MiB | 16384 | 4,471 | 5,866 | 768 | 979 |
| Direct, zstd 3 | 256 MiB | 16384 | 2,791 | 3,104 | 627 | 0 |
| Direct, zstd 3 | 256 MiB | 65536 | 3,086 | 3,400 | 848 | 51 |
| Direct, zstd 3 | 512 MiB | 122880 | 2,854 | 3,129 | 1017 | 0 |
| Direct, zstd 1 | 512 MiB | 122880 | 2,498 | 2,789 | 910 | 0 |

Przykładowa udana próba CTAS przy 1024 MiB: kopia CSV 317 ms, tworzenie tabeli 1408 ms, zapis Parquet 2683 ms, metadane około 5 ms, kopia wyniku około 13 ms. Mniejszy limit tabeli zwiększa presję spill. Bezpośredni COPY usuwa etap materializacji i potrzebę utrzymywania całej tabeli, lecz nadal buforuje dane CSV i grupy Parquet.

### Kierunek eksportu bez tabeli

```sql
COPY (
    SELECT *
    FROM read_csv_auto($sourcePath, header = true)
)
TO $parquetPath
(
    FORMAT parquet,
    COMPRESSION zstd,
    COMPRESSION_LEVEL 3,
    ROW_GROUP_SIZE 122880
);
```

Jest to bardziej strumieniowy pipeline SQL, a nie zero-copy ani streaming HTTP. Prywatna kopia CSV oraz zapis Parquet nadal istnieją. Po sukcesie należy pobrać num_rows przez parquet_file_metadata($parquetPath) i schemat przez DESCRIBE SELECT * FROM read_parquet(...). W eksperymencie nie opierano poprawności na wartości zwracanej przez ExecuteScalar dla COPY. Nie trzeba ponownie parsować całego CSV, aby policzyć wynik.

Zmiana ta może pozostać wewnątrz DuckDbCsvImporter; nie wymaga nowego systemu kolejek ani modelu DB. Przed wdrożeniem wymaga jednak testów rzeczywistego importera dla pustych danych, błędnego CSV, NULL, Unicode, dat, wartości numerycznych, cytowanych pól, anulowania i częściowych plików. Eksperymentalna kopia SQL nie zastępuje tych testów.

## 4. Kompresja i mniej powtarzalny tekst

### 150 MiB, direct, 256 MiB, grupa 16384

| Kodek | Mediana pojedynczego importu [s] | Parquet [MiB] |
| --- | ---: | ---: |
| zstd level 3 | 0,541 | 6,05 |
| zstd level 1 | 0,435 | 5,46 |
| snappy | 0,425 | 17,42 |
| uncompressed | 0,458 | 149,56 |

Niższy poziom zstd dał w tym konkretnym zbiorze także mniejszy plik. Nie należy zakładać monotonicznej poprawy kompresji z poziomem ani przenosić tych proporcji na inne dane. Zstd, snappy i uncompressed były porównane na 150 MiB; snappy/uncompressed nie badano osobno na 1 GiB.

### 1 GiB, independent_hash_blocks

| Przepływ i grupa | Limit | zstd | Jeden [s] | Para [s] | Parquet/pliki [MiB] | Prywatny szczyt pary [MiB] |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Direct, 122880 | 256 MiB | 3 | OOM | OOM | brak | nie interpretować jako sukces |
| Direct, 16384 | 256 MiB | 3 | 10,129 | 10,035 | 529,9 | 567 |
| Direct, 32768 | 256 MiB | 3 | 10,132 | 10,149 | 529,6 | 530 |
| Direct, 65536 | 256 MiB | 3 | 10,533 | 10,924 | 530,0 | 743 |
| Direct, 122880 | 512 MiB | 3 | 11,577 | 10,444 | 529,7 | 1015 |
| Direct, 32768 | 256 MiB | 1 | 3,133 | 3,581 | 512,1 | 528 |
| Direct, 122880 | 512 MiB | 1 | 3,258 | 3,706 | 512,1 | 1016 |

W tym przypadku po usunięciu problemu pamięci dominuje CPU kompresji. Dla grupy 32768 i jednego importu czas CPU procesu spadł z około 11,27 s przy level 3 do 3,27 s przy level 1. Samo zwiększenie memory_limit nie rozwiązuje tego kosztu.

Nie zmieniono domyślnej kompresji ani jej poziomu. [Dokumentacja COPY](https://duckdb.org/docs/1.5/sql/statements/copy) wskazuje poziom 3 jako domyślny dla zstd; level 1 jest obsługiwaną, bezstratną alternatywą.

## 5. Wpływ na odczyty i analizy

Dla udanego wyjścia wykonano:
- agregację Category/SUM(Value) z ORDER BY, odpowiadającą obecnym widgetom;
- profil liczbowy COUNT/MIN/MAX/AVG;
- środkową stronę 100 pełnych wierszy przez LIMIT/OFFSET;
- agregację z filtrem 1% Id jako scenariusz przyszłej selektywnej analizy;
- osobno kontrolę wszystkich danych i kolejności.

Odczyty używały 512 MiB i 1 albo 4 wątków; zapytania były rozgrzewane, a następnie wykonywane 3 razy. Poniżej mediany tych trzech wykonań. Nie są to czasy całego endpointu.

### 1 GiB repeated_hash — koszt zapytań

| Układ | Grupy | Agregacja, 1 wątek [ms] | Agregacja, 4 wątki [ms] | Strona, 4 wątki [ms] |
| --- | ---: | ---: | ---: | ---: |
| CTAS, 122880, zstd 3 | 9 | 10,32 | 4,14 | 47,83 |
| Direct, 122880, zstd 3 | 9 | 10,04 | 3,85 | 48,86 |
| Direct, 65536, zstd 3, 256 MiB importu | 16 | 11,78 | 4,53 | 35,70 |
| Direct, 32768, zstd 3 | 30 | 13,19 | 5,38 | 19,39 |
| Direct, 16384, zstd 3 | 60 | 21,28 | 7,48 | 12,07 |
| Direct, 122880, zstd 1 | 9 | 10,86 | 5,41 | 45,40 |

Duże grupy zachowują dobre pełne skany i mały narzut metadanych. Małe grupy poprawiają paginację i selektywne filtry, ale mogą ponad dwukrotnie spowolnić samą agregację. Dlatego wariantu 16k nie rekomenduje się jako uniwersalnego ustawienia Aperture.

Level 1 nie daje jednolitego wyniku: na repeated_hash agregacja czterowątkowa była wolniejsza około 1,3 ms od bazowej, podczas gdy dla independent_hash_blocks wyniosła około 4,05 ms wobec 4,90 ms przy level 3. Liczba próbek jest mała. Zysk importu uzasadnia dalszy pilotaż level 1, lecz nie stwierdzenie „brak regresji wszystkich zapytań”.

### Kopia całego Parquet jest osobnym wąskim gardłem

DuckDbParquetExecutor.cs:31–55 kopiuje cały Parquet do pliku tymczasowego dla każdego odczytu i tworzy nową bazę in-memory. Przy independent_hash_blocks wynik 1 GiB CSV miał około 512–530 MiB. Jednorazowa kopia w eksperymencie zajmowała około 147–221 ms, gdy agregacja SQL zajmowała około 4–15 ms.

To istotny argument za zstd: szybszy zapis snappy lub uncompressed może pogorszyć pełną ścieżkę odczytu przez większy plik, transfer i przestrzeń tymczasową. Dla 150 MiB kopia uncompressed trwała około 41,7 ms, a zstd 3 około 3,0 ms.

W przyszłym osobnym etapie należy pomierzyć bezpieczny odczyt niezmiennego lokalnego pliku lub ograniczone ponowne użycie kopii; musi on respektować usuwanie datasetów i storage zdalny. Nie wprowadzono teraz cache ani portu lokalnych ścieżek.

Produkcja nie ustawia też jawnego budżetu pamięci/wątków dla baz tworzonych przez DuckDbParquetExecutor. Bezpieczny importer nie ogranicza całkowitego RAM równoległych analiz. Wymaga to osobnego budżetu całego procesu i testu mieszającego uploady, importy oraz odczyty.

## 6. preserve_insertion_order i równoległość

Obecne DuckDbRowReader.cs:19–21 używa SELECT * ... LIMIT/OFFSET bez ORDER BY. Parametr preserve_insertion_order ma znaczenie funkcjonalne, a nie tylko kosztowe.

W screening:
- preserve=true, 1/4 wątki: badane wyjścia zachowały kolejność;
- preserve=false, 1 wątek: w tych danych nie zawsze było przestawienie, lecz nie daje to gwarancji;
- preserve=false, 4 wątki, 16k: przestawionych 141 457 z 150 042 wierszy;
- wyłączenie preserve z domyślną grupą nadal kończyło się OOM przy 256 MiB;
- ROW_GROUP_SIZE_BYTES=16MiB działał jako kolejna mała grupa, lecz zgodnie z dokumentacją wymaga preserve=false. Nie rozwiązuje bezkosztowo wymogu zachowania kolejności.

[DuckDB: Order Preservation](https://duckdb.org/docs/1.5/sql/dialect/order_preservation) wyjaśnia brak gwarancji po wyłączeniu tej opcji. Dodanie jawnej kolumny porządku/zmiana stronicowania wymagałaby osobnej decyzji o kontrakcie i nie jest minimalnym rozwiązaniem.

Cztery wątki poprawiły czas pojedynczego małego importu, ale zwiększają łączny budżet CPU przy dwóch zadaniach i nie są potrzebne do uzyskania 1 GiB. Rekomendacja początkowa pozostaje 1 wątek na import i istniejące 2 sloty na instancję, z globalnym limitem jednego Running na właściciela w PostgreSQL. Nie zmieniono tych mechanizmów.

## 7. Pamięć i dysk: jak interpretować liczby

memory_limit jest budżetem instancji DuckDB, nie RSS całej aplikacji. [DuckDB: OOM](https://duckdb.org/docs/1.5/guides/performance/oom) opisuje alokacje omijające buffer manager. Dwa prywatne połączenia/bazy mają odrębne budżety.

Pomiar to Process.PrivateMemorySize64 / WorkingSet64 całego procesu testowego, nominalnie co 50 ms. Nie wymuszano GC i nie resetowano natywnych allocatorów między wariantami. Baseline w JSON jest istotny: wyższy szczyt pojedynczego wariantu może pochodzić z pamięci pozostałej po poprzednich próbach/odczytach. Przyrost ponad baseline również jest w CSV zbiorczym.

Największy odstęp obserwacji wyniósł 662,77 ms. Szczyty są próbkowane i mogą nie obejmować krótkich alokacji. Nie są twardym limitem bezpieczeństwa ani podstawą do ustawienia kontenera „dokładnie 1017 MiB”. Monitor 5 GiB był tylko dodatkowym zabezpieczeniem eksperymentu.

Rozmiary dyskowe to logiczne długości plików, nie fizyczne sektory ani liczba bajtów I/O. Wystąpiło 26 niepełnych odczytów podczas zmian plików; wyniki dyskowe są dolnym oszacowaniem. Zapisywane są także liczba obserwacji i maksymalny odstęp.

Dla dwóch eksportów 1 GiB o mniejszej powtarzalności przestrzeń partii osiągała około 4,1 GiB: prywatne kopie CSV, roboczy Parquet i kopie wyniku. Nie obejmuje to dwóch trwałych źródłowych CSV w aplikacji ani buforów HTTP. Trwałe źródła dodają kolejne 2 GiB, a równoczesne bufory uploadów mogą dodać jeszcze 2 GiB. Dochodzą odczyty Parquet, stara historia, niepewne próby oraz ewentualny spill.

Direct z małymi grupami często nie używał spill, ale nadal potrzebuje katalogu na prywatne CSV i wynik. CTAS przy 1 GiB i małym RAM zużywał dla pary prawie 1 GiB dodatkowego spill. Nie usunięto ani nie podniesiono limitu spill 1024 MiB na próbę.

Obie równoległe konwersje czytały identyczne wejście do prywatnych kopii. Cache źródła może być bardziej korzystny niż dwa różne pliki produkcyjne. Wszystkie główne odczyty były warm-cache; nie testowano zimnego magazynu sieciowego. Aparatura używa File.Copy w miejscach kopii pliku, podczas gdy produkcyjny port storage używa CopyToAsync. Najbardziej miarodajne są porównania konfiguracji wewnątrz tego samego stanowiska, nie absolutne porównanie z czasami wcześniejszego benchmarku produkcyjnego importera.

Wstępny serwerowy profil powinien rezerwować pamięć procesu ponad sumą limitów importów, osobno dla analiz i ASP.NET, oraz mieć OS/container limit i monitoring. Testy wykonano na maszynie 32 GB; nie dowodzą gotowości serwera 2–4 GB. Ostateczną rezerwę należy wyznaczyć obciążeniem całej aplikacji z docelowymi równoległymi odczytami.

## 8. HTTP, klient i źródłowy CSV

### Potwierdzone w repozytorium

- ImportDataDialog.tsx:69 odrzuca powyżej 10 * 1024 * 1024 bajtów. Dotyczy wyboru pliku również w trybie połączonym; mode zmienia tekst stopki, nie ten warunek.
- Ten sam komponent:75 wykonuje candidate.text() i pełne parseCsv w przeglądarce.
- frontend/src/mocks/csv.ts:108 ogranicza parser do około 20 000 wierszy (warunek opiera się na matrix.length, więc nie należy traktować komunikatu jako ścisłej granicy liczby rekordów).
- frontend/src/data/api.ts:55 ma AbortSignal.timeout(30_000), także dla FormData uploadu.
- DatasetEndpoints.cs:31–55 używa IFormFile. Sprawdza pusty plik, ale nie definiuje własnego limitu wielkości.
- Program.cs i konfiguracja repozytorium nie podnoszą limitu serwera ani formularza. Compose w infra definiuje PostgreSQL, nie konfigurację ingress/proxy.
- Runtime frameworku: MaxRequestBodySize=30 000 000 bajtów, MultipartBodyLengthLimit=134 217 728 bajtów, MemoryBufferThreshold=65 536 bajtów. Rzeczywisty deployment może nadpisać ustawienia środowiskiem/proxy; tego nie zweryfikowano na serwerze.

[ASP.NET Core: upload plików](https://learn.microsoft.com/en-us/aspnet/core/mvc/models/file-uploads?view=aspnetcore-10.0) opisuje buforowanie IFormFile do pliku po przekroczeniu progu, limity Kestrel/IIS/formularza oraz ASPNETCORE_TEMP. Limit serwera trzeba ustawić przed odczytem body. Samo zwiększenie MultipartBodyLengthLimit nie omija Kestrel ani reverse proxy.

### Konsekwencje dla 500 MiB/1 GiB

1. UI wymaga docelowej polityki dużych uploadów i ograniczonego podglądu, zamiast pełnego tekstu i parsera demo. Nie wystarczy zmienić jednego progu 10 MiB.
2. Klient potrzebuje odpowiedniego timeoutu/anulowania/progresu. Sam transfer 1 GiB w 30 s wymaga ponad 34 MiB/s, bez pozostałych kosztów.
3. Podnosić limity per endpoint do skończonej wartości z zapasem na multipart, nie wyłączać globalnych limitów. Egzekwować też rzeczywistą liczbę bajtów przy przesyle bez Content-Length.
4. IFormFile może działać przy większym limicie, ale kosztuje dodatkową kopię dyskową. Streaming multipart do istniejącego storage może zachować kształt żądania HTTP, lecz wymaga osobnego wdrożenia i testów.
5. Autoryzację projektu, limity równoczesnych uploadów i kwoty dyskowe trzeba rozważyć przed kosztownym buforowaniem. Limit jednego Running nie ogranicza liczby Pending ani uploadów oczekujących na worker.
6. LocalFileStorage.SaveAsync strumieniuje do FileMode.CreateNew, więc nie wczytuje całego CSV do managed RAM. Importer później robi własną pełną kopię wejścia. COPY bez tabeli nie usuwa tej kopii ani retencji źródła.
7. Źródłowy CSV pozostaje po sukcesie i porażce. Podniesienie wielkości plików wymaga polityki retencji/kwot. Przed rollout należy również zabezpieczyć ścieżkę nieznanego commita tworzenia metadanych opisaną w [raporcie 09](09-import-polish-and-reliability.md), zamiast ryzykować utratę dużego uploadu lub osierocenie źródła.

Nie zmieniono teraz UI, timeoutu, endpointu, source storage ani cleanup.

## 9. Konkretne rekomendacje i kolejność

### A. Pierwsza implementacja importera — małe ryzyko odczytów

- Zastąpić CTAS bezpośrednim COPY z read_csv_auto.
- Po zapisie pobrać metadane z gotowego Parquet.
- W pilotażowym profilu odpowiednio wyposażonej instancji dopuścić 512 MiB na import, pozostawiając grupę 122880, zstd 3 i preserve=true.
- Zachować 1 wątek/import, 2 sloty, osobne połączenia, heartbeat i wszystkie granice publikacji/cleanup.
- Nie przechodzić na 1024 MiB na próbę bez danych uzasadniających potrzebę.
- Wdrożyć dopiero po testach produkcyjnej ścieżki: błędny CSV, pełna zgodność schematu/wartości, anulowanie, częściowe pliki, publish/fencing/nieznany commit i równoległe zapisy.

To profil rekomendowany do osobnej zmiany, nie ustawienie już aktywowane w repozytorium.

### B. Alternatywy zależne od priorytetu

- Jeśli 256 MiB jest twardym budżetem DuckDB: direct + grupa 65536 + zstd 3. W badanych 1 GiB zadziałał, z umiarkowanym kosztem agregacji i szybszą stroną. Potrzebuje nieco spill i akceptacji na docelowym workload.
- Grupa 32768: mniejsza pamięć i znacznie szybszy podgląd, lecz większy narzut pełnych agregacji.
- Grupa 16384: profil ograniczonego RAM/paginacji, nie uniwersalny default.
- Zstd level 1: mocny kandydat do kolejnego pilotażu po zachowaniu grupy i sprawdzeniu realnych analiz; wyniki importu są bardzo korzystne, lecz query SLA i kompresja zależą od danych.
- Nie zmieniać preserve na false bez decyzji o kolejności i stronicowaniu.
- Nie wybierać snappy/uncompressed globalnie, zanim nie uwzględni się kosztu kopiowania całego Parquet przy odczycie.

### C. Rollout 500 MiB, potem 1 GiB

Najpierw zabezpieczenia źródła/kwot i test rzeczywistego importera; następnie upload i klient z ograniczonym preview. Włączyć skończony limit 500 MiB i zmierzyć całą aplikację z dwoma właścicielami, uploadem oraz widgetami. Dopiero po pomiarze headroom RAM/dysku i stabilności odczytów zwiększać do 1 GiB.

Kryterium nie powinno brzmieć tylko „COPY się udało”: brak OOM/kill procesu, przewidywalna obsługa odrzucenia zbyt ciężkiego pliku, poprawne metadane i kolejność, brak regresji docelowych zapytań, działający heartbeat/fencing, brak aktywnych rekordów wskazujących usunięty plik oraz kontrola zajętości dysku po błędach.

## 10. Weryfikacja końcowa

- Sześć serii eksperymentów zakończyło się poprawnie; OOM rejestrowano jawnie jako wyniki.
- Build: 0 błędów, 0 ostrzeżeń.
- Standardowy dotnet test solution: 22 jednostkowe i 413 integracyjnych zaliczone.
- Trzy jawne benchmarki/eksperymenty mają NotExecuted w standardowym przebiegu.
- Brak zmian kodu produkcyjnego, API, migracji, konfiguracji limitów i frontendu; bez commita.
