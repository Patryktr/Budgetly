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
- Podsumowanie pokazuje przychody, wydatki, saldo i wykres kategorii. Wykres Chart.js porównuje wydatki z sześciu miesięcy do wybranego miesiąca włącznie. Cele oszczędnościowe mają ręcznie aktualizowany postęp. Proces w tle co kilka sekund oznacza osiągnięte cele i zapisuje datę osiągnięcia.

## Model danych

~~~mermaid
erDiagram
    USERS ||--o{ TRANSACTIONS : "rejestruje"
    USERS ||--o{ MONTHLY_BUDGETS : "ustawia"
    USERS ||--o{ GOALS : "planuje"
    USERS o|--o{ CATEGORIES : "tworzy prywatne"
    CATEGORIES ||--o{ TRANSACTIONS : "grupuje"
~~~

Para UserId i MonthStart jest kluczem budżetu miesięcznego. Brak UserId w kategorii oznacza kategorię wspólną. Limit obejmuje wszystkie wydatki użytkownika w miesiącu wynikającym z daty transakcji.

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
| GET, POST /api/goals | Listuje lub tworzy cele. Formularz celu ma pola name, target i saved. |
| PUT, DELETE /api/goals/{id} | Edytuje lub usuwa własny cel. |

Prywatne endpointy wymagają nagłówka Authorization: Bearer TOKEN. W JSON type ma wartość 1 (przychód) lub 2 (wydatek). Daty mają postać RRRR-MM-DD. Zwykłe przekroczenie limitu zwraca budgetWarning przy udanym zapisie. Ścisły limit zwraca HTTP 409 z danymi limitu w odpowiedzi.

## Testy i CI

~~~powershell
dotnet test Budgetly.sln
~~~

Testy jednostkowe sprawdzają kwoty i progi budżetu. Testy integracyjne wywołują rzeczywiste endpointy oraz logowanie przez testowy host z osobną bazą SQLite w pamięci. Środowisko Docker korzysta z SQL Server. GitHub Actions uruchamia testy .NET i kompilację Angulara po pushu oraz przy pull requestach.

## Scenariusz demo 3–5 minut

1. Załóż konto, zaloguj się i wybierz bieżący miesiąc.
2. Dodaj przychód 1000 zł, ustaw limit 100 zł i dodaj wydatek 110 zł. Pokaż ostrzeżenie, saldo i wykres.
3. Podnieś limit do 120 zł i zaznacz blokadę. Spróbuj dodać kolejny wydatek 15 zł. Pokaż, że historia pozostała bez zmian.
4. Pokaż filtrowanie historii, edycję wydatku i cel oszczędnościowy. Przy odłożonej kwocie równej celowi proces w tle zapisze datę osiągnięcia. Odśwież stronę po kilku sekundach.
5. Wyjaśnij przebieg jednego żądania: Angular → endpoint → reguła budżetu → EF Core → SQL Server. Na zakończenie pokaż ERD i plik Compose.
