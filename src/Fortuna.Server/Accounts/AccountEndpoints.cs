using System.Security.Claims;
using Fortuna.Domain;
using Microsoft.AspNetCore.Identity;

namespace Fortuna.Server.Accounts;

public static class AccountEndpoints
{
    public sealed record MeResponse(Guid Id, string? Email, string DisplayName, bool IsServerAdmin);

    public sealed record UpdateProfileRequest(string? DisplayName);

    /// <summary>認証済みクレームから UserId を取り出す。クライアントが名乗る ID は使わない（ADR-003 §2）。</summary>
    public static UserId GetUserId(this ClaimsPrincipal principal) =>
        new(Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("認証済みユーザーに ID クレームがありません。")));

    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/me").RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal principal, UserManager<ApplicationUser> users) =>
            await users.FindByIdAsync(principal.GetUserId().ToString()) is { } user
                ? Results.Ok(ToResponse(user))
                : Results.Unauthorized());

        group.MapPut("/", async (UpdateProfileRequest request, ClaimsPrincipal principal, UserManager<ApplicationUser> users) =>
        {
            var name = request.DisplayName?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > ApplicationUser.DisplayNameMaxLength)
            {
                return Results.BadRequest(new { error = "InvalidDisplayName" });
            }

            if (await users.FindByIdAsync(principal.GetUserId().ToString()) is not { } user)
            {
                return Results.Unauthorized();
            }

            user.DisplayName = name;
            var result = await users.UpdateAsync(user);
            return result.Succeeded ? Results.Ok(ToResponse(user)) : Results.BadRequest(new { error = "UpdateFailed" });
        });

        return app;
    }

    private static MeResponse ToResponse(ApplicationUser user) =>
        new(user.Id, user.Email, user.EffectiveDisplayName, user.IsServerAdmin);
}
