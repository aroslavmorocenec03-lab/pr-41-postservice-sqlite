# Звіт: ПР-41 «Робота з базою даних»

## Мета

Зберігати, отримувати, змінювати та видаляти об’єкти Posting у SQLite, відокремивши ADO.NET і SQL від бізнес-правил.

## Реалізація

До рішення ПР-40 додано бібліотеку `PostService.DataAccess`, пакет `Microsoft.Data.Sqlite` версії 10.0.14 та посилання на `PostService.Models`. BusinessLogic посилається на DataAccess. У SQL-репозиторії немає HTTP-коду; сервіс залежить від `IPostingRepository`, а не від конкретного PostingRepository. Статичних колекцій відправлень у сервісі більше немає.

Інтерфейс описує `GetList(): List<Posting>`, `GetById(int): Posting?`, `Create(Posting): int`, `Update(Posting): int`, `Delete(int): int`. Результат Update — кількість оновлених рядків: сервіс може повернути null, якщо запис уже відсутній.

У PostingRepository кожен метод відкриває власне з’єднання. З’єднання, команда й читач звільняються через `using`. Текст усіх SQL-команд задано в коді; значення надходять через параметри. Інтерполяція в SELECT вставляє лише незмінний внутрішній перелік стовпців, а не клієнтські дані.

| Операція | SQL / метод виконання |
|---|---|
| CreateDb | CREATE TABLE IF NOT EXISTS / ExecuteNonQuery |
| GetList | SELECT ... ORDER BY Id / ExecuteReader |
| GetById | SELECT ... WHERE Id = @Id / ExecuteReader |
| Create | INSERT ... RETURNING Id / ExecuteScalar |
| Update | UPDATE ... WHERE Id = @Id / ExecuteNonQuery |
| Delete | DELETE ... WHERE Id = @Id / ExecuteNonQuery |

Структура таблиці відповідає прикладу завдання: Id — INTEGER PRIMARY KEY; From, To, Content — TEXT NOT NULL; DeliveryType — INTEGER; вага, розміри, Price — REAL; Value — REAL NULL; Description — TEXT NULL; CreatedAt — INTEGER. DeliveryType записується як int і читається явним приведенням із GetInt32. Nullable-поля передаються як DBNull.Value, перед читанням перевіряється IsDBNull. UTC-дата конвертується в Unix seconds і назад.

Для узгодження з документом до моделі та всіх DTO додано Description, Value зроблено nullable, розміри — float. Мапер переносить усі ці поля. Старі цілочисельні розміри й числове Value залишаються допустимими значеннями JSON.

У Program.cs до Build зареєстровано Scoped-репозиторій і сервіс. Після Build створюється короткий DI-scope, через який викликається CreateDb, до обробки запитів і app.Run. Шлях бази формується від ContentRoot, а connection string — через SqliteConnectionStringBuilder.

PostingService зберігає попередню перевірку даних і розрахунок ціни, присвоює UTC CreatedAt із точністю до секунди, отримує Id із SQLite. Update не змінює CreatedAt. Валідація ваги, розмірів, типу доставки, обов’язкових полів, Value/Price виконується на рівні бізнес-логіки. Усі п’ять операцій працюють через інтерфейс репозиторію.

## Перевірка

Виконано `dotnet build PostService.slnx -warnaserror`: 0 помилок, 0 попереджень. У рішенні шість проєктів; файли команд і результатів додаються до цього звіту.

Власна консольна перевірка працює з тимчасовими SQLite-файлами. Успішно виконано 11 сценаріїв репозиторію/сервісу та 8 перевірок реального HTTP API — **19/19**. Перевірено всі поля моделі, дробові значення, усі види доставки, NULL, Unicode, лапки та SQL-подібний текст у параметрах, повторний CreateDb без втрати даних, CRUD і відсутні записи, серверні тарифи й валідацію.

Практичне підтвердження збереження даних:

1. Запущено окремий процес API з новою тимчасовою базою.
2. Через POST створено три відправлення; отримано 201 та Location.
3. Процес зупинено. Безпосередній SELECT COUNT(*) із файлу SQLite повернув 3.
4. API запущено повторно з тією самою базою. GET повернув ті самі Id, поля й CreatedAt.
5. Перевірено PUT і GET зміненого запису та 400 для некоректної ваги.
6. DELETE повернув 204. Після ще одного перезапуску GET/DELETE видаленого запису повернули 404, а список містив два інші записи.

Фактичний протокол: [checks-output.txt](checks-output.txt); збірка: [build-results.txt](build-results.txt). `requests.http` підготовлений для ручної демонстрації; наведені результати отримані автоматизованими HTTP-запитами, без тверджень про ручний запуск через REST Client.

## Контрольні запитання

1. **Роль репозиторію.** Він інкапсулює читання й запис даних. Окрема бібліотека ізолює SQL та провайдер від бізнес-правил. Іншу реалізацію того самого інтерфейсу можна підключити через DI.
2. **Міграція.** Готує структуру БД до роботи. CreateDb викликається під час запуску після Build, до першого HTTP-запиту. У цій роботі це початкове CREATE TABLE IF NOT EXISTS; воно не змінює структуру старих таблиць.
3. **Connection string і SqliteConnection.** Рядок з’єднання задає файл та параметри доступу, наприклад Data Source=PostingDb.db. SqliteConnection відкриває з’єднання, в межах якого виконуються команди; його потрібно закривати або Dispose.
4. **Методи виконання.** ExecuteNonQuery повертає кількість змінених рядків і підходить для UPDATE/DELETE/DDL. ExecuteReader повертає читач для рядків SELECT. ExecuteScalar повертає перше значення першого рядка, що зручно для INSERT RETURNING Id або COUNT(*).
5. **Параметри.** У SQL записують @Name, а значення задають через cmd.Parameters.AddWithValue. Вони відокремлюють текст команди від даних, тому лапки й SQL-подібні рядки залишаються значеннями та не стають командами. Не потрібно вручну форматувати числа або екранувати текст.
6. **Читання SELECT.** ExecuteReader повертає SqliteDataReader. Цикл while(reader.Read()) перебирає рядки; GetOrdinal знаходить індекс стовпця, типізовані GetInt32/GetString/GetFloat читають значення. IsDBNull потрібен перед читанням SQL NULL, щоб повернути null замість помилки типізованого читання.
7. **Отримання Id.** INSERT пропускає Id, SQLite генерує INTEGER PRIMARY KEY; RETURNING Id повертає його в результаті команди. ExecuteScalar і Convert.ToInt32 дають сервісу справжній ідентифікатор нового запису.
8. **DELETE.** ExecuteNonQuery повертає кількість видалених рядків. Для унікального Id це 1 або 0. Delete передає результат сервісу; API повертає 204 для видалення та 404, якщо запису не було.
9. **Зміни PostingService.** Видаляється статична колекція, у конструктор додається IPostingRepository. Create/GetAll/Find/Update/Delete делегують зберігання репозиторію; перевірки, ціна й дата лишаються в сервісі. Інтерфейс дозволяє підмінити механізм зберігання, не переписуючи бізнес-логіку.

## Висновок

Операції PostService переведені на SQLite; повторний запуск створює відсутню таблицю та зберігає наявні дані. Бізнес-логіка працює з контрактом репозиторію, а SQL зосереджено в DataAccess. Обмеження навчальної реалізації: синхронний ADO.NET, локальний файл, лише початкова міграція схеми; Id INTEGER PRIMARY KEY може повторно використовувати звільнені значення згідно з поведінкою SQLite.

## Джерела

- [Microsoft: параметри Microsoft.Data.Sqlite](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/parameters) — параметризовані команди та відокремлення даних від SQL.
- [Офіційний пакет Microsoft.Data.Sqlite на NuGet](https://www.nuget.org/packages/Microsoft.Data.Sqlite/10.0.14) — використаний ADO.NET-провайдер.
