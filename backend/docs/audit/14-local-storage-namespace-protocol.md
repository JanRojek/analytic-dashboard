# Aperture — AUD-006B, etap 1: protokół namespace w LocalFileStorage

Data: 2026-11-15. Zakres: staging, atomowa instalacja bez nadpisywania i trwałe zamknięcie namespace datasetu. Bez commita, migracji, zmian HTTP, frontendu, limitów importu i schematu PostgreSQL.

## 1. Wdrożony kontrakt i konsumenci

LocalFileStorage udostępnia:
- StageAsync(Stream, CancellationToken) → LocalStagedFile;
- InstallAsync(LocalStagedFile, storageKey, CancellationToken);
- CloseNamespaceAsync(datasetId, CancellationToken);
- GetNamespaceStateAsync(datasetId, CancellationToken).

LocalStagedFile jest uchwytem własności prywatnego pliku. Należy go zwolnić przez await using; zwolnienie usuwa wyłącznie jego ścieżkę stagingową. Po instalacji ta ścieżka już nie istnieje. Uchwyt nie udostępnia publicznie fizycznej ścieżki i nie może być zainstalowany przez inną instancję storage.

Nie rozbudowano IFileStorage o niezweryfikowane gwarancje innych providerów ani nie zmieniano kilkunastu adapterów testowych. Nowe operacje są jawnym kontraktem konkretnego LocalFileStorage. Dotychczasowy IFileStorage.SaveAsync jest implementowany przez staging i instalację, więc obecni producenci również respektują zamknięcie.

Sprawdzono produkcyjnych konsumentów IFileStorage: ImportCsvDatasetHandler, DuckDbCsvImporter, DuckDbParquetExecutor, GetDatasetProfileHandler i StorageCleanupProcessor oraz rejestrację DI i adaptery testowe. Klucze tworzone przez produkcję są zgodne z wymaganą postacią kanoniczną. Nie zmieniono ImportJobProcessor, DuckDbCsvImporter ani StorageCleanupWorker.

Przykład:

```csharp
await using var staged = await storage.StageAsync(source, cancellationToken);
await storage.InstallAsync(
    staged,
    $"datasets/{datasetId:N}/versions/{versionId:N}/attempts/{Guid.NewGuid():N}.parquet",
    cancellationToken
);

await storage.CloseNamespaceAsync(datasetId, cancellationToken);
```

SaveAsync wykonuje ten sam przepływ automatycznie. Wczesny odczyt stanu służy do odrzucenia już zamkniętego namespace przed kosztownym kopiowaniem; właściwa kontrola uprawnienia odbywa się ponownie pod blokadą instalacji.

## 2. Lokalizacja i synchronizacja

Układ wewnątrz root storage:

```text
.aperture-storage/
  staging/<losowy identyfikator>
  namespaces/<datasetId w formacie N>
datasets/<datasetId>/...
```

Plik namespaces/<id> jest jednocześnie stałym plikiem kontrolnym i punktem synchronizacji. Nie jest usuwany przy DeleteDirectoryAsync("datasets/<id>") ani po zwolnieniu blokady. Nie używa DeleteOnClose, wymiany inode ani TTL.

Na Windows FileShare.None wyklucza inne uchwyty w tym i innych procesach. Na Linux kod dodatkowo wymaga skutecznego, jawnego flock; nie polega na samym FileShare, którego implementacja Unix ignoruje część błędów blokowania. Brak obsługi blokady kończy operację błędem. Uzasadnienie: [kontrakt FileShare](https://learn.microsoft.com/en-us/dotnet/api/system.io.fileshare?view=net-10.0), [implementacja .NET dla Unix](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/Microsoft/Win32/SafeHandles/SafeFileHandle.Unix.cs).

Czekanie na zajęty namespace ma odstęp 20 ms i respektuje CancellationToken. To ograniczenie pollingu rzeczywistej blokady, nie mechanizm ustalania porządku wyścigu w testach. Nie ma arbitralnego przejmowania blokady po czasie; zakończenie procesu zwalnia uchwyt przez OS.

Instalacja utrzymuje blokadę od odczytu stanu przez przygotowanie katalogu docelowego do zakończenia operacji instalacji. Zamknięcie używa tego samego pliku. Inny dataset ma inny plik kontrolny i pozostaje dostępny.

Nowy plik kontrolny jest inicjalizowany jako Open tylko przez operację, która faktycznie utworzyła go przez CreateNew, i tylko jeśli pod uzyskaną blokadą nadal ma długość zero. Ten drugi warunek chroni Unix, gdzie utworzenie pliku i uzyskanie flock są osobnymi zdarzeniami: wcześniejszy closer nie może zostać nadpisany przez spóźnionego twórcę.

## 3. Trwałe stany i niepewny wynik zamknięcia

| Stan | Reprezentacja / znaczenie |
| --- | --- |
| Uninitialized | Brak pliku kontrolnego; nie jest deklarowany jako potwierdzone Open |
| Open | Dokładnie osiem bajtów AP_NS_O1 |
| Closed | Szesnaście bajtów, z AP_NS_C1 w drugiej części |
| Unknown | Istniejący plik o innej lub niepełnej zawartości |

Zamknięcie dopisuje rekord od offsetu 8, a następnie wykonuje Flush(flushToDisk: true). Nie nadpisuje otwartego rekordu i nie przywraca jego długości do 8. Każdy częściowo dopisany marker różni się od poprawnego Open i blokuje instalację. Nowe zamknięcie przed pierwszą instalacją również tworzy prawidłowy Closed.

Unknown nigdy nie jest automatycznie naprawiany do Open. Jawne ponowienie CloseNamespaceAsync może bezpiecznie doprowadzić go do Closed. Wyjątek z operacji lub brak odpowiedzi procesu nie oznacza Open. Błąd odczytu/uzyskania blokady jest zgłaszany; nie jest zamieniany na wynik bool=false.

Po rozpoczęciu krótkiego zapisu zamknięcia kod doprowadza go do końca bez dodatkowego punktu anulowania pomiędzy zapisem i flush. CancellationToken służy m.in. do przerwania oczekiwania na blokadę. Ponowienie zamknięcia jest idempotentne i ponownie potwierdza zapis.

Na Windows używane są WriteThrough i Flush(true); na Linux dodatkowo fsync katalogów potrzebny do utrwalenia wpisów. Gwarancje trwałości opierają się na zachowaniu OS i filesystemu. [Dokumentacja Flush(true)](https://learn.microsoft.com/en-us/dotnet/api/system.io.filestream.flush?view=net-10.0).

Zamknięcie nie usuwa istniejących plików ani nie blokuje ich odczytu. Uprawnienie biznesowe do jego wykonania musi ustalić przyszły konsument — sam storage nie autoryzuje DELETE w PostgreSQL.

## 4. Instalacja i wynik niepewny

Staging:
- używa CreateNew i losowej nazwy we własnym katalogu storage;
- kopiuje strumieniowo, zamyka i synchronizuje plik przed instalacją;
- nie utrzymuje blokady namespace podczas kopiowania ani flush dużych danych;
- przy błędzie usuwa wyłącznie plik, który utworzył. Nieudane CreateNew nie uruchamia usuwania istniejącego pliku.

Instalacja korzysta z dedykowanej operacji natywnej:
- Windows: MoveFileExW z WRITE_THROUGH, bez COPY_ALLOWED i bez REPLACE_EXISTING;
- Linux: renameat2 z RENAME_NOREPLACE, bez fallbacku copy/delete; po sukcesie synchronizowane są katalogi;
- brak wymaganej operacji kończy się błędem zamiast emulacji kopiowaniem.

Dzięki temu kolizja zachowuje istniejący plik, a przeniesienie między wolumenami nie zamienia się w kopiowanie dużych danych pod blokadą. Dokumentacja Windows jawnie wiąże kopiowanie między wolumenami z flagą COPY_ALLOWED, której kod nie przekazuje: [MoveFileExW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-movefileexw).

Błąd może pojawić się po rzeczywistym przeniesieniu, np. podczas synchronizacji lub przekazywania potwierdzenia. Kod nie usuwa wtedy celu i nie ponawia automatycznie instalacji. Dispose uchwytu dotyczy tylko starej ścieżki stagingu. Istnienie celu nie jest samoistnym dowodem, że należał do danej nieudanej operacji; nie zwracamy fikcyjnego sukcesu przy kolizji.

## 5. Granice ścieżek

Namespace datasetu ma postać datasets/<kanoniczny, niepusty Guid N>/.... CloseNamespaceAsync przyjmuje Guid zamiast dowolnej ścieżki.

Odrzucane są:
- ścieżki absolutne, puste segmenty i traversal;
- backslash, ADS i inne niekanoniczne znaki;
- końcowe spacje/kropki, aliasy DOS 8.3 z tyldą i nazwy urządzeń Windows;
- aliasy wielkości liter prefixu datasets oraz niekanoniczne identyfikatory;
- próby dostępu do zarezerwowanego katalogu .aperture-storage;
- nie-ASCII prefixy najwyższego poziomu mogące komplikować porównanie nazw;
- reparse points / symlinki w ścieżkach storage.

Instalacja nie może zastąpić całego katalogu namespace plikiem. Zamknięcie jednego Guid nie wpływa na inne Guid.

Root storage jest własnością aplikacji. Protokół zakłada współpracujące procesy i brak zewnętrznego podmieniania plików kontrolnych, linków lub mountów. Nie jest sandboxem przeciw administratorowi albo innemu procesowi mającemu prawo dowolnie zmieniać ten katalog. Wykrywane sieciowe rooty Windows są odrzucane; SMB/NFS i zachowania innych hostów nie są certyfikowane tym wdrożeniem.

## 6. Testy nowych gwarancji

Dodano LocalStorageNamespaceTests — 26 przypadków:
- dwie niezależne instancje procesu, instalacja pierwsza;
- dwie niezależne instancje procesu, zamknięcie pierwsze;
- niezależny dataset pozostaje zapisywalny podczas blokady pierwszego;
- śmierć procesu po utrwaleniu Closed, przed zwróceniem odpowiedzi;
- świeży proces rozpoznaje Closed także po usunięciu payloadu;
- zamknięcie podczas trwającego kopiowania do stagingu;
- pusty, uszkodzony oraz częściowo dopisany marker — blokada instalacji i bezpieczne ponowienie zamknięcia;
- granice namespace, traversal i aliasy ścieżek;
- odrzucenie uchwytu stagingu obcej instancji storage;
- wyjątek po rzeczywistej instalacji zachowuje kompletny cel.

Istniejące trzy LocalFileStorageWriteTests nadal sprawdzają ochronę istniejącego pliku oraz cleanup własnego częściowego zapisu po błędzie/anulowaniu; nie dodano ich kopii.

Mały konsolowy StorageTestHost znajduje się pod Support/StorageTestHost w projekcie IntegrationTests i służy do rzeczywistej synchronizacji między procesami. Nie ma własnych pakietów ani frameworka testowego. IntegrationTests wyklucza ten katalog z domyślnych elementów projektu, buduje host przez ProjectReference i kopiuje jego pliki uruchomieniowe do swojego outputu. Zachowanie osobnego punktu wejścia unika zastępowania Main generowanego przez xUnit v3.

Bramki wykorzystują standardowe logi Debug po uzyskaniu blokady oraz sygnał faktycznego sharing violation. Parent czeka na potwierdzone zablokowanie drugiego procesu przed zwolnieniem pierwszego. Sprawdzane są różne PID; nie używa się Task.WhenAll jako substytutu procesów ani opóźnień do ustalania zwycięzcy.

Test utraty potwierdzenia instalacji zgłasza wyjątek w obserwatorze logu po prawdziwym natywnym przeniesieniu. Test niepewnego zamknięcia zabija prawdziwy proces po zapisie i flush, przed odpowiedzią protokołu. Nie symulowano awarii zasilania komputera ani fizycznego uszkodzenia dysku.

Końcowa weryfikacja:
- build: 0 błędów i 0 ostrzeżeń;
- 22/22 testy jednostkowe;
- 475/475 integracyjnych, w tym 26 nowych i istniejące scenariusze AUD-001/AUD-004/AUD-006A/AUD-008;
- trzy kosztowne benchmarki nadal Explicit/NotExecuted.

Weryfikację wykonano na Windows 10.0.26200, lokalnym NTFS, runtime .NET 10.0.11. Kod Linux wymaga odrębnego wykonania tego samego zestawu na docelowym filesystemie; nie deklaruje się jego weryfikacji runtime w tej sesji. Rzeczywistego przeniesienia pomiędzy dwoma wolumenami nie uruchamiano; brak fallbacku wynika z użytych operacji i flag natywnych.

Wyniki: [aud006b1-namespace-test-results.json](aud006b1-namespace-test-results.json).

## 7. Koszt pliku 1 GiB

Przeprowadzono jedną kontrolowaną próbę na NTFS, z generowanym strumieniem zer i buforem 81920 bajtów:
- wejście i zainstalowany plik: 1 073 741 824 bajty;
- staging wraz z flush: 1608,1101 ms;
- instalacja, obejmująca pierwsze utworzenie stanu namespace: 27,2513 ms;
- szczyt working set procesu pomocniczego: 28 733 440 bajtów, około 27,4 MiB.

To pomiar storage, nie benchmark konwersji CSV ani SLA. Plik zerowy i stan cache ograniczają reprezentatywność. Nie mierzono fizycznie zaalokowanych bloków dysku ani awarii zasilania.

Duży plik jest kopiowany do stagingu jeden raz, a potem przenoszony. Nie powstaje druga pełna kopia w fazie instalacji. Przy importerze pozostaje dotychczasowa kopia roboczego Parquet; nowy staging zastępuje dotychczasowy bezpośredni zapis trwałej kopii. Force-flush może zwiększać opóźnienie zapisu; koszt występuje poza blokadą.

Direct COPY, pamięć 512 MiB, Zstd 3, wielkość grup i dwa sloty nie zostały zmienione. Nie podniesiono limitów HTTP.

Pomiar: [aud006b1-storage-1g.json](aud006b1-storage-1g.json). Testowy payload usunięto po weryfikacji. Opcjonalny tryb large w StorageTestHost umożliwia powtórzenie tej samej próby na prywatnym root.

## 8. Pozostawiony zakres i warunki wdrożenia

- CloseNamespaceAsync nie jest jeszcze wywoływany przez biznesowe DELETE ani cleanup. StorageCleanupJob nadal pozostaje po sukcesie zgodnie z AUD-006A.
- Nie wolno teraz wygaszać tych rekordów tylko dlatego, że biblioteka storage potrafi zamknąć namespace. Następny etap musi potwierdzać zamknięcie właściwego storage po zatwierdzonym DELETE.
- Stare binaria zapisujące bez tego protokołu muszą zakończyć pracę przed wykorzystaniem Closed do wygaszania intencji.
- Staging pozostawiony po crashu nie ma jeszcze recovery/sweepera. Nie usuwa się go według wieku.
- Znaczniki Open/Closed pozostają poza payloadem i nie mają automatycznej retencji. DatasetId nie może być ponownie użyty.
- Nie dodano przyjęć ImportAdmission, cleanup terminalnych wersji, retencji CSV/historii ani modelu wielu lokalizacji storage.
- Zamknięcie oznacza wyłącznie zakaz nowych instalacji przez ten protokół, nie usunięcie plików ani decyzję o ich biznesowej własności.
- Trwałość podlega gwarancjom OS/filesystemu i urządzenia. Utrata lub ręczne skasowanie katalogu protokołu poza API narusza założenia; kontrolnych plików nie wolno traktować jako usuwalnego cache.

## 9. Zmiany i przegląd

Zmienione istniejące pliki:
- src/AnalyticDashboard.Infrastructure/Storage/LocalFileStorage.cs;
- tests/AnalyticDashboard.IntegrationTests/AnalyticDashboard.IntegrationTests.csproj.

Nowe pliki kodu:
- src/AnalyticDashboard.Infrastructure/Storage/LocalFileStorage.Namespaces.cs;
- src/AnalyticDashboard.Infrastructure/Storage/LocalStagedFile.cs;
- src/AnalyticDashboard.Infrastructure/Storage/LocalStorageNamespaceState.cs;
- src/AnalyticDashboard.Infrastructure/Storage/StorageNamespaceClosedException.cs;
- src/AnalyticDashboard.Infrastructure/Storage/AtomicFileInstall.cs;
- tests/AnalyticDashboard.IntegrationTests/Storage/LocalStorageNamespaceTests.cs;
- tests/AnalyticDashboard.IntegrationTests/Support/StorageTestHost/AnalyticDashboard.StorageTestHost.csproj;
- tests/AnalyticDashboard.IntegrationTests/Support/StorageTestHost/Program.cs.

Dokumentacja: ten raport, dwa pliki wyników i [pełny diff](aud006b1-namespace.diff).

Przegląd techniczny obejmował oba porządki blokad, stabilność pliku kontrolnego, częściowy zapis zamknięcia, wyścig tworzenia/flock na Unix, brak nadpisania celu, cleanup wyłącznie własnego stagingu i zachowanie celu po niepewnej instalacji. Nie ma ścieżki kopiowania dużych danych pod blokadą namespace.

Przegląd spójności: istniejące ILogger, using/await using, brak inicjalizatorów IDisposable przy using, brak nowej hierarchii portów i brak pakietów. Zachowano istniejące metody odczytu i kasowania; usunięto zastąpiony bezpośredni zapis do celu. Pełny diff obejmuje tylko ten etap względem treści plików odczytanych na początku zadania i nie zawiera samego pliku diff.
