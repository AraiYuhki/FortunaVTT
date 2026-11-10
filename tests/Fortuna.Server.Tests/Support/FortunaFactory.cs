using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace Fortuna.Server.Tests.Support;

/// <summary>テストごとに独立した SQLite ファイルと、初期サーバー管理者を持つサーバー。</summary>
public sealed class FortunaFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@example.test";
    public const string AdminPassword = "Admin-Passw0rd!";
    public const string UserPassword = "User-Passw0rd!";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"fortuna-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={_dbPath}");
        builder.UseSetting("Fortuna:BootstrapAdmin:Email", AdminEmail);
        builder.UseSetting("Fortuna:BootstrapAdmin:Password", AdminPassword);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            {
                try { File.Delete(path); } catch (IOException) { /* 後始末の失敗でテストを落とさない */ }
            }
        }
    }
}
