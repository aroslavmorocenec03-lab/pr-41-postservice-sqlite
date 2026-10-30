using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PostService.BusinessLogic;
using PostService.CommonTypes;
using PostService.DataAccess;
using PostService.Models;

Console.OutputEncoding = Encoding.UTF8;
var temp = Path.Combine(Path.GetTempPath(), "PostService-Pr41-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
int passed = 0, failed = 0, sequence = 0;
try
{
    Run("CreateDb створює таблицю; повторний виклик зберігає дані", () =>
    {
        var repo = Repository(); var id = repo.Create(Model()); repo.CreateDb();
        Equal(1, repo.GetList().Count); Equal(id, repo.GetById(id)!.Id);
    });
    Run("Create генерує Id, GetById повертає всі поля", () =>
    {
        var repo = Repository(); var p = Model(); p.Id = 999;
        var id = repo.Create(p); Equal(1, id); var saved = repo.GetById(id)!;
        Equal(p.From, saved.From); Equal(p.To, saved.To); Equal(p.Content, saved.Content);
        Equal(p.DeliveryType, saved.DeliveryType); Equal(p.Weight, saved.Weight);
        Equal(p.Width, saved.Width); Equal(p.Height, saved.Height); Equal(p.Depth, saved.Depth);
        Equal(p.Value, saved.Value); Equal(p.Price, saved.Price);
        Equal(p.Description, saved.Description); Equal(p.CreatedAt, saved.CreatedAt);
    });
    Run("NULL Value/Description і всі DeliveryType проходять SQLite round-trip", () =>
    {
        var repo = Repository();
        foreach (var type in Enum.GetValues<DeliveryType>())
        {
            var p = Model(); p.DeliveryType = type; p.Value = null; p.Description = null;
            var saved = repo.GetById(repo.Create(p))!;
            Equal<float?>(null, saved.Value); Equal<string?>(null, saved.Description); Equal(type, saved.DeliveryType);
        }
    });
    Run("Параметри зберігають лапки, Unicode і SQL як звичайний текст", () =>
    {
        var repo = Repository(); var p = Model();
        p.From = "О’Браєн ' ; DROP TABLE Postings; --"; p.Description = "Крихке: \"скло\"";
        Equal(p.From, repo.GetById(repo.Create(p))!.From); Equal(1, repo.GetList().Count);
        Equal(2, repo.Create(Model()));
    });
    Run("Update змінює поля, підтримує NULL і зберігає CreatedAt", () =>
    {
        var repo = Repository(); var p = Model(); p.Id = repo.Create(p);
        var time = p.CreatedAt; p.CreatedAt = DateTime.UnixEpoch; p.Content = "Змінено"; p.Value = null;
        p.Description = null; p.Price = 77.5f; p.Width = 2.25f;
        Equal(1, repo.Update(p)); var saved = repo.GetById(p.Id)!;
        Equal("Змінено", saved.Content); Equal(time, saved.CreatedAt); Equal(77.5f, saved.Price);
        Equal(2.25f, saved.Width); Equal<float?>(null, saved.Value); Equal<string?>(null, saved.Description);
    });
    Run("GetList сортує за Id; Delete повертає 1, потім 0", () =>
    {
        var repo = Repository(); var first = repo.Create(Model()); var second = repo.Create(Model());
        Equal(first, repo.GetList()[0].Id); Equal(second, repo.GetList()[1].Id);
        Equal(1, repo.Delete(first)); Equal(0, repo.Delete(first)); Equal<Posting?>(null, repo.GetById(first));
        Equal(1, repo.GetList().Count);
    });
    Run("Відсутній запис: GetById=null, Update=0, Delete=0", () =>
    {
        var repo = Repository(); var p = Model(); p.Id = 999;
        Equal<Posting?>(null, repo.GetById(999)); Equal(0, repo.Update(p)); Equal(0, repo.Delete(999));
    });
    Run("Новий екземпляр репозиторію читає той самий файл", () =>
    {
        var path = Path.Combine(temp, "reopen.db"); var cs = ConnectionString(path);
        var first = new PostingRepository(cs); first.CreateDb(); var id = first.Create(Model());
        var second = new PostingRepository(cs); second.CreateDb(); Equal(id, second.GetList().Single().Id);
        using var conn = new SqliteConnection(cs); conn.Open(); using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT typeof(CreatedAt) FROM Postings;"; Equal("integer", (string)cmd.ExecuteScalar()!);
    });
    Run("Сервіс використовує SQLite, обчислює тарифи й UTC; вхід не мутує", () =>
    {
        var service = new PostingService(Repository());
        foreach (var pair in new[] { (DeliveryType.Department, 60f), (DeliveryType.Courier, 110f), (DeliveryType.ExpressCourier, 168f) })
        {
            var p = Model(); p.DeliveryType = pair.Item1; p.Id = 999; p.Price = -99;
            var saved = service.Create(p); Equal(pair.Item2, saved.Price); Equal(999, p.Id); Equal(-99f, p.Price);
            Equal(DateTimeKind.Utc, saved.CreatedAt.Kind); Equal(saved.CreatedAt, service.Find(saved.Id)!.CreatedAt);
        }
    });
    Run("Сервіс відхиляє некоректні дані до INSERT/UPDATE", () =>
    {
        var service = new PostingService(Repository()); var bad = Model(); bad.Weight = 0;
        Throws<PostingValidationException>(() => service.Create(bad));
        bad = Model(); bad.Value = -1; Throws<PostingValidationException>(() => service.Create(bad));
        bad = Model(); bad.Width = float.NaN; Throws<PostingValidationException>(() => service.Create(bad));
        Equal(0, service.GetAll().Count); var saved = service.Create(Model()); var update = Model(); update.Id = 999;
        Throws<PostingValidationException>(() => service.Update(saved.Id, update)); Equal(1, service.GetAll().Count);
    });
    Run("Сервіс оновлює і видаляє через репозиторій", () =>
    {
        var service = new PostingService(Repository()); var saved = service.Create(Model());
        var changed = Model(); changed.Id = saved.Id; changed.Content = "Оновлено сервісом";
        var updated = service.Update(saved.Id, changed)!; Equal(saved.CreatedAt, updated.CreatedAt);
        Equal(changed.Content, service.Find(saved.Id)!.Content); Equal(1, service.Delete(saved.Id)); Equal(0, service.Delete(saved.Id));
        Equal<Posting?>(null, service.Update(999, Model()));
    });

    if (args.Length == 2 && args[0] == "--api-dll")
        await CheckApi(Path.GetFullPath(args[1]));
    else if (args.Length != 0) throw new ArgumentException("Використання: --api-dll <шлях до PostService.dll>");
}
finally
{
    SqliteConnection.ClearAllPools();
    // Видаляємо лише файли, створені цією перевіркою в унікальній тимчасовій папці.
    foreach (var file in Directory.GetFiles(temp)) File.Delete(file);
    Directory.Delete(temp);
}
Console.WriteLine($"\nУспішно: {passed}/{passed + failed}");
return failed == 0 ? 0 : 1;

PostingRepository Repository()
{
    var repo = new PostingRepository(ConnectionString(Path.Combine(temp, $"test-{++sequence}.db")));
    repo.CreateDb(); return repo;
}

void Run(string name, Action check)
{
    try { check(); passed++; Console.WriteLine("PASS: " + name); }
    catch (Exception error) { failed++; Console.WriteLine("FAIL: " + name + " — " + error.Message); }
}

async Task CheckApi(string dll)
{
    using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
    var baseUrl = $"http://127.0.0.1:{port}/";
    using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(10) };
    var log = new StringBuilder();
    Process Start()
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = temp, UseShellExecute = false, CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add(dll); start.ArgumentList.Add("--urls"); start.ArgumentList.Add(baseUrl);
        var process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (log) log.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (log) log.AppendLine(e.Data); };
        process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine(); return process;
    }
    static void Stop(Process p) { if (!p.HasExited) { p.Kill(); p.WaitForExit(); } p.Dispose(); }
    async Task Ready(Process p)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            if (p.HasExited) throw new Exception("API завершився: " + log);
            try { using var r = await http.GetAsync(""); if (r.IsSuccessStatusCode) return; }
            catch (HttpRequestException) { }
            await Task.Delay(100);
        }
        throw new Exception("API не запустився: " + log);
    }

    Process? api = null;
    try
    {
        api = Start(); await Ready(api);
        Run("HTTP: автоматична міграція створила файл PostingDb.db", () => Equal(true, File.Exists(Path.Combine(temp, "PostingDb.db"))));
        var ids = new List<int>(); DateTime createdAt = default;
        foreach (var delivery in new[] { "Department", "Courier", "ExpressCourier" })
        {
            using var r = await http.PostAsJsonAsync("postings", new { from = "Київ", to = "Львів", content = "Посилка ' ; --", deliveryType = delivery, weight = 2, width = 20.5, height = 15.25, depth = 2.5, value = (float?)null, description = (string?)null });
            Equal(HttpStatusCode.Created, r.StatusCode);
            using var body = JsonDocument.Parse(await r.Content.ReadAsStringAsync()); var p = body.RootElement;
            ids.Add(p.GetProperty("id").GetInt32()); createdAt = ids.Count == 1 ? p.GetProperty("createdAt").GetDateTime() : createdAt;
            Equal("/postings/" + ids[^1], r.Headers.Location!.ToString());
            Equal(JsonValueKind.Null, p.GetProperty("value").ValueKind); Equal(JsonValueKind.Null, p.GetProperty("description").ValueKind);
            Equal(delivery == "Department" ? 60f : delivery == "Courier" ? 110f : 168f, p.GetProperty("price").GetSingle());
        }
        Run("HTTP POST: три записи, Id/Location, тарифи, дробові розміри та NULL", () => Equal(3, ids.Distinct().Count()));
        Stop(api); api = null;
        using (var conn = new SqliteConnection(ConnectionString(Path.Combine(temp, "PostingDb.db"))))
        {
            conn.Open(); using var cmd = conn.CreateCommand(); cmd.CommandText = "SELECT COUNT(*) FROM Postings;";
            Run("Після зупинки процесу у файлі SQLite залишилося 3 записи", () => Equal(3L, (long)cmd.ExecuteScalar()!));
        }
        api = Start(); await Ready(api);
        var list = await http.GetFromJsonAsync<JsonElement>("postings");
        Run("Після перезапуску GET повертає ті самі Id і дані", () =>
        {
            Equal(3, list.GetArrayLength()); Equal(ids[0], list[0].GetProperty("id").GetInt32());
            Equal(createdAt, list[0].GetProperty("createdAt").GetDateTime()); Equal(20.5f, list[0].GetProperty("width").GetSingle());
        });
        using (var r = await http.PutAsJsonAsync($"postings/{ids[0]}", new { id = ids[0], from = "Київ", to = "Одеса", content = "Оновлена посилка", deliveryType = "Courier", weight = 3.5, width = 1.5, height = 2.5, depth = 3.5, value = 100.5, price = 77.5, description = "Крихке" }))
        {
            Equal(HttpStatusCode.OK, r.StatusCode);
        }
        var updated = await http.GetFromJsonAsync<JsonElement>($"postings/{ids[0]}");
        Run("HTTP PUT/GET: зміни збережені, CreatedAt незмінний", () =>
        {
            Equal("Оновлена посилка", updated.GetProperty("content").GetString()); Equal(77.5f, updated.GetProperty("price").GetSingle());
            Equal("Крихке", updated.GetProperty("description").GetString()); Equal(createdAt, updated.GetProperty("createdAt").GetDateTime());
        });
        using (var bad = await http.PostAsJsonAsync("postings", new { from = "A", to = "B", content = "Bad", weight = 0, width = 1, height = 1, depth = 1 }))
            Run("HTTP: некоректна вага повертає 400", () => Equal(HttpStatusCode.BadRequest, bad.StatusCode));
        using (var deleted = await http.DeleteAsync($"postings/{ids[0]}"))
            Run("HTTP DELETE: 204 без тіла", () => { Equal(HttpStatusCode.NoContent, deleted.StatusCode); Equal(0L, deleted.Content.Headers.ContentLength ?? 0L); });
        Stop(api); api = null; api = Start(); await Ready(api);
        using var missing = await http.GetAsync($"postings/{ids[0]}");
        using var missingDelete = await http.DeleteAsync($"postings/{ids[0]}");
        var survivors = await http.GetFromJsonAsync<JsonElement>("postings");
        Run("Видалення збережене після другого перезапуску; GET/DELETE=404", () =>
        {
            Equal(HttpStatusCode.NotFound, missing.StatusCode); Equal(HttpStatusCode.NotFound, missingDelete.StatusCode);
            Equal(2, survivors.GetArrayLength());
        });
    }
    catch (Exception e) { failed++; Console.WriteLine("FAIL: HTTP lifecycle — " + e); }
    finally { if (api is not null) Stop(api); }
}

static string ConnectionString(string path) => new SqliteConnectionStringBuilder { DataSource = path }.ToString();
static Posting Model() => new() { From = "Київ", To = "Львів", Content = "Книги", DeliveryType = DeliveryType.Department, Weight = 2, Width = 20.5f, Height = 15.25f, Depth = 2.5f, Value = 100.5f, Price = 60, Description = "Не згинати", CreatedAt = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc) };
static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Очікується {expected}, отримано {actual}"); }
static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Очікувався " + typeof(T).Name); }
