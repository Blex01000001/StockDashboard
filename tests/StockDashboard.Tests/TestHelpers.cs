using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace StockDashboard.Tests;

/// <summary>
/// 假的 HTTP 回應：不連網路，依網址回傳指定內容，並記錄收到的請求網址。
/// </summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Func<Uri, HttpResponseMessage> _respond;
    public List<Uri> Requests { get; } = new();

    public FakeHttpHandler(Func<Uri, HttpResponseMessage> respond) => _respond = respond;

    public static FakeHttpHandler Always(string body, string contentType = "application/json", HttpStatusCode status = HttpStatusCode.OK) =>
        new(_ => Response(body, contentType, status));

    public static HttpResponseMessage Response(string body, string contentType = "application/json", HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, contentType) };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        return Task.FromResult(_respond(request.RequestUri!));
    }
}

/// <summary>
/// 測試用的暫存 SQLite 檔案，Dispose 時刪除。
/// </summary>
public sealed class TempSqliteDb : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"stockdashboard-test-{Guid.NewGuid():N}.db");
    public string ConnectionString => $"Data Source={Path};Pooling=False";

    public IConfiguration Configuration => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = ConnectionString })
        .Build();

    public void Execute(string sql)
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        if (File.Exists(Path)) File.Delete(Path);
    }
}

public static class Fixtures
{
    public static string Read(string name) => File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}
