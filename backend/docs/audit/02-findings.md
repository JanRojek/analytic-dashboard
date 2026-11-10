# Audyt backendu — ustalenia

Data: 2026-11-06. Ścieżki i linie względem katalogu `backend`. Kolejność według skutku i prawdopodobieństwa, nie nazw plików. Nie wdrożono poprawek.

## Jak czytać raport

- **Confirmed**: mechanizm wynika bezpośrednio z kodu i ograniczeń bazy. Nie oznacza, że odtworzono go na żywej bazie lub że wystąpił w produkcji.
- **Likely**: mechanizm ma mocne uzasadnienie, ale wpływ zależy np. od topologii wdrożenia lub sposobu użycia przez klienta.
- **Needs verification**: wskazano konkretną hipotezę i warunki jej sprawdzenia; nie przedstawia się jej jako dowiedzionej podatności.
- **High**: ryzyko utraty danych, obchodzenia ważnej ochrony lub znacznego przeciążenia. **Medium**: istotna niezawodność, kontrakt API, koszt rosnący wraz z danymi. **Low**: porządkowanie lub brak dowodu istotnego wpływu obecnie. Nie stwierdzono problemu uzasadniającego Critical w poznanym kontekście.
- Wysiłek obejmuje poprawkę i testy, bez kalendarzowej obietnicy: Small — lokalna zmiana, Medium — kilka współpracujących elementów, Large — szersza zmiana przepływu/danych. Ryzyko regresji jest odrębne od ważności problemu.

## Indeks

| ID | Priorytet | Pewność | Problem |
|---|---|---|---|
| AUD-001 | High | Confirmed | Pliki datasetu kasowane przed SQL, który może zostać odrzucony |
| AUD-002 | High | Confirmed | Logowanie pomija blokady Identity i naliczanie prób |
| AUD-003 | High | Confirmed | Kwadratowa lista wszystkich kolizji bez limitu widgetów |
| AUD-004 | High | Likely | Import nie jest bezpieczny przy wielu workerach / rolling restart |
| AUD-005 | High | Needs verification | Brak backfill danych w historycznych migracjach |
| AUD-006 | Medium | Confirmed | Osierocone CSV/Parquet po usunięciu danych |
| AUD-007 | Medium | Confirmed | Błąd finalizacji importu pozostawia Running do restartu |
| AUD-008 | Medium | Likely | Niejednoznaczny commit może wywołać błędny cleanup pliku |
| AUD-009 | Medium | Confirmed | Agregacja zwraca i buforuje nieograniczoną liczbę grup |
| AUD-010 | Medium | Confirmed | Każdy odczyt kopiuje cały Parquet |
| AUD-011 | Medium | Confirmed | Profil wykonuje osobne agregacje dla każdej kolumny przy każdym GET |
| AUD-012 | Medium | Confirmed | Niepoprawna nazwa dashboardu daje 500 |
| AUD-013 | Medium | Confirmed | Numeryczna nieznana agregacja przechodzi parsowanie i daje 500 |
| AUD-014 | Medium | Confirmed | Nazwa uploadu/datasetu nie ma limitu zgodnego z bazą |
| AUD-015 | Medium | Confirmed | WidgetId z innego dashboardu powoduje błąd PK i 500 |
| AUD-016 | Medium | Confirmed | Usunięcie zależności po walidacji daje nieobsłużony błąd FK |
| AUD-017 | Medium | Confirmed | Awaria dostarczania e-mail jest niewidoczna operacyjnie |
| AUD-018 | Medium | Likely | SMTP nie potrafi wymusić STARTTLS |
| AUD-019 | Medium | Confirmed | Test równoległego HTTP nie gwarantuje przejścia przez catch concurrency |
| AUD-022 | Medium | Likely | Revision i stan widgetów nie pochodzą z jednego odczytu |
| AUD-023 | Medium | Needs verification | Upload cookie-auth wyłącza antiforgery |
| AUD-024 | Medium | Likely | Zbyt długi e-mail może przekroczyć kolumny Identity |
| AUD-020 | Low | Confirmed | Nieużywane API persistence, typy i zależność |
| AUD-021 | Low | Likely | Niepaginowane listy datasetów/dashboardów rosną bez limitu |

Problemy Medium wymagające kontekstu klienta lub wdrożenia umieszczono po potwierdzonych problemach tej samej kategorii, a przed sugestiami Low. Numery ID pozostają stabilne niezależnie od kolejności.

## AUD-001 — utrata plików po odrzuconym usunięciu datasetu

- **Kategoria:** integralność / operacje wieloetapowe. **Priorytet:** High. **Pewność:** Confirmed.
- **Lokalizacja:** `src/AnalyticDashboard.Application/Datasets/DeleteDataset/DeleteDatasetHandler.cs:39-64`; `src/AnalyticDashboard.Infrastructure/Repositories/DatasetRepository.cs:66-83`; `src/AnalyticDashboard.Infrastructure/Data/Configurations/WidgetConfiguration.cs:60-63`.
- **Aktualne zachowanie:** handler usuwa pliki każdej wersji, następnie wykonuje DELETE datasetu. FK z Widgets do datasets ma Restrict.
- **Mechanizm i konsekwencja:** filesystem nie podlega rollbackowi transakcji PostgreSQL. Odrzucenie DELETE pozostawia w bazie wersję Ready i widgety wskazujące usunięty Parquet. Kolejne odczyty danych przestają działać; może być potrzebne ponowne odtworzenie pliku z zewnętrznej kopii/CSV.
- **Scenariusz / dowód:** zaimportować poprawny dataset, utworzyć odwołujący się widget, usunąć dataset. Plik jest kasowany przed naruszeniem FK. Tak samo niebezpieczny jest błąd bazy lub anulowanie po usunięciu części plików.
- **Kierunek poprawy:** najpierw atomowo rozstrzygnąć politykę usunięcia metadanych (np. konflikt, gdy są widgety), a cleanup wykonywać po commit, z trwałą możliwością ponowienia. Sam pre-check `Any(widget)` nie zamyka wyścigu; sam SQL transaction nie wycofa pliku.
- **Testy:** obecne `tests/AnalyticDashboard.IntegrationTests/Datasets/DeleteDatasetEndpointTests.cs:65-111` tworzą dataset bez wersji/pliku. Potrzebny referenced dataset z prawdziwym plikiem, błąd SQL i przerwany cleanup.
- **Ryzyko regresji:** Medium/High — semantyka DELETE, referencje widgetów, powtarzanie cleanup. **Wysiłek:** Medium.

## AUD-002 — obejście polityki blokowania kont przy logowaniu

- **Kategoria:** bezpieczeństwo / authentication. **Priorytet:** High. **Pewność:** Confirmed.
- **Lokalizacja:** `src/AnalyticDashboard.Infrastructure/Identity/UserAccountService.cs:163-197`; konfiguracja i pipeline w `src/AnalyticDashboard.Api/Program.cs:57-117,169-182`.
- **Aktualne zachowanie:** FindByEmail → CheckPasswordAsync → kontrola EmailConfirmed → SignInAsync. Brak AccessFailedAsync, IsLockedOutAsync i standardowego password sign-in check; w backendzie nie znaleziono rate limitera.
- **Mechanizm i konsekwencja:** błędne próby nie podnoszą licznika; aktywna blokada nie jest sprawdzana przed wystawieniem cookie. Łatwiejsze zgadywanie haseł i credential stuffing; blokada ustawiona w danych nie realizuje oczekiwanej ochrony.
- **Scenariusz / dowód:** potwierdzone konto z przyszłym LockoutEnd i poprawnym hasłem przechodzi ten sam kod co konto bez blokady. Dowolna liczba błędnych haseł nie wywołuje w aplikacji operacji naliczania porażek. Zewnętrzne limity proxy są nieznane i nie naprawiają ignorowania LockoutEnd.
- **Kierunek poprawy:** użyć właściwego sprawdzania hasła przez SignInManager z lockoutOnFailure; jawnie zmapować wyniki. Dodać rozsądne limity auth per adres i identyfikator, uwzględniając ryzyko blokowania ofiary przez napastnika. Limity objąć również register/forgot/resend, które mogą generować koszt CPU/SMTP.
- **Testy:** aktywna blokada, próg niepowodzeń, wygaśnięcie blokady, reset licznika, RememberMe i niepotwierdzony e-mail po zmianie pipeline'u. Obecne testy loginu tych ochron nie sprawdzają.
- **Ryzyko regresji:** Medium — odpowiedzi HTTP, liczniki i sesje. **Wysiłek:** Medium.

## AUD-003 — nadmierny koszt wykrywania kolizji layoutu

- **Kategoria:** bezpieczeństwo zasobów / wydajność. **Priorytet:** High. **Pewność:** Confirmed.
- **Lokalizacja:** `src/AnalyticDashboard.Application/Widgets/UpdateDashboardWidgets/UpdateDashboardWidgetsHandler.cs:62-82`; `src/AnalyticDashboard.Api/Endpoints/WidgetEndpoints.cs:140-151,318-322` (brak limitu kolekcji i ogólna odpowiedź kolizji; szczegóły listy nie są używane przez HTTP).
- **Aktualne zachowanie:** porównanie każdej pary i utworzenie rekordu dla każdej kolizji. Brak limitu liczby przesyłanych widgetów.
- **Mechanizm i konsekwencja:** O(n²) czasu oraz O(n²) pamięci w najgorszym przypadku. Ochrona rozmiarem body nie ogranicza dostatecznie mnożenia pracy. Pętla nie sprawdza anulowania.
- **Scenariusz / dowód:** 5000 elementów z unikalnymi GUID, poprawną geometrią i identycznym prostokątem daje `5000*4999/2 = 12 497 500` rekordów kolizji. Wystarczy własny istniejący dashboard i aktualna revision; dataset nie musi przejść późniejszej walidacji.
- **Kierunek poprawy:** limit liczby widgetów przed mapowaniem i w handlerze; przerwanie po pierwszej kolizji lub ograniczona diagnostyka. Przy niewielkim limicie prosta pętla jest wystarczająca — indeks przestrzenny nie jest potrzebny.
- **Testy:** max/max+1, pusta lista, kolizja na początku i końcu, styk krawędzi, anulowanie większego dopuszczonego żądania. Nie wykonywano obciążającego przykładu podczas audytu.
- **Ryzyko regresji:** Low/Medium — ustalenie realnego limitu produktu. **Wysiłek:** Small.

## AUD-004 — brak wyłącznego przejęcia importu i bezpiecznego recovery wielu instancji

- **Kategoria:** współbieżność / integralność / wdrożenie. **Priorytet:** High. **Pewność:** Likely (zagrożenie wymaga więcej niż jednego workera; brak ochrony potwierdzony).
- **Lokalizacja:** `src/AnalyticDashboard.Infrastructure/Repositories/ImportJobRepository.cs:21-46`; `src/AnalyticDashboard.Infrastructure/Services/Import/ImportJobProcessor.cs:44-50`; `src/AnalyticDashboard.Infrastructure/Services/Import/ImportJobRecoveryService.cs:25-53`; `src/AnalyticDashboard.Infrastructure/Services/Import/DuckDbCsvImporter.cs:62-80`; `src/AnalyticDashboard.Infrastructure/Data/Configurations/ImportJobConfiguration.cs:28-31`.
- **Aktualne zachowanie:** odczyt Pending i zapis Running są osobne; job nie ma tokena ani właściciela przejęcia. Każdy start workera traktuje wszystkie Running jako przerwane i usuwa ich wyniki.
- **Mechanizm i konsekwencja:** dwie instancje mogą wybrać ten sam job, pracować na tym samym kluczu wyniku i nadpisać status. Nieudane CreateNew może spowodować cleanup wyniku utworzonego przez drugą instancję. Recovery przy rolling restart może uszkodzić nadal aktywną pracę.
- **Scenariusz / dowód:** A i B odczytują Pending przed którymkolwiek SaveChanges; oba lokalne obiekty pozwalają MarkRunning. Albo A przetwarza Running, a startująca B uruchamia recovery. Deployment z jednym workerem nie uruchamia tego scenariusza.
- **Kierunek poprawy:** najprościej jawnie egzekwować jednego workera, jeśli odpowiada to potrzebom. Dla wielu instancji atomowe przejęcie z właścicielem/wygasaniem i recovery wyłącznie porzuconych przejęć; cleanup tylko własnego artefaktu. Ustalić wspólność storage — lokalne dyski różnych instancji nie zastępują współdzielonego magazynu.
- **Testy:** dwa niezależne scope z synchronizacją przed przejęciem; start drugiej instancji podczas pracy; awaria zwycięzcy i przejęcie po wygaśnięciu.
- **Ryzyko regresji:** Medium/High. **Wysiłek:** Large dla pełnej obsługi wielu instancji. Wariant egzekwowania jednego workera jest mniejszy (Medium), jeśli odpowiada wdrożeniu.

## AUD-005 — migracje wymagają weryfikacji na historycznych danych

- **Kategoria:** integralność / aktualizacja instalacji. **Priorytet:** High. **Pewność:** Needs verification co do zagrożenia istniejącej instalacji; operacje destrukcyjne i brak backfill są widoczne w kodzie.
- **Lokalizacja:** `src/AnalyticDashboard.Infrastructure/Migrations/20260906201541_AddDatasetVersions.cs:14-45`; `src/AnalyticDashboard.Infrastructure/Migrations/20260910161507_RefactorDashboardAndWidgetModel.cs:18-29,45-66,83-98`.
- **Aktualne zachowanie:** migracja wersji usuwa stare StoredPath/OriginalFileName/RowCount/ColumnCount bez utworzenia odpowiadających wersji. Późniejsza migracja zmienia nazwę Dashboard.DatasetId na ProjectId bez przeliczenia wartości, a Widget.DatasetId dodaje z Guid.Empty i następnie FK. XColumn/YColumn są usuwane bez przeniesienia do nowych pól.
- **Mechanizm i konsekwencja:** pusta baza nie ujawnia problemu. Dla starego dashboardu identyfikator datasetu nie staje się automatycznie identyfikatorem projektu; dodanie FK może się nie powieść. Stare wskaźniki do plików i konfiguracje mogą zostać utracone lub wymagać ręcznej rekonstrukcji.
- **Scenariusz / dowód:** aktualizacja bazy zawierającej dataset z plikiem, dashboard i widget sprzed tych migracji. Nie ustalono, czy taka baza istnieje i czy historyczne migracje były świadomie traktowane jako reset środowiska developerskiego.
- **Kierunek poprawy:** ustalić wspierany punkt startu i historię wdrożeń. Dla utrzymywanych instalacji przygotować osobny, sprawdzony plan przeniesienia danych i backup; nie edytować pochopnie migracji już zastosowanych. Jeśli wspierana jest tylko nowa baza, udokumentować ten fakt i kontrolowany bootstrap.
- **Testy:** docelowo upgrade reprezentatywnego historycznego schematu z danymi i zachowaniem metadanych/odwołań. W tym audycie żadnej migracji nie uruchomiono.
- **Ryzyko regresji:** High — zależne od historii baz. **Wysiłek:** Large, jeśli trzeba zachować dane historycznej instalacji; zakres wymaga ustalenia wspieranej ścieżki.

## AUD-006 — brak domknięcia usuwania plików i retencji

- **Kategoria:** integralność / storage / retencja danych. **Priorytet:** Medium. **Pewność:** Confirmed.
- **Lokalizacja:** `src/AnalyticDashboard.Application/Datasets/DeleteDataset/DeleteDatasetHandler.cs:39-64`; `src/AnalyticDashboard.Infrastructure/Repositories/ProjectRepository.cs:96-110`; `src/AnalyticDashboard.Infrastructure/Data/Configurations/ImportJobConfiguration.cs:43-47`; `src/AnalyticDashboard.Infrastructure/Services/Import/ImportJobProcessor.cs:56-127`.
- **Aktualne zachowanie:** DELETE datasetu sprząta tylko StorageKey wersji, nie SourceStorageKey joba. DELETE projektu wykonuje SQL bez cleanup plików. Import nie usuwa źródła po sukcesie/porażce.
- **Mechanizm i konsekwencja:** po usunięciu metadanych znika powiązanie pozwalające znaleźć źródłowy CSV, a przy projekcie również Parquet. Narasta zajętość dysku; dane użytkownika pozostają mimo usunięcia zasobu w API. To problem lifecycle, bez przypisywania konkretnego naruszenia prawnego.
- **Scenariusz / dowód:** poprawny import → DELETE datasetu bez widgetów pozostawia source.csv; DELETE projektu z importem pozostawia oba rodzaje plików. Retencja CSV przed usunięciem może być celowa, po usunięciu pozostaje orphan.
- **Kierunek poprawy:** ustalić retencję i objąć wszystkie klucze trwałym cleanup po zatwierdzeniu usunięcia; umożliwić ponawianie i bezpieczne rozpoznawanie osieroconych artefaktów. Powiązać rozwiązanie z AUD-001, nie dodawać kolejnego kasowania przed SQL.
- **Testy:** rzeczywiste pliki po delete dataset/project, źródło joba Failed/Pending/Running, błąd cleanup i ponowienie.
- **Ryzyko regresji:** Medium/High — omyłkowy cleanup aktywnego importu. **Wysiłek:** Medium.

## AUD-007 — import pozostaje Running po przejściowej awarii bazy

- **Kategoria:** niezawodność / state machine. **Priorytet:** Medium. **Pewność:** Confirmed.
- **Lokalizacja:** `src/AnalyticDashboard.Infrastructure/Services/Import/ImportJobProcessor.cs:46-50,75-77,100-125`; `src/AnalyticDashboard.Infrastructure/Services/Import/ImportJobWorker.cs:25-32,75-85`; `src/AnalyticDashboard.Infrastructure/Repositories/ImportJobRepository.cs:21-27`.
- **Aktualne zachowanie:** Running zapisano wcześniej. Błąd finalnego SaveChanges powoduje cleanup i wyjątek; worker loguje i wraca do wybierania Pending. Recovery uruchamia tylko na starcie.
- **Mechanizm i konsekwencja:** po pojedynczej awarii zapisującej transakcji import nie ma ścieżki dalszej obsługi w żyjącym procesie. Użytkownik widzi stale Running mimo że nikt go już nie przetwarza. Podobnie po nieudanym zapisie Failed.
- **Scenariusz / dowód:** błąd DB przed commit finalizacji, następnie poprawa dostępności DB bez restartu API. Nowe joby mogą działać, poprzedni nie zostaje ponownie pobrany.
- **Kierunek poprawy:** ograniczone retry/reconciliation dla konkretnego porzuconego zadania z rozróżnieniem aktywnego przetwarzania; nie wykonywać bezrefleksyjnie całego importu ponownie. Rozwiązać razem z przejęciem i publikacją artefaktu.
- **Testy:** kontrolowana awaria finalizacji i zapisu Failed; następna iteracja/recovery ma doprowadzić do jawnego końcowego stanu.
- **Ryzyko regresji:** Medium. **Wysiłek:** Medium.

## AUD-008 — cleanup zakłada, że wyjątek oznacza brak commit

- **Kategoria:** integralność / granica DB–plik. **Priorytet:** Medium. **Pewność:** Likely.
- **Lokalizacja:** `src/AnalyticDashboard.Application/Datasets/ImportCsvDataset/ImportCsvDatasetHandler.cs:103-128`; `src/AnalyticDashboard.Infrastructure/Services/Import/ImportJobProcessor.cs:100-124`.
- **Aktualne zachowanie:** każdy wyjątek z tworzenia metadanych/finalizacji powoduje usunięcie odpowiednio źródła lub wyniku.
- **Mechanizm i konsekwencja:** po utracie połączenia podczas potwierdzania commit klient nie zawsze wie, czy serwer zatwierdził transakcję. Bezwarunkowy cleanup może usunąć plik wskazywany przez skutecznie zapisane Pending/Ready.
- **Scenariusz / dowód:** PostgreSQL zatwierdza transakcję, połączenie zanika przed potwierdzeniem do aplikacji, catch usuwa plik. To uzasadnione ryzyko protokołu, nie scenariusz odtworzony w audycie.
- **Kierunek poprawy:** idempotentne tworzenie/publikacja z ustaleniem końcowego stanu w świeżym scope lub późniejszą rekoncyliacją; nie usuwać od razu przy nieznanym wyniku commit. Unikać automatycznego ponawiania całej operacji bez klucza idempotencji.
- **Testy:** fault injection na granicy commit, ponowny odczyt i reconciliation; ochrona committed artefaktu.
- **Ryzyko regresji:** Medium/High. **Wysiłek:** Medium.

## AUD-009 — nieograniczona liczba punktów agregacji

- **Kategoria:** wydajność / limity API. **Priorytet:** Medium. **Pewność:** Confirmed dla nieograniczonej pracy, skala skutku zależy od danych.
- **Lokalizacja:** `src/AnalyticDashboard.Infrastructure/Services/Analytics/DuckDbDatasetQueryReader.cs:156-195`; `src/AnalyticDashboard.Application/Analytics/QueryDataset/DatasetQueryExecutor.cs:80-100`.
- **Aktualne zachowanie:** GROUP BY, ORDER BY i materializacja całego wyniku, bez limitu liczności ani stronicowania.
- **Mechanizm i konsekwencja:** grupowanie po unikalnym ID/tekście może zwrócić tyle grup co wierszy. Koszt sortowania, pamięci i serializacji rośnie mimo że wynik jest przeznaczony dla widgetu/wykresu. Istnieją kolejne listy mapowań wyników.
- **Scenariusz / dowód:** zaimportowany plik z wieloma unikalnymi etykietami i query/groupBy na tej kolumnie. Nie zmierzono progu problemu w tym środowisku.
- **Kierunek poprawy:** określić limit punktów oraz semantykę przekroczenia (błąd, top N + Other lub osobny paginowany raport). Sam LIMIT ograniczy wynik, ale nie musi wyeliminować kosztu pełnej agregacji; dodatkowo ograniczyć równoległość/czas zasobochłonnej pracy.
- **Testy:** duża liczność, limit+1, null etykiety, stabilne sortowanie i jawny brak cichej utraty danych.
- **Ryzyko regresji:** Medium — semantyka wykresów. **Wysiłek:** Medium.

## AUD-010 — pełna kopia Parquet dla każdego odczytu

- **Kategoria:** wydajność / I/O. **Priorytet:** Medium. **Pewność:** Confirmed.
- **Lokalizacja:** `src/AnalyticDashboard.Infrastructure/Services/DuckDb/DuckDbParquetExecutor.cs:30-66`; `src/AnalyticDashboard.Infrastructure/Services/Datasets/DuckDbDatasetRowsReader.cs:22-32`.
- **Aktualne zachowanie:** cały plik jest kopiowany do nowego temp, po czym otwierana jest nowa instancja DuckDB in-memory. Dotyczy także strony 10 wierszy.
- **Mechanizm i konsekwencja:** koszt I/O pozostaje O(rozmiaru pliku), niezależnie od rozmiaru odpowiedzi. Wiele widgetów tego samego datasetu powiela transfer i zajętość temp. To jawny koszt kodu, bez twierdzenia o zmierzonej latencji.
- **Scenariusz / dowód:** 20 odczytów widgetów jednego datasetu uruchamia 20 kopii tego samego Parquet; osobne strony rows również powtarzają kopię.
- **Kierunek poprawy:** pomiar na realnym pliku; dla lokalnego storage bezpieczny dostęp do niezmiennego pliku albo ograniczony cache kopii po ID wersji. Nie przeciekać ścieżkami filesystemu do Application/API. Pooling silnika rozważać dopiero po pomiarze i ocenie thread safety.
- **Testy:** liczba/bajty kopiowania przy wielu odczytach, równoległe delete i odczyt, cleanup temp oraz brak odczytu innej wersji.
- **Ryzyko regresji:** Medium — lifetime pliku i synchronizacja. **Wysiłek:** Medium.

## AUD-011 — profilowanie powtarza agregacje po kolumnach

- **Kategoria:** wydajność. **Priorytet:** Medium. **Pewność:** Confirmed dla liczby zapytań; koszt odczytanych bajtów zależy od optymalizacji Parquet/DuckDB.
- **Lokalizacja:** `src/AnalyticDashboard.Infrastructure/Services/Profiling/DuckDbDatasetProfileReader.cs:65-85,147-168,230-248`; `src/AnalyticDashboard.Application/Datasets/GetDatasetProfile/GetDatasetProfileHandler.cs` (wywołanie czytnika w każdym żądaniu).
- **Aktualne zachowanie:** schema, osobna agregacja dla każdej kolumny i preview są obliczane ponownie przy każdym GET profilu niezmiennej wersji.
- **Mechanizm i konsekwencja:** szeroki dataset daje wiele kolejnych komend; powtarzanie odczytu profilu nie wykorzystuje niezmienności wersji. Nie jest to EF lazy-loading N+1 ani dowód, że każda komenda fizycznie czyta wszystkie kolumny.
- **Scenariusz / dowód:** 100 kolumn oznacza 100 komend agregujących oprócz schematu/preview, powtarzanych przez kolejnych klientów.
- **Kierunek poprawy:** cache profilu po wersji lub obliczenie przy imporcie, gdy będzie mierzalna korzyść; ewentualnie łączenie agregacji. Nie uruchamiać bez ograniczeń równoległych zapytań na współdzielonym connection.
- **Testy:** wynik cache po publikacji nowej wersji, liczba wywołań, numeric/text/date/null i szeroki plik.
- **Ryzyko regresji:** Medium. **Wysiłek:** Medium.

## AUD-012 — błędna nazwa dashboardu nie staje się błędem walidacji

- **Kategoria:** poprawność API / walidacja. **Priorytet:** Medium. **Pewność:** Confirmed.
- **Lokalizacja:** `src/AnalyticDashboard.Application/Dashboards/CreateDashboard/CreateDashboardHandler.cs:35-43`; `src/AnalyticDashboard.Domain/Entities/Dashboard.cs:38-53`; `src/AnalyticDashboard.Api/Endpoints/DashboardEndpoints.cs:28-77`.
- **Aktualne zachowanie:** konstruktor rzuca InvalidEntityNameException; handler/endpoint nie tłumaczą go na znany wynik. Globalny handler zwraca 500.
- **Mechanizm i konsekwencja:** poprawne składniowo JSON z błędną nazwą jest traktowane jak awaria serwera. Inne moduły, np. Project, obsługują ten sam rodzaj błędu przewidywalnie.
- **Scenariusz / dowód:** właściciel istniejącego projektu wysyła `{"name":" "}` albo 101 run Unicode. Data annotations nie są tu mechanizmem walidacji tej encji.
- **Kierunek poprawy:** jawny InvalidName mapowany na 400, z użyciem istniejących reguł domeny. Nie dodawać ogólnego catch wszystkich ArgumentException jako zamiennika mapowania znanych problemów.
- **Testy:** null/missing/blank, 100/101 run, trim i Unicode, bez zapisu rekordu po odrzuceniu.
- **Ryzyko regresji:** Low. **Wysiłek:** Small.

## AUD-013 — nieznana liczba jako Aggregation daje 500

- **Kategoria:** walidacja payloadu. **Priorytet:** Medium. **Pewność:** Confirmed.
- **Lokalizacja:** `src/AnalyticDashboard.Api/Endpoints/DatasetEndpoints.cs:464-481`; `src/AnalyticDashboard.Application/Analytics/QueryDataset/DatasetQueryExecutor.cs:32-86`; `src/AnalyticDashboard.Infrastructure/Services/Analytics/DuckDbDatasetQueryReader.cs:133-147`.
- **Aktualne zachowanie:** Enum.TryParse dopuszcza reprezentację liczbową bez Enum.IsDefined. W readerze default rzuca InvalidOperationException.
- **Mechanizm i konsekwencja:** żądanie klienta uruchamia kosztowny odczyt pliku/schematu, po czym kończy się 500 zamiast 400. Endpoint widgetów ma już dodatkowe Enum.IsDefined, więc kontrakty są niespójne.
- **Scenariusz / dowód:** `aggregation: "999"`, istniejący dataset i poprawna numeryczna measureColumn.
- **Kierunek poprawy:** jawnie odrzucić niezdefiniowane wartości przed pracą I/O; rozważyć walidację także na wejściu wspólnego executora.
- **Testy:** wszystkie agregacje, tekst nieznany, liczba nieznana, null/blank; query analytics ma obecnie pojedynczy happy path Sum (`QueryDatasetEndpointTests.cs:30`).
- **Ryzyko regresji:** Low. **Wysiłek:** Small.

## AUD-014 — długość nazwy CSV jest weryfikowana dopiero przez bazę

- **Kategoria:** walidacja / zgodność invariantów. **Priorytet:** Medium. **Pewność:** Confirmed.
- **Lokalizacja:** `src/AnalyticDashboard.Application/Datasets/ImportCsvDataset/ImportCsvDatasetHandler.cs:39-84,95-108`; `src/AnalyticDashboard.Domain/Entities/Dataset.cs:26-35`; `src/AnalyticDashboard.Infrastructure/Data/Configurations/DatasetConfiguration.cs:22-24`; `src/AnalyticDashboard.Infrastructure/Data/Configurations/DatasetVersionConfiguration.cs:23-25`; `src/AnalyticDashboard.Infrastructure/Data/Configurations/ImportJobConfiguration.cs:20-22`.
- **Aktualne zachowanie:** nazwa musi być niepusta i kończyć się .csv; baza wymaga max 200 dla Dataset.Name i 255 dla nazw pliku.
- **Mechanizm i konsekwencja:** klient może spowodować zapis źródła, nieudany SaveChanges, cleanup i 500 dla przewidywalnego błędu wejścia.
- **Scenariusz / dowód:** nazwa z 201 liter `a` i `.csv` przekracza limit nazwy datasetu. Testy importu obejmują poprawny upload i cudzy projekt, nie granice długości.
- **Kierunek poprawy:** wspólne stałe i zgodna walidacja nazwy bazowej/pełnej przed zapisem; zachować zgodność z liczeniem znaków PostgreSQL i regułami Unicode w pozostałych encjach.
- **Testy:** 200/201, nazwa pliku 255/256, znaki spoza BMP, pusta nazwa bazowa, brak plików/rekordów po odrzuceniu.
- **Ryzyko regresji:** Low. **Wysiłek:** Small.

## AUD-015 — globalny konflikt WidgetId kończy się 500

- **Kategoria:** poprawność / kontrakt synchronizacji. **Priorytet:** Medium. **Pewność:** Confirmed.
- **Lokalizacja:** `src/AnalyticDashboard.Application/Widgets/UpdateDashboardWidgets/UpdateDashboardWidgetsHandler.cs:132-172,197-218`; `src/AnalyticDashboard.Infrastructure/Data/Configurations/WidgetConfiguration.cs:13`.
- **Aktualne zachowanie:** ID nieobecne w aktualnym dashboardzie traktowane jest jako nowy widget, ale PK jest globalny. Catch obejmuje tylko OptimisticConcurrencyException.
- **Mechanizm i konsekwencja:** naruszenie PK daje DbUpdateException/500. DB zachowuje integralność — to nie jest IDOR ani nadpisanie istniejącego widgetu.
- **Scenariusz / dowód:** użytkownik przesyła w dashboardzie B ID istniejącego widgetu ze swojego dashboardu A, np. przy kopiowaniu konfiguracji. INSERT nie może się udać.
- **Kierunek poprawy:** ustalić kontrakt ID klienta i mapować konkretny konflikt PK na przewidywalny wynik bez ujawniania właściciela. Wstępny lookup sam nie usuwa race. Zmiana na ID serwera wymaga oceny realnego klienta i retry.
- **Testy:** ID z innego dashboardu tego samego i innego właściciela; brak zmian widgetów/revision po odrzuceniu. Dwa równoległe zapisy tworzące ten sam ID wymagają osobnej weryfikacji kolejności komend; nie przesądza się tu ich odpowiedzi HTTP.
- **Ryzyko regresji:** Medium. **Wysiłek:** Medium.

## AUD-016 — TOCTOU zależności nie ma przewidywalnego mapowania

- **Kategoria:** współbieżność / błędy persistence. **Priorytet:** Medium. **Pewność:** Confirmed dla wskazanych sekwencji.
- **Lokalizacja:** `src/AnalyticDashboard.Application/Dashboards/CreateDashboard/CreateDashboardHandler.cs:24-43`; `src/AnalyticDashboard.Application/Widgets/UpdateDashboardWidgets/UpdateDashboardWidgetsHandler.cs:89-94,158-199`; `src/AnalyticDashboard.Infrastructure/Data/Configurations/DashboardConfiguration.cs:25-28`; `src/AnalyticDashboard.Infrastructure/Data/Configurations/WidgetConfiguration.cs:60-63`.
- **Aktualne zachowanie:** aplikacja sprawdza projekt/dataset, a później zapisuje rekord zależny. Znane FK violations nie mają mapowania na wynik aplikacyjny.
- **Mechanizm i konsekwencja:** równoległe DELETE po pozytywnej walidacji powoduje 500. FK i transakcja chronią bazę — nie wykazano częściowego zapisu widgetów ani utraty Revision.
- **Scenariusz / dowód:** usunięcie projektu po check przed utworzeniem dashboardu; albo usunięcie jeszcze nieużywanego datasetu po GetExistingDatasetIds, przed utworzeniem/przepięciem widgetu.
- **Kierunek poprawy:** obsłużyć konkretne constrainty i po potrzebnym świeżym odczycie zwrócić brak zasobu/konflikt. Nie opakowywać wszystkich DbUpdateException w 404. Nie wydłużać transakcji blokującej na całą walidację, jeśli wystarczy właściwe mapowanie błędu.
- **Testy:** kontrolowana przerwa między walidacją a zapisem, usunięcie zależności, brak częściowego commitu i brak 500. Usunięcie samego dashboardu podczas save ma już ścieżkę concurrency→404; testować ją zamiast deklarować jej brak.
- **Ryzyko regresji:** Medium. **Wysiłek:** Medium.

## AUD-017 — błędy SMTP znikają bez śladu

- **Kategoria:** niezawodność / obserwowalność. **Priorytet:** Medium. **Pewność:** Confirmed.
- **Lokalizacja:** `src/AnalyticDashboard.Application/Auth/Register/RegisterUserHandler.cs:78-86`; `src/AnalyticDashboard.Application/Auth/ForgotPassword/ForgotPasswordHandler.cs:38-46`; `src/AnalyticDashboard.Application/Auth/ResendConfirmation/ResendConfirmationHandler.cs:32-42`; `src/AnalyticDashboard.Infrastructure/Auth/Email/SmtpEmailSender.cs:80-113`.
- **Aktualne zachowanie:** EmailDeliveryException jest przechwytywany pustym catch; sender nie loguje.
- **Mechanizm i konsekwencja:** użytkownik nie dostaje wiadomości potrzebnej do ukończenia procesu, a operator nie ma sygnału awarii SMTP. Ogólna odpowiedź HTTP w forgot/resend jest właściwa i nie wymaga ujawniania konta ani błędu dostawcy.
- **Scenariusz / dowód:** niedostępny SMTP; testy świadomie oczekują sukcesu publicznego mimo błędu (`RegisterUserEndpointTests.cs:761`, `ForgotPasswordEndpointTests.cs:162`, `ResendConfirmationEndpointTests.cs:330`). Nie sprawdzają śladu operacyjnego.
- **Kierunek poprawy:** bezpieczny log/metryka z identyfikatorem korelacji i typem błędu; bez tokenów, haseł i HTML. Retry/outbox dopiero przy konkretnym wymaganiu dostawy, nie jako obowiązkowa nowa warstwa.
- **Testy:** niezmieniona odpowiedź publiczna i zarejestrowanie kontrolowanego sygnału błędu.
- **Ryzyko regresji:** Low, ale uważać na dane w logach. **Wysiłek:** Small.

## AUD-018 — ograniczona konfiguracja bezpieczeństwa SMTP

- **Kategoria:** bezpieczeństwo transportu / konfiguracja. **Priorytet:** Medium. **Pewność:** Likely dla ekspozycji, brak STARTTLS jest potwierdzony.
- **Lokalizacja:** `src/AnalyticDashboard.Infrastructure/Auth/Email/SmtpEmailSender.cs:47-67`; `src/AnalyticDashboard.Infrastructure/Auth/Email/SmtpOptions.cs`.
- **Aktualne zachowanie:** UseSsl wybiera tylko SslOnConnect albo None; potem może nastąpić AuthenticateAsync.
- **Mechanizm i konsekwencja:** nie da się wyrazić wymaganego STARTTLS. Przy zdalnym SMTP z UseSsl=false połączenie nie wymusza TLS; zależnie od mechanizmu auth/serwera dane lub poświadczenia mogą być narażone albo wysyłka odrzucona.
- **Scenariusz / dowód:** dostawca wymagający STARTTLS na porcie submission. Sprawdzone ustawienia repo wskazują lokalny SMTP bez poświadczeń; nie stwierdzono rzeczywistego wycieku ani błędnego produkcyjnego wdrożenia.
- **Kierunek poprawy:** jawne tryby None / StartTls / SslOnConnect, walidacja przy starcie, celowy wyjątek dla lokalnego środowiska. Nie wprowadzać automatycznego downgrade dla zdalnego serwera.
- **Testy:** wybór trybu i odrzucenie niespójnej konfiguracji; zgodność z rzeczywistym dostawcą.
- **Ryzyko regresji:** Medium — kompatybilność SMTP. **Wysiłek:** Small.

## AUD-019 — luka dowodowa testów concurrency na poziomie handlera

- **Kategoria:** testowalność / fałszywe poczucie pokrycia. **Priorytet:** Medium. **Pewność:** Confirmed.
- **Lokalizacja:** `tests/AnalyticDashboard.IntegrationTests/Widgets/UpdateDashboardWidgetsEndpointTests.cs:957-1005,1008-1051`; `src/AnalyticDashboard.Application/Widgets/UpdateDashboardWidgets/UpdateDashboardWidgetsHandler.cs:201-218`.
- **Aktualne zachowanie:** test dwóch HTTP requestów używa Task.WhenAll, lecz nie synchronizuje odczytów dashboardu. Osobny test UnitOfWork deterministycznie weryfikuje konflikt i rollback w dwóch kontekstach.
- **Mechanizm i konsekwencja:** test HTTP może przejść przy wykonaniu szeregowym: drugi request odpada w pierwszym porównaniu revision. Nie dowodzi to wykonania catch, ponownego odczytu i mapowania 404/409. Nie oznacza to, że test rollback jest pozorny — jest wartościowy i pokrywa inną granicę.
- **Scenariusz / dowód:** pierwszy request kończy SaveChanges przed odczytem drugiego; asercje 1×200, 1×409 nadal przechodzą.
- **Kierunek poprawy:** dodać kontrolowany test handlera/endpointu wymuszający dwóm scope odczyt tej samej revision; drugi wariant usuwa dashboard po załadowaniu. Zachować istniejący test UnitOfWork i asercje całego stanu.
- **Testy:** catch przy rzeczywistym konflikcie DB, requery currentRevision, dashboard usunięty, brak częściowej zmiany create/update/delete.
- **Ryzyko regresji:** Low dla testów; unikać sleepów i zależności od schedulerów. **Wysiłek:** Medium.

## AUD-022 — revision nie jest powiązana z pobranym stanem edytora

- **Kategoria:** kontrakt odczytu / optimistic concurrency. **Priorytet:** Medium. **Pewność:** Likely, zależne od klienta.
- **Lokalizacja:** `src/AnalyticDashboard.Application/Widgets/GetWidgets/GetWidgetsResponse.cs:6-17`; `src/AnalyticDashboard.Application/Widgets/GetWidgets/GetWidgetsHandler.cs:18-43`; `src/AnalyticDashboard.Application/Dashboards/GetDashboardById/GetDashboardByIdHandler.cs:18-37`.
- **Aktualne zachowanie:** lista widgetów nie zawiera revision; dashboard i widgety pobierane są osobnymi endpointami.
- **Mechanizm i konsekwencja:** jeśli klient połączy starszą listę z nowszą revision, poprawny mechanizm zapisu zaakceptuje odtworzenie starszego stanu. To ryzyko odczytu, nie dowód nieskutecznego tokena EF.
- **Scenariusz / dowód:** A czyta widgety Rev0 → B zapisuje Rev1 → A czyta dashboard Rev1 → A zapisuje stary zestaw z ExpectedRevision=1. Nie sprawdzono, czy frontend faktycznie może wykonać tę sekwencję.
- **Kierunek poprawy:** najpierw prześledzić klienta. Jeśli potrzebny wspólny stan edytora, zwracać `{revision, widgets}` ze spójnego snapshotu. Dwa SELECT w zwykłym ReadCommitted nie gwarantują takiego powiązania; wybrać najprostszy właściwy odczyt/projekcję lub izolację.
- **Testy:** zmiana między odczytami, zgodność revision z pełnym zwróconym zestawem, ponowny zapis bez cichego przywrócenia starszej konfiguracji.
- **Ryzyko regresji:** Medium — kontrakt klienta. **Wysiłek:** Medium.

## AUD-023 — brak jawnej ochrony CSRF uploadu

- **Kategoria:** bezpieczeństwo / cookies. **Priorytet:** Medium. **Pewność:** Needs verification.
- **Lokalizacja:** `src/AnalyticDashboard.Api/Endpoints/DatasetEndpoints.cs:31-93`; konfiguracja cookies i middleware w `src/AnalyticDashboard.Api/Program.cs:90-117,169-182`.
- **Aktualne zachowanie:** endpoint multipart jest uwierzytelniany cookie i ma DisableAntiforgery; w pipeline nie znaleziono walidacji antiforgery ani Origin.
- **Mechanizm i konsekwencja:** SameSite Lax ogranicza zwykłe cross-site POST, ale nie jest ogólną granicą zaufania pomiędzy originami tego samego site. CORS sam w sobie nie blokuje wysłania prostego formularza. Ryzykiem jest import bez zamiaru użytkownika i zużycie storage/CPU.
- **Scenariusz / dowód:** nieufny/skompromitowany sibling origin w tym samym site wysyła multipart z cookies do znanego projectId. Nie ustalono, czy taka topologia istnieje; nie odtworzono ataku przeglądarkowego. Nie stwierdza się uniwersalnej podatności wszystkich endpointów JSON.
- **Kierunek poprawy:** ustalić originy i sposób hostowania frontendu; dla cookie-auth uploadu rozważyć token antiforgery lub konsekwentną weryfikację zaufanego Origin w połączeniu z polityką cookies. Nie wyłączać zabezpieczeń tylko dlatego, że API wymaga logowania.
- **Testy:** prawdziwa przeglądarka: trusted origin, obcy site, nieufny sibling origin, brak/niepoprawny token; odpowiedzi 401/403 zgodne z przyjętym kontraktem.
- **Ryzyko regresji:** Medium — upload i integracja klienta. **Wysiłek:** Medium po rozpoznaniu deploymentu.

## AUD-024 — brak limitu długości e-maila zgodnego z Identity schema

- **Kategoria:** walidacja / auth. **Priorytet:** Medium. **Pewność:** Likely (dowód statyczny, brak uruchomienia integracji).
- **Lokalizacja:** `src/AnalyticDashboard.Application/Auth/Register/RegisterUserHandler.cs:24-53`; `src/AnalyticDashboard.Infrastructure/Identity/UserAccountService.cs:30-62`; `src/AnalyticDashboard.Infrastructure/Migrations/AppDbContextModelSnapshot.cs:302-320,339-340`.
- **Aktualne zachowanie:** rejestracja ogranicza DisplayName i wymaga hasła, ale nie sprawdza długości Email. Email zapisuje też do UserName; kolumny Identity mają limit 256. Catch obejmuje unikalność, nie przekroczenie długości.
- **Mechanizm i konsekwencja:** standardowy walidator Identity nie gwarantuje zgodności z limitem kolumny; przesadnie długi adres może zakończyć zapis błędem PostgreSQL i 500 zamiast 400.
- **Scenariusz / dowód:** 257 liter `a` + `@example.com` przy poprawnej reszcie payloadu. Brak limitu potwierdzono również w [UserValidator dla użytej wersji Identity](https://raw.githubusercontent.com/dotnet/aspnetcore/v10.0.10/src/Identity/Extensions.Core/src/UserValidator.cs) oraz [EmailAddressAttribute](https://raw.githubusercontent.com/dotnet/runtime/v10.0.10/src/libraries/System.ComponentModel.Annotations/src/System/ComponentModel/DataAnnotations/EmailAddressAttribute.cs). Ostateczny wynik wymaga testu z dostawcą PostgreSQL.
- **Kierunek poprawy:** jawna zgodna walidacja Email/UserName i postaci normalizowanej; korzystać z jednego limitu schema, nie polegać na przypadkowym zachowaniu zewnętrznego walidatora.
- **Testy:** 256/257, Unicode i normalizacja, brak rekordu po odrzuceniu, dotychczasowe concurrent duplicate registration.
- **Ryzyko regresji:** Low/Medium — zgodność reguł Identity. **Wysiłek:** Small.

## AUD-020 — martwe elementy zwiększają powierzchnię utrzymania

- **Kategoria:** jakość C# / uproszczenie. **Priorytet:** Low. **Pewność:** Confirmed co do braku wywołań w src/tests; zalecenie usunięcia jest sugestią projektową.
- **Lokalizacja:** `src/AnalyticDashboard.Infrastructure/Services/Csv/CsvStructureInfo.cs:3-6`; `src/AnalyticDashboard.Infrastructure/AnalyticDashboard.Infrastructure.csproj:10`; `src/AnalyticDashboard.Application/Datasets/Persistence/IDatasetRepository.cs:7-10,32-38`; `src/AnalyticDashboard.Infrastructure/Repositories/DatasetRepository.cs:17-26,86-132`; `src/AnalyticDashboard.Application/Datasets/Persistence/IDatasetVersionRepository.cs:7-10`; `src/AnalyticDashboard.Infrastructure/Repositories/DatasetVersionRepository.cs:19-28`; `src/AnalyticDashboard.Application/Datasets/Persistence/IImportJobRepository.cs:11-14`; `src/AnalyticDashboard.Infrastructure/Repositories/ImportJobRepository.cs:55-64`; `src/AnalyticDashboard.Domain/Entities/Dashboard.cs:33-36`; `src/AnalyticDashboard.Domain/Entities/Widget.cs:85-88`.
- **Aktualne zachowanie:** repozytorium zawiera CsvStructureInfo, referencję CsvHelper i publiczne metody, których nie wywołują obecne ścieżki. Import używa PendingDatasetImportRepository i publikacji w processorze.
- **Mechanizm i konsekwencja:** czytelnik musi rozważać alternatywne, martwe ścieżki zapisu i ich gwarancje; pakiet nie wnosi wykazanej funkcji. Nie stwierdzono stąd błędu działającego endpointu.
- **Scenariusz / dowód:** przeszukanie symboli w całym src/tests, z wyłączeniem wygenerowanych bin/obj, zwraca deklaracje/implementacje bez wywołań dla wymienionych elementów.
- **Kierunek poprawy:** po sprawdzeniu konsumentów poza solution usunąć niepotrzebne API/typ/pakiet. Nie zachowywać ich „na wszelki wypadek” i nie tworzyć zamienników. Rename zachować tylko przy rzeczywistym planie użycia.
- **Testy:** świeża kompilacja solution i istniejące testy po przyszłej zmianie; nie dodawać testów odtwarzających usuniętą implementację.
- **Ryzyko regresji:** Low w tym solution, nieznane poza nim. **Wysiłek:** Small.

## AUD-021 — listy zasobów bez paginacji

- **Kategoria:** wydajność / skala danych. **Priorytet:** Low. **Pewność:** Likely.
- **Lokalizacja:** `src/AnalyticDashboard.Infrastructure/Repositories/DatasetRepository.cs:28-44`; `src/AnalyticDashboard.Infrastructure/Repositories/DashboardRepository.cs:29-43`; `src/AnalyticDashboard.Application/Datasets/GetDatasets/GetDatasetsHandler.cs:41-93`.
- **Aktualne zachowanie:** pobierane są wszystkie datasety/dashboardy projektu; datasety mają dodatkowo zbiorcze bieżące wersje. Brak N+1 w tym mechanizmie.
- **Mechanizm i konsekwencja:** czas i odpowiedź rosną wraz z liczbą zasobów; brak wykazanego problemu dla obecnej wielkości projektów. Projekcja zmniejszyłaby payload z bazy, ale sama nie zastąpi ograniczenia liczby rekordów.
- **Scenariusz / dowód:** wielokrotny import tworzy nowe datasety, a listowanie nie ma Take/pageSize. Nie znana jest oczekiwana liczba zasobów na projekt.
- **Kierunek poprawy:** najpierw ustalić skalę/limity produktu; przy potrzebie dodać paginację/projekcję wzorowaną na prostym GetProjects. Lista widgetów jest częścią pełnego stanu edytora i wymaga innego limitu, nie mechanicznej paginacji.
- **Testy:** tylko przy zmianie kontraktu — sortowanie, granice stron, izolacja właścicieli.
- **Ryzyko regresji:** Medium dla klienta. **Wysiłek:** Medium.

## Obserwacje niebędące potwierdzonymi błędami

1. `Program.cs:84-87`: SecurityStampValidator z interwałem zero kosztuje zapytania DB, ale zapewnia natychmiastowe odwołanie sesji. Testy auth tego wymagają. Zmianę poprzedzić decyzją o akceptowalnym oknie ważności sesji, a nie samą chęcią redukcji zapytań.
2. Brak konfiguracji współdzielonych kluczy Data Protection oraz root storage niezależnego od CWD (`LocalFileStorage.cs:9-16`) wymaga weryfikacji wdrożenia, backupu i trwałości wolumenów. Brak tego w repo nie dowodzi utraty kluczy/plików na działającym serwerze.
3. `DuckDbDatasetQueryReader.cs:156-185`: agregaty są konwertowane na double. Jeśli produkt wymaga dokładnych kwot/liczb ponad 2^53, potrzebny jest inny kontrakt numeryczny. Dla wykresów przybliżone double może być celowe. Testów nonfinite/dużych decimal nie znaleziono.
4. `DatasetConfiguration.cs:35-39` wiąże CurrentVersionId z ID wersji, ale nie wymusza w samym FK zgodności DatasetId. Podobnie FK widgetów nie wymusza wspólnego projektu dashboardu i datasetu. Obecne ścieżki aplikacji sprawdzają te warunki; nie znaleziono wejścia HTTP naruszającego je. Mocniejsze constraints są opcją przy nowych writerach/importach administracyjnych, nie automatycznym obowiązkiem rozbudowy modelu.
5. Brak FK Project.OwnerId do Identity user może być celowym oddzieleniem cyklu życia kont i projektów. Nie ma endpointu usunięcia konta w badanym zakresie. Przed dodaniem kaskady ustalić retencję i odzyskiwanie danych.
6. Repeated ownership predicates, cienkie handlery, podobne DTO i różnice stylistyczne using nie zostały uznane za błędy. Nie ma dowodu, że mediator, generic repository lub nowa warstwa poprawiłyby te przepływy.
7. Brak własnego jawnego limitu bajtów uploadu nie oznacza, że upload jest nieograniczony: działają też limity serwera/form i ewentualnego proxy. Nie ustalono ich efektywnej konfiguracji. Należy określić budżet pliku, równoległości, czasu importu i quota właściciela; nie twierdzić bez pomiaru, że pojedynczy upload wyczerpie pamięć.

Pełna macierz brakujących scenariuszy i kolejność zabezpieczania zmian: [03-refactoring-roadmap.md](03-refactoring-roadmap.md).


