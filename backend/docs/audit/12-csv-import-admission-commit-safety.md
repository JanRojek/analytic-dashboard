# Aperture — AUD-008: bezpieczne przyjęcie importu CSV przy niepewnym commicie

Data: 2026-11-15. Zakres: zapis źródła CSV i atomowe utworzenie Dataset, DatasetVersion oraz ImportJob. Bez commita, migracji, zmian publicznego API, konfiguracji, frontendu i mechanizmów cleanup/AUD-006.

## 1. Naprawiony mechanizm

Poprzedni ImportCsvDatasetHandler obejmował SaveAsync źródła i CreateAsync metadanych wspólnym catch, który zawsze usuwał źródło. Wyjątek z SaveChangesAsync nie dowodził, że PostgreSQL nie zatwierdził danych.

Obecnie handler najpierw zapisuje źródło, a następnie osobno tworzy metadane. Błąd zapisu pliku nie uruchamia DeleteAsync handlera: LocalFileStorage odpowiada za własny częściowy zapis i chroni istniejący plik przez FileMode.CreateNew. Rozdzielenie usuwa również możliwość skasowania istniejącego artefaktu po nieudanym CreateNew.

PendingDatasetImportRepository otwiera jawną, krótką transakcję obejmującą wyłącznie trzy rekordy metadanych. Kopiowanie CSV i konwersja DuckDB odbywają się poza nią. Jawna granica pozwala rozróżnić błąd przed wysłaniem próby COMMIT od niepewnego wyniku samego commita.

| Wynik | Dowód / zachowanie repozytorium | Zachowanie handlera |
| --- | --- | --- |
| Potwierdzony sukces | CommitAsync powrócił poprawnie albo świeży kontekst potwierdził dokładny zestaw rekordów | Dotychczasowy Accepted z tymi samymi, już wygenerowanymi identyfikatorami; CSV pozostaje |
| Potwierdzony brak zapisu | Nie rozpoczęto zapisu metadanych albo przed próbą COMMIT udało się potwierdzić ROLLBACK | Usunięcie własnego źródła i ponowne zgłoszenie pierwotnego wyjątku |
| Wynik nieznany | COMMIT zgłosił wyjątek; rollback nie został potwierdzony; świeży odczyt niczego nie potwierdził lub był niedostępny | CSV pozostaje, pierwotny wyjątek jest zgłaszany; brak powtórzenia CreateAsync |

Nie wykonuje się rollbacku w celu uzasadnienia cleanup po rozpoczęciu próby COMMIT. Sam późniejszy brak rekordów również nie jest uprawnieniem do usunięcia źródła: odczyt może poprzedzać zatwierdzenie pierwotnej operacji.

Niewielki PendingDatasetImportRolledBackException jest wewnętrznym kontraktem persistence oznaczającym wyłącznie potwierdzony brak zatwierdzenia. Pozostałe wyjątki nie uprawniają do cleanup. Normalny powrót z CreateAsync oznacza potwierdzony sukces. Nie dodano uniwersalnej hierarchii wyników ani infrastruktury retry.

Handler po cleanup przekazuje dalej oryginalny wyjątek przez ExceptionDispatchInfo, zachowując także OperationCanceledException i jego stos. Dotychczasowy AggregateException przy dodatkowym błędzie fizycznego cleanup pozostaje. Nie zmieniono mapowania 202, 400, 401 ani 404; nierozstrzygnięta awaria nadal trafia do istniejącej obsługi wyjątków API.

## 2. Rekoncyliacja

Po błędzie wymagającym weryfikacji repozytorium tworzy świeży scope i pobiera nowy AppDbContext. Nie odczytuje ChangeTrackera ani potencjalnie uszkodzonego połączenia poprzedniego kontekstu.

Jedno zapytanie AsNoTracking potwierdza łącznie:
- dokładne ImportJob.Id, DatasetVersion.Id i Dataset.Id;
- relacje job → version → dataset;
- Dataset.ProjectId;
- dokładny SourceStorageKey.

Zapytanie nie wymaga Pending ani pustego CurrentVersionId. Inny worker może już przejąć lub zakończyć prawidłowo zatwierdzony import; to nie unieważnia potwierdzenia utworzenia metadanych.

Wynik dodatni potwierdza sukces bez ponownego INSERT. Wynik ujemny oznacza wyłącznie „nie potwierdzono sukcesu w tym odczycie”. Nie zamienia się w potwierdzony rollback.

Rollback i weryfikacja mają osobne, stałe limity po 10 sekund oraz tokeny niezależne od anulowanego żądania. Anulowanie HTTP nie może samo uniemożliwić ustalenia bezpieczeństwa pliku. Nie dodano nowej opcji konfiguracji. Błąd zwolnienia transakcji jest logowany i nie zastępuje już ustalonego wyniku ani pierwotnego wyjątku.

Nowe logi zawierają ImportJobId i nazwę typu wyjątku. Nie przekazują loggerowi całych wyjątków, nazw źródłowych, kluczy storage, ścieżek ani zawartości CSV.

## 3. Testy i deterministyczne wymuszanie awarii

Nowy PendingDatasetImportCommitTests używa rzeczywistego PostgreSQL Testcontainers, prawdziwych transakcji EF/Npgsql i LocalFileStorage. Fault injection przez istniejące interceptory EF zastępuje zgubienie potwierdzenia lub niedostępność odczytu; nie zastępuje metadanych mockiem repozytorium.

| Scenariusz | Zweryfikowana gwarancja |
| --- | --- |
| Wyjątek w TransactionCommittedAsync po rzeczywistym commicie | Świeży odczyt potwierdza zapis, Accepted, źródło zachowane, dokładnie jeden zestaw metadanych |
| Anulowanie i OperationCanceledException po rzeczywistym commicie | Niezależny token pozwala potwierdzić commit, CSV pozostaje |
| Niedostępny odczyt w świeżym kontekście po commicie | Pierwotny wyjątek, zero DeleteAsync, zatwierdzone metadane i używalne źródło pozostają |
| Ujemny odczyt przed późniejszym rzeczywistym commitem | Chwilowa nieobecność rekordów nie powoduje usunięcia CSV |
| Błąd po wykonaniu trzech INSERT, przed COMMIT | Potwierdzony ROLLBACK całego zestawu, brak wszystkich trzech rekordów, źródło usunięte |
| Anulowanie po INSERT, przed COMMIT | Potwierdzony rollback mimo anulowanego tokenu żądania, oryginalne anulowanie przekazane dalej |
| Projekt usunięty po walidacji/zapisie pliku | Rzeczywiste naruszenie FK PostgreSQL, rollback i brak częściowych metadanych |
| Wyjątek po rzeczywistym ROLLBACK, przed jego potwierdzeniem | Ostrożne zachowanie CSV; ujemny odczyt nadal nie uprawnia do kasowania |
| Błąd CreateNew źródła przy istniejącym pliku | Brak DeleteAsync handlera, istniejące bajty zachowane, zero metadanych |

W teście późnego commita interceptor zatrzymuje ścieżkę przed potwierdzeniem COMMIT. Świeży SELECT jest rzeczywiście wykonany w PostgreSQL, a po utworzeniu jego snapshotu interceptor ReaderExecutedAsync zatwierdza oryginalną transakcję bezpośrednio przez DbTransaction. Odczyt zwraca wcześniejszy brak rekordów, choć commit już nastąpił. Handler zgłasza nierozstrzygnięty błąd i zachowuje plik; późniejszy niezależny odczyt widzi wszystkie rekordy, a prawdziwy worker kończy import. Kolejność jest wymuszona callbackami w konkretnych punktach protokołu, bez Task.Delay i Thread.Sleep.

Dodatnie testy sprawdzają odmienny ContextId w rekoncyliacji, pojedynczy SaveChanges oraz pojedynczą próbę COMMIT. Liczby rekordów weryfikowane są osobnym kontekstem. W scenariuszach zachowanego commita uruchamiany jest rzeczywisty ImportJobProcessor: claim, CSV → Parquet, fencing i atomowa publikacja kończą się Completed/Ready, poprawnymi metadanymi oraz istniejącym Parquet. Źródło pozostaje również po przetworzeniu.

Nie testowano fizycznego zrywania pakietów sieciowych PostgreSQL; wyjątek po rzeczywistym commicie odtwarza istotną dla aplikacji utratę potwierdzenia, a nie zachowanie konkretnej sieci.

## 4. Wyniki weryfikacji

- Build całego solution: **0 błędów, 0 ostrzeżeń**.
- Nowe testy celowane: **9/9**.
- Cały standardowy zestaw: **22/22 testy jednostkowe, 439/439 integracyjne**, 0 niepowodzeń.
- Trzy ciężkie metody pomiarowe pozostały Explicit/NotExecuted. Nie uruchamiano benchmarków w tym zadaniu.
- Wszystkie istniejące testy HTTP importu, walidacji nazw, storage, AUD-001 i AUD-004 pozostają w standardowym zestawie.

Komendy:

```powershell
dotnet build AnalyticDashboard.slnx --no-restore --nologo
dotnet test AnalyticDashboard.slnx --no-build --no-restore --nologo
```

Zapis liczb i nazw nowych testów: [aud008-test-results.json](aud008-test-results.json).

## 5. Dodatkowa kontrola komunikatów błędów — bez zmiany produkcji

**Potwierdzony mechanizm ryzyka:** ImportJobProcessor.cs:174 przekazuje CreateErrorMessage(exception) do TryFailAsync. Metoda w ImportJobProcessor.cs:324–335 kopiuje exception.Message i jedynie skraca ją do 2000 jednostek UTF-16. ImportJobRepository zapisuje tekst do ErrorMessage (około linii 319). Skrócenie nie usuwa wrażliwych fragmentów.

FileNotFoundException/DirectoryNotFoundException z odczytu storage mogą zawierać pełną ścieżkę pliku. Diagnostyka parsera DuckDB może zawierać ścieżkę prywatnego workspace, techniczną nazwę source.csv, nazwy kolumn i fragment niepoprawnego rekordu. Oryginalnej nazwy uploadu nie używa się jako nazwy roboczego CSV; nie stwierdzono odrębnego przepływu, który w tej ścieżce celowo kopiuje ją do błędu. Nie przeprowadzono w tym zadaniu nowego pomiaru kompletnego formatu wszystkich komunikatów natywnych.

**Klient HTTP jest już chroniony:** DatasetEndpoints.cs:309 zwraca dla Failed stały tekst „Import failed. Please try again.”. GetImportStatusResult nie zawiera wewnętrznego ErrorMessage. Istniejący GetImportStatus_ShouldReturnSafeErrorMessageForFailedImport weryfikuje, że ścieżka z zapisanego błędu nie trafia do klienta; test przeszedł. Pozostałe użycia pola sprawdzono przez wyszukiwanie symbolu.

Mała proponowana poprawka, do osobnego wdrożenia:
1. Zastąpić kopiowanie exception.Message w CreateErrorMessage kilkoma stałymi komunikatami według już rozróżnianych FileNotFound/DirectoryNotFound oraz DuckDBErrorType.InvalidInput/Conversion.
2. Zachować istniejące logi strukturalne z JobId, AttemptNumber i ErrorType, bez pełnego wyjątku/treści rekordu.
3. Rozszerzyć istniejący test processor/diagnostyki o syntetyczny znacznik prywatnej wartości i ścieżki; sprawdzić ich brak w zapisanym ErrorMessage oraz niezmieniony bezpieczny HTTP.
4. Ustalić osobno postępowanie z historycznymi ErrorMessage; nie czyścić istniejącej bazy w ramach poprawki kodu.

Nie przebudowano systemu logowania ani sposobu prezentacji błędów. Powyższa analiza pozostaje jawnie otwartym ryzykiem, zgodnie z ograniczeniem zakresu.

## 6. Pozostałe granice

- Przy nieznanym wyniku mogą pozostać pliki, których metadane ostatecznie nie zostały zatwierdzone. To świadoma zamiana ryzyka utraty czynnego źródła na zachowanie pliku wymagającego przyszłej bezpiecznej inwentaryzacji/AUD-006.
- Brak nowego trwałego procesu rekoncyliacji lub cleanup. Zatwierdzone Pending obsługuje dotychczasowy worker; źródła bez metadanych nie są automatycznie kasowane.
- Brak idempotency key publicznego HTTP. Nie ma automatycznej powtórki w serwerze; osobne ponowne żądanie klienta po utraconej odpowiedzi może nadal utworzyć nowy, niezależny import.
- Awaria cleanup po potwierdzonym rollbacku może pozostawić źródło i zgłosić dotychczasowy AggregateException. Nie tworzy aktywnego zadania bez źródła.
- Nagłe zabicie procesu między zapisem CSV i SQL nadal może pozostawić osierocone źródło. Nie naruszono własności plików ani nie dodano usuwania według wieku.
- Nie zmieniono ograniczeń storage między hostami, retencji CSV, późnych zapisów zombie workerów ani innych ustaleń AUD-006. Bieżąca poprawka chroni przed kasowaniem źródła przez obsługę niepewnego zapisu metadanych.
- Potwierdzenie dodatnie dopuszcza równoległy postęp workera. Celowe późniejsze usunięcie datasetu/projektu nadal podlega AUD-001; ta poprawka nie obiecuje bezterminowej retencji plików.

## 7. Zmienione pliki i końcowy przegląd

Kod:
- src/AnalyticDashboard.Application/Datasets/ImportCsvDataset/ImportCsvDatasetHandler.cs
- src/AnalyticDashboard.Application/Datasets/Persistence/IPendingDatasetImportRepository.cs
- src/AnalyticDashboard.Application/Common/Exceptions/PendingDatasetImportRolledBackException.cs — nowy
- src/AnalyticDashboard.Infrastructure/Repositories/PendingDatasetImportRepository.cs
- tests/AnalyticDashboard.IntegrationTests/Datasets/PendingDatasetImportCommitTests.cs — nowy

Dokumentacja: ten raport, aud008-test-results.json i [pełny diff](aud008-import-admission.diff).

Przegląd techniczny: cały diff, trzy wyniki zapisu, brak cleanup po niepewnym COMMIT/ROLLBACK, dowód atomowości na PostgreSQL, niezależność kontekstów i anulowania, brak powtórnego INSERT, zachowanie pierwotnych wyjątków oraz własności storage. Brak zmian claim/lease/fencing i brak produkcyjnego dostępu do bazy podczas testów.

Przegląd spójności: zachowano istniejący port persistence, scoped EF, IServiceScopeFactory i ILogger oraz małe wyjątki aplikacyjne analogiczne do Common/Exceptions. Brak nowych serwisów, bibliotek i konfiguracji. Testy wykorzystują istniejący ApiFixture/PostgreSQL Testcontainers, jawne scenariusze i lokalne interceptory; brak frameworka synchronizacji i using z inicjalizatorem IDisposable.

Pełny diff przedstawia zmiany wyłącznie tego zadania względem treści plików odczytanych na jego początku. Nie zawiera wcześniejszych etapów ani samego pliku diff. Nie wykonano commita.
