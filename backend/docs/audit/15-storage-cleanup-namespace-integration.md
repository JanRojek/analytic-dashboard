# AUD-006B — integracja zamknięcia namespace z cleanup

Data: 2026-11-16. Zakres: zakończenie intencji DELETE po trwałym zamknięciu namespace i usunięciu katalogu. Bez commita, migracji i zmian HTTP, importera, claim/lease/fencing ani StorageTestHost.

## Zmiany

| Plik względem backend/ | Zmiana |
| --- | --- |
| src/AnalyticDashboard.Application/Storage/IStorageNamespaceCloser.cs | Jednometodowy kontrakt istniejącego zamykania namespace. |
| src/AnalyticDashboard.Infrastructure/Storage/LocalFileStorage.cs | Implementacja kontraktu; protokół stagingu, instalacji i zamknięcia pozostaje bez zmian. |
| src/AnalyticDashboard.Infrastructure/DependencyInjection.cs | IFileStorage i IStorageNamespaceCloser wskazują tę samą instancję LocalFileStorage w scope. |
| src/AnalyticDashboard.Infrastructure/Storage/StorageCleanupProcessor.cs | CloseNamespaceAsync → DeleteDirectoryAsync → DELETE zadania po Id; odczyt bez trackingu i bezpieczna aktualizacja backoff. |
| src/AnalyticDashboard.Infrastructure/Storage/StorageCleanupJob.cs | Usunięcie nieużywanego ScheduleVerification. |
| tests/AnalyticDashboard.IntegrationTests/Storage/StorageCleanupProcessorTests.cs | Kolejność operacji, zakończenie zadania, zachowanie intencji i backoff przy błędzie zamknięcia lub usuwania. |
| tests/AnalyticDashboard.IntegrationTests/Concurrency/ImportStorageLifecycleTests.cs | Późna instalacja po DELETE, niepewne zakończenie SQL oraz równoczesne zakończenie tego samego zadania przez dwa konteksty. |
| tests/AnalyticDashboard.IntegrationTests/Import/ImportArtifactOwnershipTests.cs | Dostosowanie istniejącej ochrony opublikowanego pliku innego datasetu do zakończenia cleanup. |

Usunięto VerificationInterval i pomocnicze wymuszanie godzinnej weryfikacji w testach. Nie zachowano zastąpionej ścieżki dla kompatybilności. Zredukowano dwa warianty testu późnego zapisu, które zakładały skuteczną instalację po DELETE i dopiero późniejszą utratę potwierdzenia: przy zamkniętym namespace taka instalacja jest teraz odrzucana. Pokrycie niepewnych operacji filesystemu pozostaje w istniejących LocalStorageNamespaceTests.

## Gwarancje i ponowienia

1. Intencja nadal powstaje atomowo z DELETE w PostgreSQL. Nie zmieniono blokad projektu, sprawdzania aktywnych importów ani transakcji usuwania.
2. Processor odczytuje dostępną intencję, zamyka namespace przy użyciu dotychczasowego protokołu, usuwa katalog i dopiero wtedy usuwa rekord zadania.
3. Operacje filesystemu nie odbywają się wewnątrz transakcji SQL. Zamknięcie korzysta z istniejącej blokady międzyprocesowej instalacji. Nie jest potrzebne utrzymywanie jej podczas rekurencyjnego usuwania: zamknięty namespace odrzuca wszystkie kolejne współpracujące instalacje.
4. Błąd zamknięcia nie pozwala przejść do usuwania katalogu. Błąd któregokolwiek kroku filesystemu zachowuje intencję i dotychczasowy backoff: 30 s, 60 s, 120 s, aż do 1920 s. Anulowanie hosta przerywa pracę i pozostawia niezakończone zadanie.
5. Niepewne zamknięcie jest ponawiane; istniejący protokół nie interpretuje niepełnego markera jako Open. Znacznik znajduje się poza katalogiem datasetu i pozostaje po jego usunięciu.
6. Błąd SQL podczas końcowego DELETE nie wymaga rozstrzygnięcia commita przez ponowienie nieidempotentnej operacji. Jeśli rekord pozostał, worker ponownie wykona idempotentne zamknięcie i usunięcie. Jeśli rekord zniknął, oba kroki filesystemu już wcześniej zakończyły się sukcesem.
7. ExecuteDeleteAsync po Id traktuje zero usuniętych wierszy jako poprawne zakończenie przez inną instancję. Aktualizacja backoff również filtruje po Id i nie odtwarza usuniętej intencji. Brak trackingu zapobiega używaniu nieaktualnego stanu z poprzedniego obiegu batcha.
8. Nieudana instalacja starego importera nie może odtworzyć katalogu ani opublikować Parquet. Fencing nadal chroni PostgreSQL; zamknięcie namespace dodatkowo chroni filesystem, również po usunięciu rekordu cleanup.

## Testy i przegląd

| Weryfikacja | Wynik |
| --- | --- |
| Build solution | 0 błędów, 0 ostrzeżeń |
| Wszystkie standardowe testy jednostkowe | 22/22 |
| Wszystkie standardowe testy integracyjne | 476/476; rzeczywisty PostgreSQL Testcontainers |
| Kosztowne benchmarki Explicit | 3 niewykonane zgodnie ze standardowym trybem |
| Istniejące LocalStorageNamespaceTests na Linux | 26/26, w tym rzeczywiście niezależne procesy |

Nowe pokrycie ograniczono do wariantu błędu zamknięcia, wariantu błędu SQL przed zakończeniem zadania oraz jednego testu równoczesnego zakończenia. Pozostałe testy dostosowują wcześniejsze scenariusze do nowej semantyki.

Test późnego importera zatrzymuje prawdziwą konwersję przed instalacją już przygotowanego stagingowego Parquet albo przed zakończeniem odczytu zbuforowanego źródła. Po recovery oraz DELETE datasetu lub projektu cleanup kończy się przed zwolnieniem bramki. Próba InstallAsync otrzymuje StorageNamespaceClosedException; plik i katalog nie powstają, metadane pozostają usunięte, kolejka jest pusta. Nie wykorzystuje opóźnień do ustalania porządku wyścigu.

Test SQL wstrzykuje wyjątek przed wykonaniem DELETE albo po rzeczywistym ExecuteNonQuery w PostgreSQL. Niezależny kontekst potwierdza odpowiednio istnienie albo brak rekordu. Kolejny przebieg cleanup jest bezpieczny w obu przypadkach.

Test dwóch processorów zatrzymuje pierwszy przed końcowym DELETE, po zakończeniu operacji filesystemu. Drugi processor usuwa to samo zadanie. Pierwszy otrzymuje zero zmienionych wierszy i poprawnie kończy pracę.

Linux: oficjalny obraz mcr.microsoft.com/dotnet/sdk:10.0, SDK 10.0.404, runtime 10.0.15, Ubuntu 24.04, kernel WSL2 6.18.33.2, lokalny /tmp na overlayfs. Uruchomiono przenośne assembly z Windowsowego builda bez przebudowy kodu pomocniczego. VSTest oczekiwał Linuxowego apphost, dlatego użyto istniejącego bezpośredniego runnera xUnit:

```powershell
docker run --rm `
  --mount 'type=bind,source=C:/Users/janro/RiderProjects/analytic-dashboard-v2/backend/tests/AnalyticDashboard.IntegrationTests/bin/Debug/net10.0,target=/tests,readonly' `
  --workdir /tmp mcr.microsoft.com/dotnet/sdk:10.0 `
  dotnet /tests/AnalyticDashboard.IntegrationTests.dll `
  -class '*LocalStorageNamespaceTests' -noColor
```

Build i standardowy zestaw Windows:

```powershell
dotnet build AnalyticDashboard.slnx --no-restore --nologo
dotnet test AnalyticDashboard.slnx --no-build --no-restore --nologo
```

TRX: C:/Users/janro/AppData/Local/Temp/aperture-aud006b-cleanup-tests/ (końcowy przebieg: prefiks aud006b-cleanup-final; Linux: linux/aud006b-linux-namespace.trx).

Przegląd techniczny: kolejność filesystem/SQL, brak długiej transakcji, anulowanie, niepewne potwierdzenie, równoczesne zakończenie i izolacja opublikowanego pliku. Przegląd spójności: istniejący podział odpowiedzialności, minimalny kontrakt, wspólna instancja scoped storage, brak zastępczego systemu cleanup, migracji i zmian StorageTestHost. git diff --check bez błędów białych znaków.

## Warunek wdrożenia i granice

**Przed uruchomieniem cleanup wygaszającego intencje należy zatrzymać wszystkie starsze procesy zapisujące z pominięciem protokołu namespace.** Dotyczy to również nadal działających importerów. Sam marker nie zatrzyma procesu, który go nie sprawdza. Nie jest bezpieczne mieszane wdrożenie z takimi writerami.

Gwarancja obejmuje współpracujące procesy korzystające z tego samego fizycznego root LocalFileStorage. Nie zapewnia zamknięcia odrębnych lokalnych dysków innych hostów. Wynik Linux nie stanowi certyfikacji innych filesystemów ani sieciowego storage. Zachowano obecne ograniczenia protokołu.

Nie usuwa się trwałych markerów zamknięcia ani nie otwiera ponownie namespace. Istniejące terminy NextAttemptAtUtc są respektowane; po sukcesie nie powstaje kolejna godzinna kontrola. Nie implementowano sweepera stagingu/workspace, retencji źródeł CSV ani kolejnego etapu AUD-006.

Pełny diff bieżącego etapu: [aud006b-cleanup-integration.diff](aud006b-cleanup-integration.diff). Jest liczony od stanu zastanego przy rozpoczęciu tego zadania, zawiera kod, testy i ten raport; nie powiela wcześniejszego niezatwierdzonego protokołu ani samego pliku diff.
