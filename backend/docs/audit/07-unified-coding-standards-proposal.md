# Aperture — propozycja wspólnych standardów backendu

Data: 2026-11-06. **Propozycja do dyskusji i stopniowego przyjęcia, nie obowiązująca specyfikacja ani wykonany refaktor.**

Dokument syntetyzuje drugi audyt: [spójność feature'ów](04-cross-feature-consistency.md), [persistence i wydajność](05-persistence-performance.md), [jakość testów](06-test-quality.md). Ustalenia AUD-001–024 i ich pilność pozostają w [pierwszym raporcie](02-findings.md); nie nadano im nowych numerów. Standardy oznaczone S-01 itd. nie są dodatkowymi błędami ani nowymi ID ustaleń.

## Wniosek

Projekt potrzebuje przede wszystkim przewidywalnych kontraktów i jawnych granic zapisu. Nie wymaga narzucenia jednego schematu klas wszystkim operacjom. Trzy istniejące rozwiązania nadają się na punkt odniesienia:

- Project — ograniczenia domenowe i bazy oraz rozpoznawanie konkretnego konfliktu unikalności.
- Synchronizacja widgetów — walidacja przed mutacją, wspólny scoped kontekst, jeden commit i token Revision.
- Testy widgetów — lokalny scenariusz seed, odczyt świeżego stanu i porównywanie całej konfiguracji po odrzuceniu.

To propozycja wspólnego sposobu podejmowania decyzji, a nie żądanie zastąpienia prawidłowych wyjątków wspólną abstrakcją. Nowe poprawki jakościowe nie zastępują pilnych napraw utraty plików, blokad auth i limitu widgetów z pierwszego audytu.

## S-01 — nazwy opisują rolę i skutki

**Propozycja:** `Command` dla operacji zmieniającej stan, `Query` dla odczytu, `Handler.HandleAsync` dla wykonania przypadku użycia; `Request/Response` dla transportu HTTP, `Result` dla wyniku aplikacyjnego z alternatywami, `Outcome` dla wewnętrznego wyniku operacji persistence. Zmienna `request` oznacza HTTP, `command/query` wejście handlera, `result` jego wynik, `outcome` wynik repozytorium. Nazwy `projectId`, `datasetId`, `dashboardId` są czytelniejsze od niekontekstowego `id` w metodach operujących na wielu Guid.

**Podstawa:** `src/AnalyticDashboard.Application/Projects/CreateProject/CreateProjectHandler.cs:16-56` i `src/AnalyticDashboard.Api/Endpoints/ProjectEndpoints.cs:25-74` już rozdzielają te role. Dla porównania `src/AnalyticDashboard.Application/Dashboards/GetDashboardById/GetDashboardByIdResponse.cs:3-9` jest bezpośrednim wynikiem handlera używanym w HTTP, a `src/AnalyticDashboard.Application/Projects/GetProjectById/GetProjectByIdResult.cs:3-11` modelem alternatyw wyniku.

**Granica:** nie tworzyć osobnych identycznych recordów i list tylko dla sufiksów; wspólny niemutowalny payload jest dopuszczalny, jeśli nie wiąże Application z HTTP. Nie wykonywać masowego rename istniejących publicznych typów bez korzyści dla edytowanego przepływu. Zmiana nazwy CLR może zmienić schema OpenAPI; sprawdzić realnych konsumentów. Rozbieżność nazw sama nie jest błędem poprawności.

## S-02 — nazwa repozytorium nie ukrywa granicy commit

**Propozycja:** wyraźnie odróżniać odczyt, stage i operację zapisującą. `StageAdd/StageRemove` nie wykonują I/O ani commit. Dla metod wykonujących commit kontrakt ma to mówić wprost; nie mieszać stage i automatycznego SaveChanges w jednym komponowanym przepływie.

**Podstawa:** `src/AnalyticDashboard.Infrastructure/Repositories/WidgetRepository.cs:17-20,84-87` stage'uje zmiany; `src/AnalyticDashboard.Infrastructure/Repositories/ProjectRepository.cs:18-40` zapisuje podczas AddAsync; `src/AnalyticDashboard.Infrastructure/Repositories/PendingDatasetImportRepository.cs:17-29` atomowo zapisuje cały zestaw metadanych importu.

**Korzyść:** czytając handler, można wskazać moment trwałego zapisu i ocenić, czy późniejszy błąd pozostawi częściowy stan. Nie zaleca się zmiany każdej nazwy na długi opis techniczny — wystarczy spójny port i jawna odpowiedzialność. Usunąć niewywoływane alternatywy z AUD-020 zamiast standaryzować martwą ścieżkę.

## S-03 — IUnitOfWork stosować do wspólnego zapisu śledzonych zmian

**Proponowany wybór zależnie od semantyki:**

| Rodzaj operacji | Proponowana konwencja | Istniejący dowód / wyjątek |
|---|---|---|
| Kilka śledzonych zmian stanowiących jeden zapis biznesowy | handler/processor wyznacza jeden commit przez IUnitOfWork; repozytoria stage'ują | `src/AnalyticDashboard.Application/Widgets/UpdateDashboardWidgets/UpdateDashboardWidgetsHandler.cs:158-199` |
| Samodzielna, kompletna operacja repozytorium zapisująca spójny zestaw | dopuszczalny commit wewnętrzny, jeśli nie jest komponowany z oczekującymi zmianami i kontrakt to ujawnia | `src/AnalyticDashboard.Infrastructure/Repositories/PendingDatasetImportRepository.cs:17-29` |
| Pojedyncze ExecuteUpdate/Delete | SQL jest operacją zapisu; nie dodawać pustego SaveChanges dla symetrii | `src/AnalyticDashboard.Infrastructure/Repositories/ProjectRepository.cs:58-105` |
| Identity UserManager / SignInManager | zachować zarządzanie persistence przez Identity; dodatkowy UoW nie oznacza objęcia wcześniejszych zapisów transakcją | `src/AnalyticDashboard.Infrastructure/Identity/UserAccountService.cs:39-44,155-158` |
| Import z widocznym stanem Running i późniejszą publikacją | osobne, uzasadnione fazy commit; jeden finalny commit Ready + CurrentVersionId + Completed | `src/AnalyticDashboard.Infrastructure/Services/Import/ImportJobProcessor.cs:46-50,88-104` |

`IUnitOfWork` jest obecnie implementowany **jawnie** przez AppDbContext: `src/AnalyticDashboard.Infrastructure/Data/AppDbContext.cs:45-54`. To tylko ta ścieżka tłumaczy DbUpdateConcurrencyException. Zwykłe `_dbContext.SaveChangesAsync` w repozytorium jej nie wywołuje. Rejestracja w `src/AnalyticDashboard.Infrastructure/DependencyInjection.cs:37-43` wskazuje ten sam scoped AppDbContext — to warunek wspólnej atomowości widgetów.

Przy porządkowaniu importu warto przenieść odpowiedzialność za flush całego kontekstu z IImportJobRepository do jawnej granicy UoW. **Obecny finalny zapis importu jest atomowy**; problemem jest ukryta odpowiedzialność, nie brak transakcji. Nie dodawać drugiego kontekstu ani automatycznej transakcji dla każdego handlera. Wymiana wyjątku na inny typ nie naprawia semantyki konfliktu jobów bez tokena/przejęcia.

**Weryfikacja:** testy mają potwierdzić ten sam zakres commit i rollback. Zmiana konwencji nie może dodać wcześniejszego SaveChanges ani usunąć uzasadnionego Running.

## S-04 — handler ma czytelne etapy, bez obowiązkowego szablonu klas

**Propozycja:** lokalna walidacja wejścia → odczyt w zakresie uprawnień → walidacja zależności/stanu → przygotowanie zmian → commit → jawny wynik. Kolejność walidacji i autoryzowanego odczytu może się różnić, jeśli chroni informacje o cudzym zasobie lub ogranicza koszt; wynik ma być celowy.

**Podstawa:** `src/AnalyticDashboard.Application/Widgets/UpdateDashboardWidgets/UpdateDashboardWidgetsHandler.cs:35-60,85-137,158-221`; `src/AnalyticDashboard.Application/Projects/CreateProject/CreateProjectHandler.cs:20-56`. Guardy przed mutacją chronią stan; `src/AnalyticDashboard.Domain/Entities/Widget.cs:178-193` waliduje całość konfiguracji przed przypisaniami.

**Granica:** prosty Delete delegujący do jednego warunkowego SQL nie potrzebuje wszystkich etapów ani dodatkowego odczytu. Nie rozbijać metody na kolejne handlery/pipeline tylko dla podobnej liczby kroków. Testować brak mutacji po odrzuceniu, nie liczbę metod pomocniczych.

## S-05 — wybierać kształt odczytu według rzeczywiście potrzebnych danych

**Propozycja:**

- Samo istnienie/uprawnienie → AnyAsync z pełnym predykatem owner/project.
- Pojedyncze pole/metadane → Select przed materializacją, nullable scalar jeśli trzeba rozróżnić brak wiersza od wartości 0.
- Odczyt do odpowiedzi → wąska projekcja, gdy oszczędza rzeczywiście niewykorzystywane dane.
- Zmiana zachowania encji → tracked entity; nie zastępować jej projekcją tylko dla mniejszej liczby kolumn.

**Podstawa:** `src/AnalyticDashboard.Infrastructure/Repositories/DashboardRepository.cs:98-112` czyta revision jako `long?`; `src/AnalyticDashboard.Infrastructure/Repositories/DatasetRepository.cs:134-150` pobiera zbiorczo tylko ID. Dla porównania `src/AnalyticDashboard.Application/Dashboards/CreateDashboard/CreateDashboardHandler.cs:24-38` potrzebuje wyłącznie istnienia projektu, a `src/AnalyticDashboard.Application/Projects/RenameProject/RenameProjectHandler.cs:20-56` korzysta też z danych projektu i domenowej walidacji.

**Korzyść:** mniej przesyłanych kolumn i obiektów. Any zamiast encji nadal zwykle oznacza jeden round-trip; nie przedstawiać tej zmiany jako usunięcia zapytania. Nie dodawać osobnego interfejsu i klasy dla każdej projekcji. Zwykła metoda istniejącego portu często wystarczy.

## S-06 — tracking i liczba zapytań są decyzją przepływu

**Propozycja:** AsNoTracking dla niemodyfikowanych encji; scalar/DTO bez encji nie wymaga mechanicznie AsNoTracking. Śledzone obiekty pozostają we wspólnym scope do commit. `ForUpdate` w obecnym nazewnictwie oznacza odczyt do późniejszej zmiany, **nie SELECT FOR UPDATE ani blokadę**.

**Podstawa:** porównanie `src/AnalyticDashboard.Infrastructure/Repositories/DashboardRepository.cs:46-77` i `src/AnalyticDashboard.Infrastructure/Repositories/WidgetRepository.cs:22-60`. Zbiorczy odczyt wersji w `src/AnalyticDashboard.Application/Datasets/GetDatasets/GetDatasetsHandler.cs:47-58` jest dobrym wzorcem bez N+1. Odczyty danych joba/version/dataset w `src/AnalyticDashboard.Infrastructure/Repositories/ImportJobRepository.cs:21-46` muszą zachować tracking, jeśli zostaną połączone.

**Granica:** nie ustawiać globalnie NoTracking bez przeglądu wszystkich writerów. Nie uruchamiać Task.WhenAll zapytań na jednym DbContext. Nie traktować każdego powtórnego FindById w Identity jako dowiedzionego drugiego SQL — należy uwzględnić identity map. Przy zmianie read path policzyć komendy w tym samym scenariuszu i sprawdzić wygenerowany SQL, nie wnioskować z liczby metod.

## S-07 — ExecuteUpdate/Delete nie omija warunków biznesowych

**Propozycja:** używać set-based SQL do pojedynczej celowej zmiany, gdy zachowuje walidację, zakres dostępu, liczbę zmienionych wierszy i wymagania concurrency. Dla wersjonowanego zasobu predykat revision i jego podniesienie musiałyby być jawne. Nie mieszać ze śledzonymi kopiami tych samych encji bez rozstrzygnięcia ich aktualności.

**Podstawa:** `src/AnalyticDashboard.Infrastructure/Repositories/ProjectRepository.cs:58-105` realizuje samodzielne Rename/Delete, a handler `src/AnalyticDashboard.Application/Projects/RenameProject/RenameProjectHandler.cs:31-47` wcześniej stosuje walidację domenową. W przeciwieństwie do tego zapis widgetów łączy kilka zmian z Revision (`src/AnalyticDashboard.Application/Widgets/UpdateDashboardWidgets/UpdateDashboardWidgetsHandler.cs:158-218`), więc mechaniczna zamiana jego fragmentów na ExecuteDelete pogorszyłaby granicę atomowości.

**Weryfikacja:** porównać stan po odrzuceniu, usunięcie zależności w trakcie, wpływ na revision i tracker. Dodatkowe SaveChanges po bezpośrednim SQL nie przywraca tych gwarancji.

## S-08 — walidacja ma jedno źródło reguły i użyteczne błędy

**Propozycja:** reguły nazwy/layoutu/konfiguracji należą do encji/value object lub małej współdzielonej funkcji domenowej używanej przez konstruktory i mutatory. API sprawdza strukturę transportu, handler tłumaczy znane odrzucenie na wynik. Limity stringów mają odpowiadać schema; Unicode liczyć zgodnie z przyjętym znaczeniem znaku.

**Podstawa:** `src/AnalyticDashboard.Domain/Entities/Project.cs:15-54` używa wspólnej normalizacji w Rename i konstruktorze; `src/AnalyticDashboard.Domain/Entities/Widget.cs:90-123,126-168` centralizuje konfigurację. `src/AnalyticDashboard.Api/Endpoints/ProjectEndpoints.cs:65-70,236-241` przekazuje komunikaty jako błędy pola, podczas gdy `src/AnalyticDashboard.Api/Endpoints/WidgetEndpoints.cs:291-296` odrzuca obliczone przyczyny konfiguracji na rzecz ogólnego tekstu.

**Proponowany kontrakt:** błędy pól → ValidationProblem z ustalonymi kluczami; błędy operacji → ProblemDetails z bezpiecznym kodem/typem i potrzebnymi danymi kontekstowymi. Dla listy widgetów zachować powiązanie błędu z WidgetId/polem, ale ograniczyć liczbę diagnostyk. Nie ujawniać cudzych ID, ścieżek storage, SQL ani surowych wyjątków. Auth może celowo zwracać mniej szczegółów, aby nie zdradzać istnienia konta.

**Ryzyko:** to zmiana kontraktu HTTP, nie wyłącznie refaktor C#. Uwzględnić klienta i testy; nie wymagać wielowarstwowego systemu walidatorów.

## S-09 — rozróżniać brak zasobu, brak gotowości i pustą kolekcję

**Propozycja:** ustalić wspólną politykę nested collections i zasobu Pending/Failed. Pusta kolekcja w istniejącym parent to inny stan od nieistniejącego/niedostępnego parent. Metadane datasetu mogą być poprawnie dostępne przed ukończeniem importu; rows/query nie muszą wtedy zwracać sukcesu. Stan „jeszcze niegotowy” powinien jednak mieć celowo przyjęte mapowanie i stabilny sygnał dla klienta, a nie zależeć od wybranego endpointu.

**Podstawa:** `src/AnalyticDashboard.Application/Datasets/GetDatasets/GetDatasetsHandler.cs:28-45` rozpoznaje brak parent, podczas gdy `src/AnalyticDashboard.Application/Dashboards/GetDashboards/GetDashboardsHandler.cs:18-30` i `src/AnalyticDashboard.Application/Widgets/GetWidgets/GetWidgetsHandler.cs:18-41` zwracają samą listę. `src/AnalyticDashboard.Application/Datasets/GetDatasetProfile/GetDatasetProfileHandler.cs:39-56` i `src/AnalyticDashboard.Application/Analytics/QueryDataset/DatasetQueryExecutor.cs:53-76` inaczej reprezentują brak Ready wersji.

**Granica:** nie zaleca się jednego statusu HTTP dla wszystkich operacji i wszystkich stanów. Nie dodawać odczytu parent tylko dla symetrii bez uwzględnienia kosztu; można użyć projekcji/sygnału istnienia w celowym read path. Nie mylić wygasłej sesji z brakiem zasobu.

## S-10 — obsługiwać tylko rozpoznane konflikty, pozostawiając niespodziewane błędy widoczne

**Propozycja:** mapować znany constraint i concurrency na wynik aplikacji. Nie tłumaczyć wszystkich DbUpdateException na 404 ani wszystkich wyjątków domenowych/argumentów na 400. Po konkurencyjnym usunięciu możliwy jest celowy brak zasobu; po obcej wersji — konflikt.

**Podstawa:** `src/AnalyticDashboard.Infrastructure/Repositories/ProjectRepository.cs:32-40,82-88` filtruje konkretny indeks i rodzaj błędu; `src/AnalyticDashboard.Infrastructure/Identity/UserAccountService.cs:46-61` podobnie obsługuje unique violations. `src/AnalyticDashboard.Application/Widgets/UpdateDashboardWidgets/UpdateDashboardWidgetsHandler.cs:201-218` rozróżnia świeżą revision i brak dashboardu.

**Weryfikacja:** nie tylko status HTTP — sprawdzić atomowy stan po przegranej oraz brak przypadkowego ujawnienia danych. Dla pozostałych znanych luk PK/FK zachować ID AUD-015/016; nie tworzyć nowego numeru dla innej nazwy tej samej usterki.

## S-11 — transakcja odpowiada granicy biznesowej, a nie całemu requestowi

**Propozycja:** jeden SaveChanges do jednego kompletnego zestawu SQL; jawna dodatkowa transakcja tylko przy rzeczywistej potrzebie łączenia kilku komend. Dłuższy import nie powinien trzymać transakcji PostgreSQL podczas pracy nad plikiem. Commit SQL nie obejmuje filesystemu i nie naprawia cleanup.

**Podstawa:** atomowe tworzenie metadanych `src/AnalyticDashboard.Infrastructure/Repositories/PendingDatasetImportRepository.cs:23-29` i atomowa publikacja `src/AnalyticDashboard.Infrastructure/Services/Import/ImportJobProcessor.cs:88-104`. Kontrprzykłady ryzyka znajdują się już w AUD-001/006/008, a brak atomowego przejęcia joba w AUD-004.

**Weryfikacja:** stan po błędzie przed i po commit, anulowanie, restart oraz wynik nieznanego commit. Nie zastępować testów tych przypadków samą obecnością BeginTransaction. Osobno rozstrzygnąć spójność odczytu revision + widgets (AUD-022).

## S-12 — koszty danych mierzyć tam, gdzie faktycznie powstają

**Propozycja:** optymalizacje mają opisywać bieżący read path, alternatywę, usuwany koszt i ryzyko. Przy round-tripach odróżniać liczbę komend, etapy SaveChanges i odczyty pliku. Indeks proponować na podstawie filtra/sortowania i rozmiaru/planów, nie samego wystąpienia Where.

**Podstawa:** indeks paginacji i query `src/AnalyticDashboard.Infrastructure/Repositories/ProjectRepository.cs:117-130` wraz z `src/AnalyticDashboard.Infrastructure/Data/Configurations/ProjectConfiguration.cs:63-77` stanowią istniejący dobry punkt odniesienia. Znane koszty DuckDB pozostają AUD-009–011: pełna kopia `src/AnalyticDashboard.Infrastructure/Services/DuckDb/DuckDbParquetExecutor.cs:30-66` i osobne komendy profilujące `src/AnalyticDashboard.Infrastructure/Services/Profiling/DuckDbDatasetProfileReader.cs:65-85`.

**Granica:** nie podawać niezmierzonego przyspieszenia procentowego. Połączenie trzech SELECT w jeden to określona redukcja round-tripów, lecz wynikowa latencja, alokacje EF i plan wymagają pomiaru. Ograniczenie liczby grup musi mieć uzgodnioną semantykę; nie wprowadzać cichego obcinania danych. Zachować CancellationToken w EF i I/O, ale skuteczność przerwania pracy natywnej zmierzyć.

## S-13 — helper testowy usuwa mechanikę, scenariusz pozostaje widoczny

**Propozycja:** niewielkie współdzielone elementy dla konstrukcji requestów, przygotowania istniejącego konta i zestawu importu. Nazwane parametry wskazują owner, stan potwierdzenia i wersji; wynik helpera udostępnia potrzebne ID/artefakty. Endpoint będący przedmiotem testu jest wywoływany i oceniany jawnie w teście.

**Podstawa:** `tests/AnalyticDashboard.IntegrationTests/Widgets/UpdateDashboardWidgetsEndpointTests.cs:23-46,97-115,134-187` ma czytelny lokalny scenariusz z nazwanymi ID. Powtarzające się fabryki requestów są w `tests/AnalyticDashboard.IntegrationTests/Auth/LoginUserEndpointTests.cs:24-80` i `tests/AnalyticDashboard.IntegrationTests/Auth/ResetPasswordEndpointTests.cs:26-117`; powtarzany import w `tests/AnalyticDashboard.IntegrationTests/Analytics/QueryDatasetEndpointTests.cs:217-254` i `tests/AnalyticDashboard.IntegrationTests/Widgets/GetWidgetDataEndpointTests.cs:67-98`.

**Granica:** nie dodawać bazowej klasy dla wszystkich endpointów ani uniwersalnego buildera dowolnego grafu encji. Zwykłe `new Project(ownerId, name)` często jest czytelniejsze od buildera. Surowy JSON, błędne/missing/null pola, ręczne cookies i dwa konkurencyjne scope muszą pozostać dostępne, bez helpera automatycznie naprawiającego testowany błąd. Nie używać badanego endpointu w ukrytym setupie do sprawdzania go potem ponownie.

## S-14 — fixture określa granicę izolacji i własność zasobów

**Propozycja:** zachować osobne zastosowanie test auth i real cookies. Wspólny transport helper nie ustawia globalnego użytkownika na współdzielonym HttpClient. Zasoby plikowe i klienci mają jawnego właściciela oraz cleanup w finally/Dispose także po błędzie setupu.

**Podstawa:** `tests/AnalyticDashboard.IntegrationTests/ApiFixture.cs:30-61,64-97` rozdziela fabryki i real auth client; `tests/AnalyticDashboard.IntegrationTests/CustomWebApplicationFactory.cs:25-63` centralizuje Testing, connection string, wyłączenie workera i testowy sender; `tests/AnalyticDashboard.IntegrationTests/TestAuthHandler.cs:29-59` odczytuje użytkownika z pojedynczego requestu.

**Granica:** obecne IClassFixture izoluje bazę między klasami, ale współdzieli ją wewnątrz klasy. Nie przenosić wszystkich klas na jedną kolekcję/DB wyłącznie dla szybszego startu kontenerów bez planu resetu i serializacji. Współbieżność w środku testu nie uzasadnia twierdzenia, że wszystkie testy współdzielą jednego sendera. Thread-safe kolejka w `tests/AnalyticDashboard.IntegrationTests/Auth/TestEmailSender.cs:8-33` nie zastępuje izolacji scenariusza i precyzyjnego wyboru wiadomości.

**Uzupełnienie o pliki:** obecna fabryka nie zastępuje IFileStorage testową instancją (`tests/AnalyticDashboard.IntegrationTests/CustomWebApplicationFactory.cs:39-63`), a Dispose fixture (`tests/AnalyticDashboard.IntegrationTests/ApiFixture.cs:64-81`) nie usuwa plików aplikacji. Docelowy scenariusz powinien rejestrować własne klucze przed asercjami mogącymi przerwać test, a cleanup korzystać z niezależnego ograniczonego tokena zamiast już anulowanego tokena testu. Samo finally nie gwarantuje sprzątania, jeśli jego pierwsza operacja natychmiast rzuca anulowanie. Szczegółowe porównania przypadków zawiera AUD-036 w raporcie 06.

## S-15 — asercje i nazwy testów opisują zachowanie

**Propozycja:** nazwa `Operation_ShouldExpected_WhenCondition` tam, gdzie ma to sens; test ma widoczne przygotowanie, wywołanie i weryfikację. Małe helpery ProblemDetails/cookies/state mogą usuwać powtarzalne parsowanie, ale oczekiwany status, kod błędu i kluczowe wartości pozostają jawne. Po zapisie sprawdzić stan świeżym scope, aby tracker nie zastąpił dowodu persistence.

**Podstawa:** snapshot i AssertUnchanged w `tests/AnalyticDashboard.IntegrationTests/Widgets/UpdateDashboardWidgetsEndpointTests.cs:190-233` oraz deterministyczny konflikt UnitOfWork `:1008-1051` sprawdzają zachowanie. Dla kontrastu sam Task.WhenAll z `:957-1005` nie gwarantuje przejścia przez handler catch — to AUD-019, nie nowe ustalenie.

**Granica:** nie usuwać podobnych testów, jeśli sprawdzają inne reguły (np. źle sformatowany JSON, null i brak wymaganego pola). Nie przenosić wszystkich asercji do jednej nieprzezroczystej funkcji „AssertSuccess”. Używać sygnałów kolejności zamiast sleepów przy wymuszaniu wyścigu. Test regresyjny ma zawieść na konkretnym błędnym zachowaniu, a nie na dowolnej zmianie formatowania komunikatu.

**Dwa konkretne zastosowania wspólnych asercji:**

- Usunięcie cookie sprawdzać przez parsowanie właściwego nagłówka, nazwę/Path, pustą wartość i skuteczne wygaśnięcie według Max-Age/Expires; samo wystąpienie `expires=` dopuszcza również przyszłą datę. Obecne słabe warunki powtarzają się w `tests/AnalyticDashboard.IntegrationTests/Auth/RegistrationStatusEndpointTests.cs:338-355`, `tests/AnalyticDashboard.IntegrationTests/Auth/CompleteRegistrationEndpointTests.cs:368-385` i `tests/AnalyticDashboard.IntegrationTests/Auth/GetCurrentUserEndpointTests.cs:509-525` (AUD-038).
- Test awarii wysyłki ma potwierdzić próbę wywołania SendAsync i zużycie kontrolowanej awarii dla właściwego odbiorcy, obok publicznego 204. Sam status w `tests/AnalyticDashboard.IntegrationTests/Auth/ForgotPasswordEndpointTests.cs:185-203` i `tests/AnalyticDashboard.IntegrationTests/Auth/ResendConfirmationEndpointTests.cs:353-371` przejdzie również przy regresji całkowicie pomijającej wysyłkę (AUD-039). Izolować/resetować stan wstrzykniętej awarii, aby nie przechodził do kolejnego testu tej samej klasy.

## S-16 — styl C# porządkować lokalnie i mechanicznie

**Propozycja stylistyczna, nie błąd:** using: System.*, Microsoft.*, biblioteki zewnętrzne, AnalyticDashboard.*; wewnątrz grup alfabetycznie. File-scoped namespace. W klasie: stałe → pola → konstruktor(y) → publiczne metody pogrupowane funkcją → prywatne helpery. W metodzie utrzymywać lokalne grupy walidacji/odczytu/zmiany/zapisu, zamiast pustej linii po każdym argumencie. Dłuższe listy argumentów łamać konsekwentnie; krótkich deklaracji nie rozbijać obowiązkowo na wiele linii.

**Podstawa porównawcza:** `src/AnalyticDashboard.Application/Projects/CreateProject/CreateProjectHandler.cs:1-4` zaczyna od System, a `src/AnalyticDashboard.Application/Projects/RenameProject/RenameProjectHandler.cs:1-3` ma System po using projektu. `src/AnalyticDashboard.Infrastructure/DependencyInjection.cs:1-24` miesza grupy. `src/AnalyticDashboard.Domain/Entities/Widget.cs:8-43` umieszcza stałe po właściwościach, w odróżnieniu od fields-first `src/AnalyticDashboard.Infrastructure/Services/Import/ImportJobProcessor.cs:9-20`.

To świadomy wybór zespołu, nie dowiedzione źródło defektów. Nie dodawać pakietu analizatora ani zmieniać konfiguracji podczas audytu. Jeżeli konwencja zostanie przyjęta, wdrażać ją automatycznie w zmienianych plikach; masowe formatowanie oddzielić od poprawek funkcjonalnych, a pliki wygenerowane przez EF pozostawić generatorowi.

## S-17 — abstrakcja musi upraszczać konkretnych konsumentów

**Propozycja:** nowy helper/port musi wskazać istniejących użytkowników i uprościć ich bez zatarcia semantyki. Usuwać niewywoływaną ścieżkę zamiast tworzyć dla niej standard i testy. Nie oceniać jakości liczbą warstw, wspólnych klas ani znalezionych usterek.

**Podstawa:** istniejący IUnitOfWork ma konkretny konsument — zapis widgetów; `src/AnalyticDashboard.Application/Common/Persistence/IUnitOfWork.cs:3-5` jest minimalny. DatasetQueryExecutor ma dwóch konsumentów query/widget-data, a martwe metody repozytoriów i CsvStructureInfo opisano już jako AUD-020. Podobne algorytmy paginacji w `src/AnalyticDashboard.Application/Projects/GetProjects/GetProjectsHandler.cs:24-61` i `src/AnalyticDashboard.Application/Datasets/GetDatasetRows/GetDatasetRowsHandler.cs:65-93` nie uzasadniają same w sobie nowego frameworka paginacji — źródła total count i odczytu są inne.

**Kryterium:** rozwiązanie da się opisać przez usunięty koszt, zmniejszone ryzyko lub czytelniejszy scenariusz konkretnego konsumenta. Sama mniejsza liczba powtórzonych linii nie wystarcza.

## Jak przyjmować tę propozycję

Najpierw uzgodnić kontrakty i granice commit, a potem stosować je w małych zmianach. Właściwe sprawdzenie obejmuje stan bazy/plików, odpowiedzi HTTP i realnych konsumentów. Testy bazy wymagają prawdziwego providera, a kontrakty cookies/CSRF także właściwego środowiska HTTP/przeglądarki.

Drugi audyt jest statyczny: nie wykonano nowych testów, buildów, migracji ani pomiarów. Wynik 19/19 testów z raportu 01 dotyczy wcześniejszego uruchomienia istniejącego artefaktu, nie nowej weryfikacji tej propozycji. Standardy należy zatwierdzić jako decyzje zespołu; sam plik nie zmienia działania aplikacji.

Kontrola końcowa porównała sumy kontrolne 295 plików źródeł, testów, konfiguracji `.config`, solution i raportów 01–03 ze stanem na początku drugiego audytu: brak zmienionych, dodanych lub usuniętych plików w tym zestawie. Nowe pliki to wyłącznie raporty 04–07. Sprawdzono także istnienie plików i poprawność zakresów 331 odwołań do kodu w nowych raportach; jest to kontrola referencji, nie zastępstwo wykonania testów lub pomiarów. Nowe ustalenia mają unikalne ID AUD-025–039, bez zmiany wcześniejszej numeracji.

## Dziesięć propozycji o największej wartości

Kolejność uwzględnia korzyść praktyczną, wysiłek i ryzyko regresji; nie jest fikcyjnym rankingiem liczbowym. Dotyczy ujednolicenia i optymalizacji z drugiego audytu. Pilne naprawy bezpieczeństwa i utraty danych AUD-001–004 pozostają wcześniejszym priorytetem niezależnie od tej listy.

| Miejsce | Propozycja | Korzyść | Wysiłek | Ryzyko regresji | Oparcie / warunek |
|---|---|---|---|---|---|
| 1 | Jawna własność plików testowych i cleanup odporny na przerwanie testu | mniej osieroconych danych i zależności od przypadkowego powodzenia asercji | Medium | Medium | AUD-036; S-14, nie korzystać z badanego DELETE do sprzątania |
| 2 | Wspólne semantyczne asercje auth: skuteczne usunięcie cookie i faktyczna próba wysyłki przy awarii | testy wykrywają realne regresje zamiast akceptować dowolne expires/status204 | Small | Low | AUD-038 / AUD-039; S-15, nadal jawne oczekiwania w teście |
| 3 | Wspólny kontrakt błędów walidacji, z przypisaniem do pola/widgetu | klient może poprawić właściwy element zamiast zgadywać z ogólnego detail | Small | Medium | AUD-027; S-08, ograniczone bezpieczne diagnostyki i przegląd klienta |
| 4 | Wąski autoryzowany odczyt statusu importu | polling może wykonać jeden SELECT zamiast dwóch szerokich odczytów | Medium | Medium | AUD-030; S-05/06, owner/project i kolejność „latest” pozostają |
| 5 | Wydzielić mechanikę auth w testach, pozostawiając badany krok jawny | krótsze testy reset/login i jedna kontrolowana obsługa tokenów/cookies | Medium | Medium | AUD-034; S-13/14, setup nie ukrywa testowanej rejestracji |
| 6 | Scenariusz importu testowego, wspólne multipart i małe seedy projektów | mniej kopiowanego setupu, bez generycznego frameworka testów | Medium | Medium | AUD-035 / AUD-037; S-13/14, dane CSV i właściwe asercje pozostają lokalne |
| 7 | AnyAsync dla istniejących konsumentów potrzebujących wyłącznie istnienia projektu | mniej danych i brak niepotrzebnej encji bez zmiany liczby requestów DB | Small | Low | AUD-029; S-05, Rename nadal potrzebuje danych |
| 8 | Wąski read path bieżącej wersji datasetu tam, gdzie dwa odczyty są tylko metadanymi | mniej round-tripów do PostgreSQL i powtarzanej selekcji | Medium | Medium | AUD-031; S-05/09, zachować stany oraz zgodność DatasetId wersji |
| 9 | Ujawnić granicę wspólnego flush/commit, szczególnie importu; zachować wyjątki dla Execute i Identity | łatwiejsze utrzymanie atomowości i mniej przypadkowych wczesnych zapisów | Small | Medium | AUD-028; S-02/03, bez mechanicznej migracji wszystkich repozytoriów |
| 10 | Ustalić politykę parent-not-found i dataset-not-ready w analogicznych endpointach | przewidywalny klient i spójne testy stanów przejściowych | Medium | Medium | AUD-025 / AUD-026; S-09, metadane i query nie muszą mieć identycznego statusu |


