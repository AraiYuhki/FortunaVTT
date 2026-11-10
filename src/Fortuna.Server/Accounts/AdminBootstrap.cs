using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Fortuna.Server.Accounts;

/// <summary>
/// サーバー管理者が1人もいない時に限り、設定から初期管理者を作る。
/// 既存アカウントを昇格させることはしない（メール未確認のまま登録した他人が管理者になる事故を避ける）。
/// 設定は <c>Fortuna:BootstrapAdmin:Email</c> / <c>Fortuna:BootstrapAdmin:Password</c>（環境変数やユーザーシークレットで渡す）。
/// </summary>
public static class AdminBootstrap
{
    public static async Task EnsureAsync(IServiceProvider services, IConfiguration config, ILogger logger)
    {
        var email = config["Fortuna:BootstrapAdmin:Email"];
        var password = config["Fortuna:BootstrapAdmin:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
        {
            return;
        }

        using var scope = services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        if (await users.Users.AnyAsync(u => u.IsServerAdmin))
        {
            return;
        }

        if (await users.FindByEmailAsync(email) is not null)
        {
            logger.LogWarning(
                "BootstrapAdmin のメールは既に登録済みのため、管理者を作成しませんでした。別のメールを指定してください。");
            return;
        }

        var admin = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = "admin",
            IsServerAdmin = true,
        };

        var result = await users.CreateAsync(admin, password);
        if (!result.Succeeded)
        {
            logger.LogError("初期管理者の作成に失敗: {Errors}", string.Join(", ", result.Errors.Select(e => e.Code)));
            return;
        }

        logger.LogInformation("初期サーバー管理者を作成しました。");
    }
}
