using Fortuna.Server.Accounts;
using Fortuna.Server.Realtime;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default") ?? "Data Source=data/fortuna.db";
EnsureDatabaseDirectory(connectionString);

builder.Services.AddDbContext<FortunaDbContext>(o => o.UseSqlite(connectionString));

// ローカルアカウント（メール + パスワード）。Google 連携は AddAuthentication().AddGoogle(...) で後付けできる。
builder.Services
    .AddIdentityApiEndpoints<ApplicationUser>(o =>
    {
        o.User.RequireUniqueEmail = true;
        o.Password.RequiredLength = 10;
    })
    .AddEntityFrameworkStores<FortunaDbContext>();

builder.Services.AddAuthorization();
builder.Services.AddSignalR();

builder.Services.AddSingleton<RoomDispatcher>();
builder.Services.AddSingleton<RoomRegistry>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    // 暫定: マイグレーションは未導入。スキーマ変更が入る前に EF Migrations へ移行する。
    scope.ServiceProvider.GetRequiredService<FortunaDbContext>().Database.EnsureCreated();
}

await AdminBootstrap.EnsureAsync(app.Services, app.Configuration, app.Logger);

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => "FortunaVTT");
app.MapGroup("/api/auth").MapIdentityApi<ApplicationUser>();
app.MapAccountEndpoints();
app.MapLobbyEndpoints();
app.MapHub<RoomHub>("/hubs/room");

app.Run();

static void EnsureDatabaseDirectory(string connectionString)
{
    var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
    if (string.IsNullOrEmpty(dataSource) || dataSource == ":memory:")
    {
        return;
    }

    if (Path.GetDirectoryName(Path.GetFullPath(dataSource)) is { Length: > 0 } dir)
    {
        Directory.CreateDirectory(dir);
    }
}

// 統合テスト（WebApplicationFactory）から参照するため公開する。
public partial class Program;
