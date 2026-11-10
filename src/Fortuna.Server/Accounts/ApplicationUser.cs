using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace Fortuna.Server.Accounts;

/// <summary>
/// アカウント。ID（GUID）は認証手段に依存しない。Google 連携は外部ログイン（UserLogin）として後付けできる（ADR-003 §2）。
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    public const int DisplayNameMaxLength = 60;

    [MaxLength(DisplayNameMaxLength)]
    public string? DisplayName { get; set; }

    /// <summary>
    /// サーバー管理者か。全ルームの全データを閲覧できる唯一の特権（閲覧のみ。ADR-003 §3）。
    /// サーバー側でのみ設定する。登録 API・クライアント入力からは変更できない。
    /// </summary>
    public bool IsServerAdmin { get; set; }

    public string EffectiveDisplayName =>
        !string.IsNullOrWhiteSpace(DisplayName) ? DisplayName : Email?.Split('@')[0] ?? "user";
}
