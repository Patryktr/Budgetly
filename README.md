# Budgetly

Budgetly to aplikacja studencka do prowadzenia domowego budżetu. Rejestrujesz przychody i wydatki, ustawiasz miesięczny limit i śledzisz cele oszczędnościowe.

## Uruchomienie

Potrzebujesz Docker Desktop z uruchomionym silnikiem kontenerów linuksowych. Docker buduje Angulara 22 (Node 24), uruchamia go przez nginx, buduje API ASP.NET Core 10 oraz uruchamia SQL Server 2025 Developer i Redis 8. Instalowanie SDK .NET i Node na komputerze nie jest wymagane.

1. Jeśli masz już plik .env z ustawionymi sekretami, użyj go. W nowym katalogu skopiuj .env.example jako .env.
2. Ustaw DB_PASSWORD na silne hasło do deweloperskiego SQL Server i JWT_SIGNING_KEY na osobny losowy sekret o długości co najmniej 32 bajtów. Najprościej wygenerować oba jako ciągi szesnastkowe, dodając do hasła SQL znaki wymagane przez SQL Server, np. prefiks Aa7!. Nie publikuj pliku .env.
3. W katalogu projektu uruchom:

       docker compose up --build -d --wait

4. Wejdź na [http://localhost:4200](http://localhost:4200) i załóż konto. [Dokument OpenAPI](http://localhost:8080/openapi/v1.json) jest dostępny na porcie API. Migracje bazy wykonują się automatycznie podczas startu API.

Jeśli obrazy Budgetly są już zbudowane i chcesz ponownie uruchomić tę samą wersję, użyj `docker compose up -d --no-build --wait`.

Opcjonalnie ustaw BUDGETLY_ADMIN_EMAIL i BUDGETLY_ADMIN_PASSWORD (minimum 12 znaków) przed startem. Aplikacja utworzy konto ADMIN, jeśli podany e-mail jest wolny. Zwykła rejestracja tworzy wyłącznie USER. Zmiana hasła w pliku .env nie zmienia już utworzonego konta.

Dane są przechowywane w wolumenie budgetly-data. Polecenie docker compose down zatrzymuje i usuwa kontenery, zachowując wolumen. Aplikacja i API nasłuchują na interfejsie lokalnym, a baza jest dostępna na wewnętrznej sieci Compose.

## Cache i limity żądań

- Redis działa w sieci Compose bez portu dostępnego z hosta i bez trwałego wolumenu. API przechowuje listę kategorii osobno dla każdego użytkownika przez minutę. Dodanie, zmiana nazwy i usunięcie kategorii unieważniają wpis po zapisie. Jeśli Redis jest niedostępny, odczyt trafia do SQL Server.
- Logowanie i rejestracja współdzielą limit 20 żądań na minutę na adres połączenia. W lokalnym Compose żądania z przeglądarki, które przechodzą przez nginx, współdzielą limit proxy.
- Prywatne żądania API mają osobny limit dla każdego użytkownika. Konto może wysłać do 120 żądań naraz, a limit odnawia się o dwa żądania na sekundę. Po wyczerpaniu limitu API zwraca 429 i nagłówek Retry-After. Liczniki działają w procesie API.

## Główne funkcje

- Rejestracja i logowanie z JWT. Token jest ważny przez godzinę. Użytkownik widzi własne dane. ADMIN może odczytać liczbę kont.
- Transakcje mają kategorię, datę, kwotę i opcjonalny opis. Kwota w formularzu jest dodatnia. Przychód jest zapisywany z plusem, a wydatek z minusem. Kategoria musi pasować do typu transakcji.
- Historia ma filtry miesiąca, typu i kategorii oraz paginację. Można dodawać, edytować i usuwać transakcje. Kategorie początkowe są wspólne, a kategorie własne są prywatne.
- Import CSV pozwala wczytać własny plik, wybrać kategorie i zapisać zaznaczone pozycje z podglądu.
- Jeden limit na miesiąc. Zwykły limit pozwala zapisać wydatek i sygnalizuje przekroczenie. Limit o wysokim priorytecie blokuje zapis lub edycję zwiększającą wydatki ponad limit. Korekta zmniejszająca wydatek w tym samym miesiącu pozostaje możliwa, gdy miesiąc jest już ponad limitem.
- Podsumowanie pokazuje przychody, zwykłe wydatki, dostępne saldo i wpłaty na oszczędności. Saldo to przychody minus wydatki minus wpłaty na cele. Wykres Chart.js porównuje zwykłe wydatki z sześciu miesięcy do wybranego miesiąca włącznie.
- Nowy cel oszczędnościowy zaczyna od zera. Przycisk „Wpłać” tworzy powiązaną transakcję w kategorii Oszczędności i zwiększa postęp celu w jednej transakcji bazodanowej. Wpłaty pomniejszają saldo, ale pozostają poza miesięcznym limitem i statystykami zwykłych wydatków, również przy wysokim priorytecie. Można wpłacać ponad kwotę docelową.
- Korekta lub usunięcie wpłaty w historii aktualizuje również kwotę odłożoną na cel. Typ i kategoria wpłaty pozostają stałe. Usunięcie celu zachowuje jego wpłaty w historii jako oszczędności, nadal poza limitem. Dotychczasowe ręcznie wpisane oszczędności pozostają na celach jako kwoty odłożone wcześniej; migracja nie tworzy za nie wydatków wstecz.
- Proces w tle co kilka sekund oznacza osiągnięte cele i zapisuje datę osiągnięcia. Korekta zmniejszająca oszczędności poniżej kwoty docelowej usuwa oznaczenie osiągnięcia.

## Model danych

SQL Server przechowuje pięć głównych tabel. Schemat uwzględnia klucze główne (`PK`), obce (`FK`) i unikalność (`UK`). Pola opcjonalne opisano jako `NULL`.

~~~mermaid
erDiagram
    Users ||--o{ Transactions : "rejestruje"
    Users ||--o{ MonthlyBudgets : "ustawia"
    Users ||--o{ Goals : "planuje"
    Users o|--o{ Categories : "tworzy prywatne"
    Categories ||--o{ Transactions : "grupuje"
    Goals o|--o{ Transactions : "otrzymuje wplaty"

    Users {
        uniqueidentifier Id PK
        nvarchar Email UK "maks. 254 znaki"
        nvarchar PasswordHash "hash hasla"
        nvarchar Role "USER lub ADMIN"
    }

    Categories {
        uniqueidentifier Id PK
        uniqueidentifier UserId FK "NULL dla kategorii wspolnej"
        nvarchar Name "maks. 80 znakow"
        nvarchar Type "Income lub Expense"
    }

    Transactions {
        uniqueidentifier Id PK
        uniqueidentifier UserId FK
        uniqueidentifier CategoryId FK
        uniqueidentifier GoalId FK "NULL dla wpisu bez celu"
        bit IsSavings "wplata poza limitem wydatkow"
        decimal Amount "decimal(12,2); przychod dodatni, wyplyw ujemny"
        date Date "data transakcji"
        nvarchar Description "NULL; maks. 250 znakow"
        datetime2 CreatedAtUtc "czas utworzenia wpisu"
    }

    MonthlyBudgets {
        uniqueidentifier UserId PK,FK
        date MonthStart PK "pierwszy dzien miesiaca"
        decimal Limit "decimal(12,2)"
        bit HighPriority "blokada przekroczenia limitu"
    }

    Goals {
        uniqueidentifier Id PK
        uniqueidentifier UserId FK
        nvarchar Name "maks. 100 znakow"
        decimal Target "decimal(12,2); kwota docelowa"
        decimal Saved "decimal(12,2); odlozona kwota"
        datetime2 CompletedAtUtc "NULL do osiagniecia celu"
    }
~~~

| Tabela | Rola i najważniejsze ograniczenia |
| --- | --- |
| `Users` | Konto użytkownika. Unikalny indeks `Email` zapobiega powieleniu adresu po jego normalizacji przez API. |
| `Categories` | Kategoria przychodu lub wydatku. `UserId = NULL` oznacza kategorię wspólną; pozostałe należą do konkretnego użytkownika. |
| `Transactions` | Historia przychodów, wydatków i wpłat na cele. Indeks `(UserId, Date)` wspiera filtrowanie historii. `GoalId` łączy wpłatę z celem. |
| `MonthlyBudgets` | Klucz złożony `(UserId, MonthStart)` zapewnia jeden limit na miesiąc dla użytkownika. |
| `Goals` | Nazwa celu, kwota docelowa, suma oszczędności i opcjonalna data osiągnięcia. |

Każda transakcja ma użytkownika i kategorię, a wpłata może dodatkowo wskazywać cel. Powiązania mają regułę `Restrict`: baza nie usuwa automatycznie zależnych danych. Kategorii używanej przez transakcje nie można usunąć.

Usunięcie celu odłącza jego transakcje przez ustawienie `GoalId = NULL`, zachowując `IsSavings = true`. Dzięki temu wpłaty pozostają w historii i nadal są wyłączone z limitu oraz statystyk zwykłych wydatków. Saldo wybranego miesiąca to przychody minus zwykłe wydatki minus wpłaty na oszczędności. Miesiąc wynika z pola `Date`.

Model i relacje są skonfigurowane w `Budgetly.Api/Data/BudgetlyDbContext.cs`. Zmiany schematu przechowują migracje w `Budgetly.Api/Data/Migrations`:

1. `InitialCreate` — transakcje, kategorie początkowe i budżety.
2. `CompleteBudgetly` — konta, cele i powiązania z użytkownikami.
3. `GoalContributions` — pola `GoalId` i `IsSavings`, relacja wpłat z celami i kategoria Oszczędności.

Przy starcie w Docker Compose `Database.MigrateAsync()` wykonuje brakujące migracje. EF Core zapisuje ich wykonanie w technicznej tabeli `__EFMigrationsHistory`, więc ponowny start nie odtwarza istniejących tabel ani nie zeruje danych.

## Import CSV

W widoku **Importuj transakcje z CSV** można pobrać gotowy szablon. Plik w kodowaniu UTF-8 zawiera do 100 transakcji (do 128 KB). Kolumny są oddzielone średnikiem, kwota jest dodatnia, a separator dziesiętny może być przecinkiem lub kropką. Nazwa kategorii może być pusta, jeśli wybierzesz ją w podglądzie.

```csv
data;typ;kwota;kategoria;opis
2026-11-16;wydatek;27,50;Zakupy spożywcze;Zakupy
2026-11-16;przychód;1000;Wynagrodzenie;Wypłata
```

Wiersze są zapisywane kolejno w porządku pliku. Każdy wydatek przechodzi zwykłą kontrolę budżetu. Powtórny import identycznej pozycji CSV przez to samo konto zgłasza duplikat, jeśli oryginalny wpis wciąż istnieje. Identyczność uwzględnia datę, rodzaj, kwotę, wybraną kategorię i opis. Podgląd działa w przeglądarce, a do API trafiają wyłącznie wybrane pozycje.

## REST API

| Metoda i ścieżka | Przeznaczenie |
| --- | --- |
| POST /api/auth/register | Tworzy konto USER. Przyjmuje email i password. |
| POST /api/auth/login | Zwraca JWT, czas ważności i rolę. Przyjmuje email i password. |
| GET /api/admin/summary | Zwraca liczbę kont. Wymaga roli ADMIN. |
| GET /api/categories | Zwraca kategorie wspólne i własne. |
| POST /api/categories | Tworzy kategorię z name i type. |
| PUT, DELETE /api/categories/{id} | Zmienia nazwę (name) lub usuwa własną kategorię. |
| GET /api/transactions | Historia z parametrami from, to, type, categoryId, page i pageSize. |
| POST /api/transactions | Tworzy transakcję z amount, type, date, categoryId i opcjonalnym description. Pole fromCsv włącza wykrywanie powtórnego importu. |
| GET, PUT, DELETE /api/transactions/{id} | Odczytuje, aktualizuje lub usuwa własną transakcję. |
| GET /api/budgets/{month} | Zwraca limit, podsumowanie miesiąca RRRR-MM i serię wydatków `spendingTrend`. |
| PUT, DELETE /api/budgets/{month} | Ustawia limit (limit i highPriority) lub usuwa limit. |
| GET, POST /api/goals | Listuje lub tworzy cele. Nowy cel ma pola name i target; saved zaczyna od zera. |
| PUT, DELETE /api/goals/{id} | Zmienia nazwę i kwotę docelową lub usuwa własny cel, zachowując jego wpłaty. |
| POST /api/goals/{id}/contributions | Dodaje wpłatę z amount i date; zwraca goal i powiązaną transaction. |

Prywatne endpointy wymagają nagłówka Authorization: Bearer TOKEN. W JSON type ma wartość 1 (przychód) lub 2 (wydatek). Daty mają postać RRRR-MM-DD. Zwykłe przekroczenie limitu zwraca budgetWarning przy udanym zapisie. Ścisły limit zwraca HTTP 409 z danymi limitu w odpowiedzi.

## Struktura backendu

Kod znajduje się w `Budgetly.Api/Features`, w folderach funkcji. Endpointy deklarują adresy, wymagane uprawnienia i modele wejściowe, a następnie wywołują handlery. Handlery zawierają walidację, reguły operacji i dostęp do danych przez EF Core. Przyjmują identyfikator użytkownika jako argument, dzięki czemu logika operacji jest niezależna od kontekstu HTTP.

- `Auth/Register/RegisterHandler` i `Auth/Login/LoginHandler` obsługują rejestrację i logowanie.
- `Transactions/CreateTransaction/CreateTransactionHandler` dodaje transakcje, a `Transactions/Manage/TransactionHandler` obsługuje historię, szczegóły, edycję i usuwanie.
- `Budgets/SetMonthlyBudget/SetMonthlyBudgetHandler` ustawia limit. `Budgets/MonthlyBudgetHandler` oblicza podsumowanie i trend oraz usuwa limit.
- `Categories/CategoryHandler` zarządza kategoriami i unieważnia cache po zapisie.
- `Goals/GoalHandler` zarządza celami. `Goals/Contribute/ContributeToGoalHandler` zapisuje wpłatę i zwiększa oszczędności atomowo. `CompleteGoalsHandler` oznacza osiągnięte cele, a worker wywołuje go okresowo.

Modele request i response opisują kontrakty API. `Application/HandlerResult` przenosi wynik lub błąd operacji, a `Infrastructure/HandlerHttpResults` zamienia go na odpowiedź HTTP. Dodawanie transakcji ma własny model wyniku uwzględniający ostrzeżenie budżetowe i powtórny import CSV. `BudgetPolicy` zawiera współdzielone reguły finansowe.

## Testy i CI

~~~powershell
dotnet test Budgetly.sln
~~~

Testy jednostkowe sprawdzają kwoty i progi budżetu. Testy integracyjne wywołują rzeczywiste endpointy oraz logowanie przez testowy host z osobną bazą SQLite w pamięci. Sprawdzają również wpłaty poza limitem wydatków, korekty i usuwanie wpłat, zachowanie historii po usunięciu celu oraz dostęp do prywatnych danych. Środowisko Docker korzysta z SQL Server. GitHub Actions uruchamia testy .NET i kompilację Angulara po pushu oraz przy pull requestach.

## Scenariusz demo 3–5 minut

1. Przed prezentacją przygotuj konto z przykładowymi przychodami, wydatkami i wpłatami na cele za trzy miesiące. Dane do logowania pozostają lokalnie.
2. Zaloguj się, przełącz miesiące i pokaż raporty, wykres oraz szczegóły kategorii Jedzenie, Sport i Rower.
3. Dodaj wydatek. Ustaw zwykły limit i pokaż zapis z ostrzeżeniem, a następnie włącz wysoki priorytet i pokaż blokadę przekroczenia.
4. Popraw kwotę transakcji w historii. Wczytaj CSV, sprawdź podgląd i zapisz wybrane pozycje. Ponownie wczytaj plik, żeby pokazać wykrywanie duplikatów.
5. Przy celu użyj „Wpłać”: saldo maleje, a zwykłe wydatki i limit pozostają bez zmian. Po wpłacie osiągającej cel odśwież stronę po kilku sekundach, żeby zobaczyć datę osiągnięcia.
6. Wyloguj użytkownika, zaloguj administratora i pokaż sekcję Administracja z liczbą kont.
