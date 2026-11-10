# Audyt C — jakość i utrzymywalność testów

Data: 2026-11-06. Drugi przegląd, po przeczytaniu raportów 01–03. Analiza statyczna wszystkich **39 plików C# w tests**, bez bin/obj: 34 klasy z testami oraz 5 plików infrastruktury testowej. Rozpoznano **165 metod testowych: 151 Fact i 14 Theory**; to liczba metod, nie rozwiniętych przypadków teorii ani wynik uruchomienia.

Nie zmieniano produkcji, testów, konfiguracji ani wcześniejszych raportów. W tym przeglądzie nie uruchamiano testów, migracji, kontenerów ani aplikacji. Informacja z pierwszego audytu o 19 zaliczonych wcześniej skompilowanych przypadkach jednostkowych nie potwierdza wykonawczo ustaleń tego dokumentu. Integracje inicjują migracje (`ApiFixture.cs:30-48`), więc pozostają poza dozwoloną weryfikacją wykonawczą.

Ścieżki i zakresy linii poniżej są względem backendu. Jeżeli tabela podaje katalog nadrzędny, nazwy plików w jej wierszach są względem tego katalogu. **Confirmed** dotyczy zaobserwowanego mechanizmu lub powielenia; wydzielenie helpera jest nadal sugestią projektową, a nie dowodem błędu testu. Nie powiela się tutaj jako nowych problemów braków funkcjonalnych i concurrency z AUD-001–024.

## Ocena

Testy mają wartościową bazę: rzeczywisty provider PostgreSQL, odczyt stanu po operacji w osobnym scope, zabezpieczenie niezmienności widgetów oraz testy prawdziwej sesji Identity. Główny koszt utrzymania to wielokrotne ręczne przygotowywanie tego samego konta lub zaimportowanego datasetu. Szczególnie w auth właściwe zachowanie testu bywa poprzedzone kilkudziesięcioma liniami identycznej obsługi wiadomości i tokena. Nie jest to uzasadnienie dla wspólnej klasy bazowej obejmującej cały zestaw.

Potwierdzono też dwa problemy jakości dowodu: część testów awarii SMTP nie wymaga, aby próba wysyłki nastąpiła, a część asercji usunięcia cookies sprawdza tylko obecność atrybutu daty. Lifecycle plików testowych jest mniej solidny niż lifecycle bazy: brak katalogu zarządzanego przez fixture, a cleanup może zostać pominięty przy anulowaniu lub wcześniejszej nieudanej asercji.

## Inwentarz zakresu

Każdy poniższy plik przeczytano, także helpery i końcowe bloki finally. Liczba oznacza metody Fact/Theory. Nie oznacza pełnego pokrycia funkcjonalnego.

### `tests/AnalyticDashboard.UnitTests`

| Plik / klasa | Metody | Co rzeczywiście sprawdza / ocena |
|---|---:|---|
| `Projects/ProjectTests.cs` / ProjectTests | 11 | Trim, Unicode i granice długości, owner, ID, UTC, zachowanie starej nazwy po błędzie; dobre małe testy domenowe. |
| `Widgets/WidgetTests.cs` / WidgetTests | 1 | Niepoprawny GroupBy nie zmienia wcześniej istniejących pól konfiguracji; istotny invariant, bez potrzeby wspólnego buildera dla jednego przypadku. |

### `tests/AnalyticDashboard.IntegrationTests` — infrastruktura

| Plik / klasa | Faktyczne działanie i granice |
|---|---|
| `ApiFixture.cs` / ApiFixture | Kontener i migracja per fixture, wspólny Client i Services w klasie, osobna leniwie tworzona fabryka real auth, fabryki zamykane przed PostgreSQL. Brak zarządzania storage przy Dispose. |
| `PostgresFixture.cs` / PostgresFixture | Kontener postgres:16.15, StartAsync/DisposeAsync. Faktycznie izoluje bazę między klasami używającymi IClassFixture. |
| `CustomWebApplicationFactory.cs` / CustomWebApplicationFactory | Testing, worker wyłączony, testowy connection string, przełączalne test auth, singleton TestEmailSender. Nie podmienia IFileStorage. |
| `TestAuthHandler.cs` / TestAuthHandler | Tożsamość wyłącznie z nagłówka; celowo pozwala przekazać błędny/empty Guid do walidacji API. Nie jest testem pipeline'u Identity. |
| `Auth/TestEmailSender.cs` / TestEmailSender | ConcurrentQueue wiadomości, atomowy jednorazowy wyjątek, snapshot kolekcji; brak informacji o próbie wysyłki zakończonej błędem. Zawiera także TestEmailMessage. |

### `tests/AnalyticDashboard.IntegrationTests` — klasy testowe

| Plik / klasa | Metody | Zakres i uwagi |
|---|---:|---|
| `ApiSmokeTests.cs` / ApiSmokeTests | 1 | Sukces health check DB; adekwatnie mały smoke test. |
| `Api/InvalidRequestBodyTests.cs` / InvalidRequestBodyTests | 1 | Niepoprawna składnia JSON na resend, 400; nie mylić z walidacją semantycznego null. |
| `Auth/RegisterUserEndpointTests.cs` / RegisterUserEndpointTests | 13 | Rekord/hasło/status potwierdzenia, walidacja, duplikaty, Unicode, concurrent create, null/missing i awaria e-mail; dużo lokalnych asercji stanu. |
| `Auth/ConfirmEmailEndpointTests.cs` / ConfirmEmailEndpointTests | 5 | Valid/invalid/missing user/already confirmed/concurrent; odczyt EmailConfirmed w DB, powtarzalne parsowanie linka. |
| `Auth/RegistrationStatusEndpointTests.cs` / RegistrationStatusEndpointTests | 5 | Pending/Confirmed, brak/uszkodzona sesja i usunięty użytkownik; ręczny Cookie i asercje wygaszenia. |
| `Auth/CompleteRegistrationEndpointTests.cs` / CompleteRegistrationEndpointTests | 5 | Wystawienie auth cookie i kasowanie registration cookie, niepotwierdzony e-mail i błędne/brakujące sesje, usunięty użytkownik. |
| `Auth/LoginUserEndpointTests.cs` / LoginUserEndpointTests | 9 | Credentials, status potwierdzenia, RememberMe i case-insensitive e-mail; kilka bardzo podobnych workflow rejestracji/potwierdzenia. |
| `Auth/LogoutUserEndpointTests.cs` / LogoutUserEndpointTests | 4 | Test auth i prawdziwe cookies, drugi logout, brak możliwości complete-registration po login/logout; lokalne klienty dla sesji. |
| `Auth/GetCurrentUserEndpointTests.cs` / **CurrentUserEndpointTests** | 6 | Dane użytkownika, test auth, real cookies, usunięcie użytkownika i kasowanie cookie; nazwa klasy różni się od pliku. |
| `Auth/ForgotPasswordEndpointTests.cs` / ForgotPasswordEndpointTests | 4 | Istniejący/nieistniejący/pusty e-mail i awaria; filtracja wiadomości po adresie, słaby dowód wykonania awarii. |
| `Auth/ResendConfirmationEndpointTests.cs` / ResendConfirmationEndpointTests | 5 | Nowa wiadomość i użycie jej tokena, brak/already confirmed/blank i awaria; analogiczna luka dowodu awarii. |
| `Auth/ResetPasswordEndpointTests.cs` / ResetPasswordEndpointTests | 9 | Stare/nowe hasło, invalid token/user/password, reuse, null/missing, concurrent reset, dwie prawdziwe sesje; posiada już użyteczny helper resetu. |
| `Projects/CreateProjectEndpointTests.cs` / CreateProjectEndpointTests | 10 | DTO, Location, owner/DB, walidacja nazw, unikalność per owner i wyścig, niepoprawne claims. |
| `Projects/GetProjectByIdEndpointTests.cs` / GetProjectByIdEndpointTests | 5 | DTO/UTC, owner, missing i auth; prosty powielony seed. |
| `Projects/GetProjectsEndpointTests.cs` / GetProjectsEndpointTests | 11 | Izolacja, page metadata, ordering/tie breaker, page bounds/pageSize; czasy sortowania ustalane jawnie, bez sleepów. |
| `Projects/RenameProjectEndpointTests.cs` / RenameProjectEndpointTests | 12 | Trim, state preservation, name conflict, owner, auth, rename/delete i różne konkurujące rename; parametryzacja InvalidNames. |
| `Projects/DeleteProjectEndpointTests.cs` / DeleteProjectEndpointTests | 6 | 204/404, brak rekordu i zachowanie cudzego rekordu, auth i powtórne równoległe delete. |
| `Projects/ProjectRepositoryTests.cs` / ProjectRepositoryTests | 1 | Context działa po konflikcie, odłączenie wadliwej encji i skuteczny kolejny zapis; sensowny test repozytorium. |
| `Projects/ProjectDatabaseConstraintsTests.cs` / ProjectDatabaseConstraintsTests | 2 | Raw SQL omija domenę; konkretne constraints oraz whitespace. Transakcja lokalnie sprząta nieudany scenariusz. |
| `Datasets/GetDatasetsEndpointTests.cs` / GetDatasetsEndpointTests | 2 | Dokładny zbiór datasetów projektu, izolacja właściciela; tylko metadane. |
| `Datasets/GetDatasetByIdEndpointTests.cs` / GetDatasetByIdEndpointTests | 3 | Bieżąca Ready wersja, inny owner i inny projekt; seed wersji z osobnym PublishVersion. |
| `Datasets/DeleteDatasetEndpointTests.cs` / DeleteDatasetEndpointTests | 2 | Usunięcie i zachowanie cudzego datasetu bez plików; luka funkcjonalna już opisana w AUD-001. |
| `Datasets/ImportCsvDatasetEndpointTests.cs` / ImportCsvDatasetEndpointTests | 2 | Cały Pending graph/plik i odmowa przed walidacją pliku dla obcego ownera; sprawdza wiele invariantów DB. |
| `Datasets/GetImportStatusEndpointTests.cs` / GetImportStatusEndpointTests | 3 | Pending, foreign owner, bezpieczny tekst Failed zamiast ścieżki wewnętrznej. |
| `Datasets/GetDatasetProfileEndpointTests.cs` / GetDatasetProfileEndpointTests | 1 | Import i processor, pola profilu/number/null/min/max/avg/preview; duży Arrange. |
| `Analytics/QueryDatasetEndpointTests.cs` / QueryDatasetEndpointTests | 1 | Import i processor, dwie grupy z Sum; setup niemal identyczny jak profil. |
| `Dashboards/GetDashboardByIdEndpointTests.cs` / GetDashboardByIdEndpointTests | 1 | Odmowa odczytu cudzego projektu; reszta luk funkcjonalnych w raporcie 03. |
| `Widgets/GetWidgetDataEndpointTests.cs` / GetWidgetDataEndpointTests | 1 | Import → dashboard → save widget → read layout → aggregate data; wartościowy przepływ, wiele zachowań w jednej metodzie. |
| `Widgets/UpdateDashboardWidgetsEndpointTests.cs` / UpdateDashboardWidgetsEndpointTests | 19 | Snapshoty, walidacja, synchronizacja ID i mixed create/update/delete, null/geometria, Revision, concurrency i rollback UoW; mocne lokalne helpery. |
| `Import/DuckDbCsvImporterTests.cs` / DuckDbCsvImporterTests | 1 | Konwersja i metadane plus PAR1 magic; importer nie korzysta z DB, mimo ciężkiego ApiFixture. |
| `Import/ImportJobProcessorTests.cs` / ImportJobProcessorTests | 2 | Sukces publikacji oraz brak source; stan DB i storage, przy sukcesie również publiczny status. |
| `Import/ImportJobRecoveryServiceTests.cs` / ImportJobRecoveryServiceTests | 1 | Running → Failed i usunięcie dummy Parquet; cleanup już używa CancellationToken.None. |

## Nowe ustalenia

| ID | Priorytet | Pewność | Temat |
|---|---|---|---|
| AUD-036 | Medium | Confirmed | Cleanup plików testowych zależy od anulowania i od wykonania wcześniejszych asercji |
| AUD-039 | Medium | Confirmed | Test awarii wysyłki może zaliczyć się bez próby wysyłki |
| AUD-038 | Low | Confirmed | Obecność expires nie dowodzi usunięcia cookie |
| AUD-034 | Low | Confirmed dla powielenia | Powtarzany workflow auth i konstruktory żądań utrudniają zmianę testów |
| AUD-035 | Low | Confirmed dla powielenia | Trzy kopie przygotowania gotowego datasetu i cztery konstruktory multipart |
| AUD-037 | Low | Confirmed dla powielenia | Podstawowe seedy projektów powielono w wielu klasach |

### AUD-036 — niezależny od powodzenia testu lifecycle jego plików

- **Kategoria:** infrastruktura testowa / izolacja i niezawodność. **Priorytet:** Medium. **Pewność:** Confirmed dla mechanizmu; nie wykonano próby anulowania ani inwentaryzacji pozostałych plików.
- **Lokalizacje:** `CustomWebApplicationFactory.cs:39-64` i `ApiFixture.cs:64-81` pozostawiają produkcyjny storage oraz nie zarządzają jego katalogiem; `src/AnalyticDashboard.Infrastructure/Storage/LocalFileStorage.cs:9-16,88-103` wybiera `CWD/storage`, a DeleteAsync natychmiast sprawdza anulowanie. Cleanup: `Analytics/QueryDatasetEndpointTests.cs:191-213`, `Datasets/GetDatasetProfileEndpointTests.cs:238-260`, `Import/DuckDbCsvImporterTests.cs:108-118`, `Import/ImportJobProcessorTests.cs:275-291,441-457` — wszystkie te ścieżki przekazują token testu.
- **Mechanizm i scenariusz:** przerwanie testu anuluje TestContext token; finally wciąż jest wykonywane, ale DeleteAsync rzuca przed File.Delete. Pierwsze nieudane usunięcie przerywa także sprzątanie kolejnego klucza. Dodatkowo `Datasets/ImportCsvDatasetEndpointTests.cs:109-221` przypisuje sourceStorageKey dopiero po wielu asercjach: wcześniejsza nieudana asercja pozostawi null w finally. W profil/query wynik jest poznawany dopiero po `Assert.Equal(Ready, version.Status)` (`GetDatasetProfileEndpointTests.cs:120-138`, `QueryDatasetEndpointTests.cs:120-138`).
- **Konsekwencja:** pozostałości CSV/Parquet i katalogów między uruchomieniami, utrudnione odróżnienie danych testu od innych plików tej lokalizacji, dodatkowy wyjątek cleanup zasłaniający diagnostykę. GUID-y ograniczają kolizję nazw, ale nie usuwają plików. Nie twierdzi się, że test usunął dane użytkownika albo że bazy klas są współdzielone.
- **Kierunek poprawy / korzyść:** najpierw cleanup z własnym ograniczonym czasem/tokenem niezależnym od anulowania testu i próbą obsługi każdego klucza. Właścicielem plików powinien być konkretny test/scenario lub fixture z osobnym tymczasowym katalogiem; granicę zapewnić przez IFileStorage w DI. Katalog można usunąć w całości tylko po sprawdzeniu, że został utworzony dla tej instancji fixture. Nie zmieniać globalnego CurrentDirectory dla równoległych klas. Rejestrować własność pliku możliwie przed asercjami. Istniejący dobry przykład: `Import/ImportJobRecoveryServiceTests.cs:138-148` używa CancellationToken.None.
- **Granica wydzielenia:** jeden właściciel artefaktów dla import scenario; żadnej wspólnej klasy bazowej wymaganej przez testy bez plików. Cleanup testu nie ma naprawiać ani maskować produkcyjnego cleanup z AUD-001/006.
- **Weryfikacja przyszłej zmiany:** kontrolowana porażka po uploadzie, anulowanie przed finally, pomyślny przebieg i cleanup dwóch kluczy mimo błędu jednego; brak plików poza katalogiem fixture. Zachować oryginalny powód niepowodzenia.
- **Wysiłek:** Medium. **Ryzyko regresji:** Medium — zbyt szerokie usuwanie lub zamaskowanie braku cleanup aplikacji; asercje produkcyjnego stanu muszą nastąpić przed testowym disposal.

### AUD-039 — brak dowodu, że testowana awaria SMTP w ogóle nastąpiła

- **Kategoria:** jakość asercji / stan atrap. **Priorytet:** Medium. **Pewność:** Confirmed dla luki dowodowej; obecna implementacja wywołuje sender i nie stwierdzono obecnie losowego błędu.
- **Lokalizacje:** `Auth/ForgotPasswordEndpointTests.cs:185-203`, `Auth/ResendConfirmationEndpointTests.cs:353-371`; `Auth/TestEmailSender.cs:15-40`, singleton w `CustomWebApplicationFactory.cs:57-63`.
- **Aktualne zachowanie:** test ustawia FailNextSend, wywołuje endpoint i sprawdza tylko 204. Fake konsumuje zaplanowany wyjątek dopiero w SendAsync i zwraca faulted Task; nie zapisuje informacji o tej próbie.
- **Konkretny scenariusz regresji:** handler błędnie kończy się przed SendAsync. Odpowiedź nadal jest 204, zatem test awarii przechodzi, choć nie wykonano catch. `_nextException` pozostaje w singletonie i może dotknąć pierwszej wysyłki kolejnego testu tej samej klasy. xUnit domyślnie serializuje testy klasy, ale nie gwarantuje ich porządku; nie chodzi o obecny wyścig między klasami.
- **Kierunek poprawy / korzyść:** fake może zapamiętać próbę wysyłki i jej wynik lub udostępnić informację o skonsumowaniu zaplanowanej awarii. Test musi wymagać dokładnie próby dla swojego recipienta, błędu i niezmienionej ogólnej odpowiedzi HTTP. Awarię wiązać z adresem/scenario lub przywracać stan przy końcu testu; nie rozbudowywać fake do symulatora całego SMTP. Samo `Assert.DoesNotContain(Messages)` też nie rozstrzyga, czy próba nastąpiła.
- **Granica wspólnego helpera:** obserwacja SendAsync i kontrolowany błąd należą do TestEmailSender; oczekiwany status/recipient i znaczenie zachowania pozostają w testach endpointów. To inne zagadnienie niż brak logów aplikacji z AUD-017.
- **Weryfikacja:** tymczasowe pominięcie SendAsync musi spowodować błąd tych testów; po teście żaden zaplanowany fault nie pozostaje dla następnego przypadku.
- **Wysiłek:** Small. **Ryzyko regresji:** Low — nie zmienić publicznych odpowiedzi i nie traktować Cancelled jako dostarczonej wiadomości.

### AUD-038 — sprawdzenie expires może zaakceptować cookie, które nie zostało usunięte

- **Kategoria:** jakość asercji bezpieczeństwa. **Priorytet:** Low. **Pewność:** Confirmed dla zakresu asercji; nie oznacza to błędu obecnego Cookies.Delete.
- **Porównane lokalizacje:** `Auth/RegistrationStatusEndpointTests.cs:338-355,434-445`, `Auth/CompleteRegistrationEndpointTests.cs:368-385`, `Auth/GetCurrentUserEndpointTests.cs:509-525`. Sprawdzają nazwę i wystąpienie `expires=` lub `max-age=0`; ostatnia asercja registration status wymaga tylko expires. Wariantom sprawdzającym prefiks `registration_session=;` (`CompleteRegistrationEndpointTests.cs:476-481`) bliżej do potwierdzenia pustej wartości.
- **Mechanizm i scenariusz:** odpowiedź z tą samą nazwą, niepustą wartością i datą Expires w przyszłości przejdzie asercję wariantów opartych na samej obecności expires. Nawet gdy 401 prawidłowo dowodzi odmowy zasobu, nie dowodzi instrukcji usunięcia cookie w przeglądarce.
- **Kierunek poprawy / korzyść:** mała wspólna asercja parsuje konkretny Set-Cookie i wymaga pustej wartości oraz rzeczywiście wygaszonej daty/zerowego MaxAge, z odpowiednią ścieżką. Pozostawić w teście nazwę cookie i oczekiwany efekt; nie zastępować testów faktycznej utraty sesji samym parserem. Użyć dostępnego parsera nagłówków zamiast kolejnych regexów.
- **Granica:** helper ma weryfikować tylko kontrakt usunięcia cookie, nie wiedzieć, czy powodem był logout, usunięcie usera czy niepoprawny registration token.
- **Weryfikacja:** poprawny nagłówek usunięcia przechodzi; cookie z przyszłym Expires albo niepustą wartością nie przechodzi. Następnie istniejące testy real auth.
- **Wysiłek:** Small. **Ryzyko regresji:** Low — uwzględnić alternatywne poprawne formy nagłówka, a nie wymagać jego formatowania znak w znak.

### AUD-034 — powtarzane przygotowanie kont i parsowanie tokenów auth

- **Kategoria:** utrzymywalność testów / powielenie. **Priorytet:** Low. **Pewność:** Confirmed dla powielenia; wydzielenie to konkretna sugestia projektowa, bez przypisywania błędu biznesowego.
- **Rzeczywiste kopie konstruktorów żądań:** `Auth/LoginUserEndpointTests.cs:24-80`, `Auth/GetCurrentUserEndpointTests.cs:26-82`, `Auth/LogoutUserEndpointTests.cs:22-86`, `Auth/ResetPasswordEndpointTests.cs:26-118`. Register/confirm są także w `CompleteRegistrationEndpointTests.cs:26-66`, `ConfirmEmailEndpointTests.cs:26-66`, `RegistrationStatusEndpointTests.cs:37-77`, `ResendConfirmationEndpointTests.cs:23-59` i register w `ForgotPasswordEndpointTests.cs:21-39`.
- **Rzeczywiste kopie workflow:** register → odebranie wiadomości → regex href → Uri → QueryHelpers → userId/token → confirm w `LoginUserEndpointTests.cs:88-148,563-623,667-727`, `GetCurrentUserEndpointTests.cs:242-302`, `LogoutUserEndpointTests.cs:161-221`, `CompleteRegistrationEndpointTests.cs:92-158`. To wspólna operacja protokołu, a nie przypadkowo podobne trzy linie.
- **Aktualny koszt:** zmiana DTO/polityki linka lub wymagania potwierdzenia wymaga edycji wielu kopii. Test np. RememberMe poprzedza własną asercję kilkudziesięcioma liniami weryfikacji innego endpointu; łatwiej przeoczyć różnicę między Services a RealAuthServices.
- **Najmniejszy kierunek / korzyść:** kilka zwykłych helperów w katalogu testowego auth: tworzenie żądań register/confirm/login, odczyt tokena z konkretnej wiadomości i przygotowanie potwierdzonego konta. Helper workflow powinien przyjmować jawnie HttpClient oraz właściwy TestEmailSender i zwracać ID/dane przygotowanego konta. Parsowanie wiadomości może być wspólne dla confirmation/reset; cel wiadomości i recipient pozostają jawne. Dobrze działający lokalny precedens: `ResetPasswordEndpointTests.cs:120-188` (CreateValidPasswordResetAsync), używany w `:795-798,830-833`.
- **Co zostawić lokalnie:** Act i asercje register w testach samej rejestracji; badany login/RememberMe; operacje logout/reset i kontrolę browserA/browserB; żądania malformed/raw JSON; testy niepotwierdzonego konta. Helper nie powinien zawsze potwierdzać lub logować konta ani wybierać globalnego klienta. Nie zastępować całego Prepare przez bezpośrednie UserManager, jeżeli intencją jest przepływ HTTP.
- **Weryfikacja:** niezmienione efekty i asercje wszystkich testów wykorzystujących wydzielenie, prawdziwe cookies nadal są testowane rzeczywistym pipeline'em, błędny link nie jest akceptowany przez helper. Najpierw przenieść jedną rodzinę setupów, potem kolejnych konsumentów.
- **Wysiłek:** Medium. **Ryzyko regresji:** Medium — ukryte automatyczne potwierdzenie, pomylenie senderów fabryk lub współdzielenie cookies mogłoby zmienić znaczenie testów.

### AUD-035 — wspólne przygotowanie datasetu Ready ma trzech bezpośrednich konsumentów

- **Kategoria:** utrzymywalność testów / powielenie przepływu. **Priorytet:** Low. **Pewność:** Confirmed dla powielenia; rekomendacja projektowa.
- **Porównane przygotowania:** `Analytics/QueryDatasetEndpointTests.cs:48-140`, `Datasets/GetDatasetProfileEndpointTests.cs:48-140`, `Widgets/GetWidgetDataEndpointTests.cs:180-276`. Każde robi upload, odczyt source key, nowy scope processora, ProcessNextAsync i odczyt Ready wersji. Część parsuje typowane ImportCsvDatasetResponse, część JsonDocument.
- **Porównane multipart:** `Datasets/ImportCsvDatasetEndpointTests.cs:26-63`, `Analytics/QueryDatasetEndpointTests.cs:217-254`, `Datasets/GetDatasetProfileEndpointTests.cs:264-300`, `Widgets/GetWidgetDataEndpointTests.cs:67-98` — ten sam endpoint, pole file, UTF-8 oraz text/csv.
- **Mechanizm kosztu:** każdy nowy przypadek agregacji/profilu wymaga utrzymania długiego technicznego Arrange. Te kopie różnią się przypadkowo sposobem deserializacji i cleanup; część problemu jest uchwytna w AUD-036. Nie zaleca się zastępowania rzeczywistego importera atrapą we wszystkich tych testach.
- **Kierunek poprawy / korzyść:** testowy helper tworzenia multipart oraz jawna operacja PrepareImportedDatasetAsync przyjmująca client, services, projectId, ownerId, filename i CSV. Wynik: dataset/version/job ID i właściciel artefaktów do cleanup; informacja o ręcznym ProcessNext musi pozostać czytelna. Pozwala dopisywać przypadki null, różnych grup i agregacji bez kopiowania dziewięćdziesięciu linii setupu. Nie twierdzi się, że samo wydzielenie zwiększy pokrycie.
- **Co pozostawić lokalnie:** treść CSV i oczekiwane wyniki, query/profile/data Act, konfigurację widgetu. W teście samego importu Pending oraz testach processora/recovery etap będący Act nie może zostać ukryty w PrepareImportedDatasetAsync. Istotne asercje Pending graph z `ImportCsvDatasetEndpointTests.cs:148-232` pozostają tam. Zachować osobne scope zapisu i weryfikacji.
- **Weryfikacja:** wszystkie trzy przepływy nadal sprawdzają realne wyniki DuckDB; helper przerywa setup, gdy import nie Ready; cleanup także po błędzie. Rozbudowa parametrów tylko wtedy, gdy drugi konsument potrzebuje danej różnicy.
- **Wysiłek:** Medium, łączyć z AUD-036. **Ryzyko regresji:** Medium — helper mógłby przypadkowo przetworzyć inny Pending job lub ukryć fragment badanego zachowania; izolacja kolejki musi zostać zachowana.

### AUD-037 — podstawowe seedy projektów można scalić bez generycznego frameworka

- **Kategoria:** utrzymywalność testów / niewielkie powielenie. **Priorytet:** Low. **Pewność:** Confirmed dla kopii; sugestia o mniejszej korzyści niż AUD-034/046.
- **Porównane konkretne kopie:** `Projects/CreateProjectEndpointTests.cs:50-60`, `Projects/DeleteProjectEndpointTests.cs:40-50`, `Projects/GetProjectByIdEndpointTests.cs:41-51` — nowy scope, AppDbContext, Add(project), SaveChanges. Wariant AddRange: `Projects/GetProjectsEndpointTests.cs:94-104`, `Projects/RenameProjectEndpointTests.cs:87-97`. To samo w `Analytics/QueryDatasetEndpointTests.cs:282-298`, `Datasets/GetDatasetProfileEndpointTests.cs:320-336`, `Datasets/ImportCsvDatasetEndpointTests.cs:65-79`, `Widgets/GetWidgetDataEndpointTests.cs:100-113`.
- **Kierunek / konkretna korzyść:** pojedynczy SeedProjectsAsync dla przekazanych encji, w test support lub ApiFixture, zastępuje wiele lokalnych metod o identycznej semantyce. Nazwy, OwnerId i relacje nadal tworzy test; helper tylko zapisuje. Nie potrzeba interfejsu, dziedziczenia ani rejestracji nowego serwisu.
- **Granice:** nie scalać z seedingiem Ready wersji, ImportJob state ani całego dashboardu w jedną metodę z flagami. `GetDatasetByIdEndpointTests.cs:43-78` ma istotną kolejność zapisu i PublishVersion; `UpdateDashboardWidgetsEndpointTests.cs:134-188` opisuje specyficzny graph. Zachować te czytelne lokalne operacje. Nie owijać wszystkich odczytów DB w wspólny delegate tylko po to, by ukryć trzy linie scope.
- **Dlaczego nie jest to pilny defekt:** kopie są dziś proste i zgodne, brak dowodu rozjechania zachowania. Wydzielać przy zmianach tych klas, po helperach dających większy zysk; pozostawienie ich nie blokuje bezpiecznego refaktoru backendu.
- **Weryfikacja:** istniejące testy i ich stan; nie pisać testu odtwarzającego wewnętrzne AddRange helpera.
- **Wysiłek:** Small. **Ryzyko regresji:** Low — nie zwracać śledzonego DbContext poza jego lifetime i nie podmieniać ownerów/domniemanych wartości.

## Pozostałe małe ulepszenia — bez nowych ID

1. **Wspólna asercja koperty błędu, jeśli będzie realnie używana.** `Projects/CreateProjectEndpointTests.cs:146-179` i `Projects/RenameProjectEndpointTests.cs:236-269` powtarzają status, content type, deserializację, Status i pojedynczy błąd pola. `Auth/LoginUserEndpointTests.cs:257-282,314-339` powtarza ProblemDetails. Można wydzielić odczyt/sprawdzenie koperty, pozostawiając oczekiwany tytuł, detail i klucz lokalnie. Nie scalać HttpValidationProblemDetails i wszystkich błędów auth w wielką metodę z opcjonalnymi parametrami. Pojedyncze Assert.Equal(StatusCode) pozostają zrozumiałe i nie wymagają wrappera.
2. **Dispose odpowiedzi konkurujących requestów.** `Projects/CreateProjectEndpointTests.cs:426-459` i `Auth/RegisterUserEndpointTests.cs:556-588` nie zamykają odpowiedzi z Task.WhenAll; `Projects/RenameProjectEndpointTests.cs:469-501` podobnie, a `:546-606` zamyka tylko patchAResponse. Wzór już istnieje w `Projects/DeleteProjectEndpointTests.cs:218-224` i `Auth/ConfirmEmailEndpointTests.cs:451-457`. Ujednolicić lokalnymi using, bez wspólnego schedulera testów. Nie stwierdzono wyczerpania zasobów TestServer.
3. **Nazwy i lokalna czytelność.** `Auth/GetCurrentUserEndpointTests.cs:14,21` deklaruje CurrentUserEndpointTests; wystarczy zgodność pliku/klasy podczas przyszłego porządkowania. Nazwa `Login_ShouldReturnUnauthorized_WhenEmailOrPasswordIsBlank` (`LoginUserEndpointTests.cs:503-509`) opisuje dwa warunki, ale testuje tylko blank e-mail: zmienić nazwę albo dodać wiersze teorii dla password/null. Nie zgłaszać braku wszystkich możliwych nulli jako nowej architektury walidacji.
4. **Jawne warianty zamiast przypadkowej kolejności enum.** `Widgets/UpdateDashboardWidgetsEndpointTests.cs:88,92,141-142,243-244` i `Widgets/GetWidgetDataEndpointTests.cs:301` wybierają pierwszy/ostatni element enum. Obecnie wartości są poprawne, ale dodanie członka może zmienić sens danych testowych. Dla fixture użyć konkretnej wartości; dla pokrycia wszystkich wartości — jawnej teorii. Bez zarzutu błędu obecnych wyników.
5. **Duża metoda ma kilka odpowiedzialności.** `Widgets/GetWidgetDataEndpointTests.cs:162-442` sprawdza import, utworzenie dashboardu, zapis/odczyt layoutu i agregację. Po wydzieleniu wyłącznie Arrange można pozostawić jeden test przepływu, a asercje geometrii oprzeć głównie na istniejącej rodzinie UpdateDashboardWidgets. Powtórne identyczne `Assert.Equal(OK, createWidgetResponse.StatusCode)` w `:329-334` można po prostu usunąć przy porządkowaniu. Nie jest potrzebny osobny finding.

## Izolacja, kolejność i lifetime — czego nie potwierdzono

- **Nie ma jednej wspólnej bazy wszystkich klas.** Klasy deklarują IClassFixture<ApiFixture>; każdy ApiFixture tworzy własny PostgresFixture (`ApiFixture.cs:30-37`). Nie znaleziono CollectionFixture ani wspólnej statycznej fabryki. Singleton TestEmailSender jest singletonem konkretnej fabryki, nie globalnym singletonem całej sesji testowej. Wspólne stałe User1Id/User2Id nie tworzą same z siebie konfliktu między tymi bazami.
- **Dane wewnątrz klasy są współdzielone świadomie.** Większość testów filtruje po nowym ownerId/e-mailu/ID. Przykładowo `GetProjectsEndpointTests.cs:107-175` używa stałego User1 tylko w swoim scenariuszu, inne testy listowania tworzą nowe ownerId. Nie wykazano, że obecna kolejność testów zmienia TotalCount. Nie proponuje się masowego resetu bazy po każdym teście bez potrzeby.
- **Brak potwierdzonego obecnego wyścigu TestEmailSender.** ConcurrentQueue i Interlocked chronią struktury; domyślna serializacja testów klasy ogranicza współbieżność. AUD-039 dotyczy luki dowodowej i pozostawionego faultu przy regresji, a nie twierdzenia, że dwa różne testy dziś walczą o FailNextSend.
- **Klienci real auth są lokalni.** `ApiFixture.cs:51-61` tworzy klienta z własną obsługą cookies; testy używają using. `ResetPasswordEndpointTests.cs:978-979` jawnie tworzy dwa browsery — zachować tę granicę, nie ukrywać we wspólnym kliencie helpera. TestAuthHandler pozostaje osobną ścieżką.
- **Koszt fixture wymaga pomiaru.** Per-class kontenery i migracje mogą podnieść koszt uruchomienia równoległych klas, ale brak pomiaru czasu/pamięci nie pozwala ogłosić problemu wydajności testów. `Import/DuckDbCsvImporterTests.cs:32-39,54-64` nie używa DB i mógłby otrzymać lżejsze lokalne przygotowanie samego importer/storage. Nie przenosić wszystkich klas na jeden współdzielony kontener tylko dla potencjalnej oszczędności, bo pogorszy to prostotę izolacji.
- **Nie znaleziono sleepów sterujących kolejnością.** Test sortowania jawnie ustawia timestamps (`GetProjectsEndpointTests.cs:67-92,297-301`); jest to dobre. Task.WhenAll nie dowodzi konkretnego interleaving — AUD-019 już to opisuje. Nie mnożono tego samego problemu pod nowym ID.

## Granice sensownego współdzielenia

| Element | Współdzielić | Pozostawić przy teście |
|---|---|---|
| Auth | budowa powtarzanego żądania, parsowanie konkretnego linka, jawny krok przygotowania konta | Act, expected outcome, confirmed/unconfirmed, prawdziwy browser, wybór sendera/fabryki |
| Import | konstruktor multipart, PrepareImportedDatasetAsync dla testów odczytów, ownership artefaktów | CSV, oczekiwane wartości, badany processor/import Pending/recovery |
| Seeding | zapis przekazanych projektów | owner, nazwy, FK, stan wersji/jobów i graph widgetów |
| Asercje | protokół usunięcia cookie, ewentualnie koperta HTTP ProblemDetails | teksty i znaczenie kontraktu, konkretne expected DTO/pola |
| Snapshoty widgetów | zachować istniejące lokalne helpery; uogólnić dopiero dla drugiego realnego konsumenta | zakres pól stanowiących invariant i asercję revision |
| Identity nagłówka | używać już istniejącej stałej UserIdHeader | URL endpointu, metoda i cel żądania; pięć podobnych linii nie wymaga nowej warstwy |

`Widgets/UpdateDashboardWidgetsEndpointTests.cs:190-234` (AsNoTracking + snapshot + AssertUnchanged) oraz `Projects/ProjectRepositoryTests.cs:64-107` (stan trackera i dalszy realny zapis) są dobrymi wzorcami weryfikacji zachowania. `UnitTests/Projects/ProjectTests.cs:179-240` sprawdza zachowanie stanu po wyjątku. Takie asercje są ważniejsze od samego zmniejszenia liczby linii.

## Proponowana kolejność dalszej pracy

1. **AUD-036 i 050:** poprawić wiarygodność infrastruktury plików i obserwowalność próby wysyłki. Nie mieszać z naprawą produkcyjnych ścieżek. Kryterium: wymuszona porażka/anulowanie nie pozostawia plików ani zaplanowanego faultu, a test awarii nie przechodzi po pominięciu sendera.
2. **AUD-038:** wspólna poprawna asercja usunięcia cookie, zachować sprawdzanie realnej sesji.
3. **AUD-034:** najpierw odczyt linka i powtarzane żądania, następnie wyłącznie powtarzany Arrange potwierdzonego konta. Kryterium: Act jest widoczne, helper nie zmienia wybranej tożsamości ani rodzaju sesji.
4. **AUD-035:** jeden scenariusz importu dla profile/query/widget-data, łącznie z ownership artefaktów. Kryterium: CSV i wyniki pozostają lokalne; zachowane realne DuckDB i rozdzielenie scope; nie ukryto Act processora/recovery.
5. **AUD-037 i drobne uwagi:** przy zmianach klas, bez osobnego dużego projektu porządkowego. Nie dodawać testów, które wyłącznie odtwarzają wnętrze prostego helpera.

Po dopuszczonej w osobnej pracy zmianie testów trzeba wykonać świeży build i odpowiednie testy. Sam zestaw 19 przypadków domeny nie zweryfikuje wydzielenia auth/import HTTP. Zakaz migracji obowiązujący podczas audytu pozostaje przyczyną braku uruchomienia integracji tutaj.
