using System.Security.Claims;
using Fortuna.Domain;
using Fortuna.Domain.Rooms;
using Fortuna.Server.Accounts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Fortuna.Server.Realtime;

/// <summary>ロビー: ルームの作成・参加・一覧と、メンバー管理（ADR-003 §1）。</summary>
public static class LobbyEndpoints
{
    public const int MaxRoomNameLength = 60;

    public sealed record CreateRoomRequest(string? Name);
    public sealed record JoinRoomRequest(string? InviteCode);
    public sealed record TargetUserRequest(Guid UserId);

    public sealed record RoomSummary(Guid Id, string Name, string Role, string? InviteCode);
    public sealed record MemberSummary(Guid UserId, string DisplayName, string Role, bool IsActive);

    public static IEndpointRouteBuilder MapLobbyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/rooms").RequireAuthorization();

        group.MapPost("/", (CreateRoomRequest request, ClaimsPrincipal principal, RoomRegistry rooms) =>
        {
            var name = request.Name?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > MaxRoomNameLength)
            {
                return Results.BadRequest(new { error = "InvalidName" });
            }

            var host = rooms.Create(name, principal.GetUserId());
            return Results.Ok(new RoomSummary(host.Info.Id.Value, host.Info.Name, nameof(RoomRole.Owner), host.Info.InviteCode));
        });

        // 自分が参加中のルームだけを返す。
        group.MapGet("/", async (ClaimsPrincipal principal, RoomRegistry rooms) =>
        {
            var me = principal.GetUserId();
            var result = new List<RoomSummary>();

            foreach (var host in rooms.All)
            {
                var role = await host.QueryAsync(r =>
                    r.Members.TryGet(me, out var m) && m.IsActive ? m.Role : (RoomRole?)null);

                if (role is { } found)
                {
                    result.Add(Summarize(host, found));
                }
            }

            return Results.Ok(result);
        });

        group.MapPost("/join", async (JoinRoomRequest request, ClaimsPrincipal principal, RoomRegistry rooms) =>
        {
            // 招待コードが無効な場合のレスポンスは、コードの形式に関わらず同一にする。
            if (string.IsNullOrEmpty(request.InviteCode) || !rooms.TryGetByInvite(request.InviteCode, out var host))
            {
                return Results.NotFound();
            }

            var me = principal.GetUserId();
            await host.ExecuteAsync(r => r.Join(me));
            var role = await host.QueryAsync(r => r.Members.TryGet(me, out var m) ? m.Role : RoomRole.Member);
            return Results.Ok(Summarize(host, role));
        });

        group.MapGet("/{roomId:guid}/members", async (Guid roomId, ClaimsPrincipal principal,
            RoomRegistry rooms, UserManager<ApplicationUser> users) =>
        {
            var me = principal.GetUserId();
            if (!rooms.TryGet(new RoomId(roomId), out var host))
            {
                return Results.NotFound();
            }

            var members = await host.QueryAsync(r => r.Members.All.ToList());
            if (!members.Any(m => m.Id == me && m.IsActive))
            {
                return Results.NotFound(); // 非参加者には部屋の存在を教えない。
            }

            var ids = members.Select(m => m.Id.Value).ToList();
            var names = await users.Users
                .Where(u => ids.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.EffectiveDisplayName);

            return Results.Ok(members.Select(m => new MemberSummary(
                m.Id.Value, names.GetValueOrDefault(m.Id.Value, "?"), m.Role.ToString(), m.IsActive)));
        });

        group.MapPost("/{roomId:guid}/leave", (Guid roomId, ClaimsPrincipal principal, RoomRegistry rooms) =>
        {
            var me = principal.GetUserId();
            return Command(rooms, roomId, r => r.Leave(me), evict: me);
        });

        group.MapPost("/{roomId:guid}/kick", (Guid roomId, TargetUserRequest request,
            ClaimsPrincipal principal, RoomRegistry rooms) =>
        {
            var me = principal.GetUserId();
            var target = new UserId(request.UserId);
            return Command(rooms, roomId, r => r.Kick(me, target), evict: target);
        });

        group.MapPost("/{roomId:guid}/transfer", (Guid roomId, TargetUserRequest request,
            ClaimsPrincipal principal, RoomRegistry rooms) =>
        {
            var me = principal.GetUserId();
            var target = new UserId(request.UserId);
            return Command(rooms, roomId, r => r.TransferOwnership(me, target));
        });

        return app;
    }

    private static RoomSummary Summarize(RoomHost host, RoomRole role) =>
        new(host.Info.Id.Value, host.Info.Name, role.ToString(),
            role == RoomRole.Owner ? host.Info.InviteCode : null);

    private static async Task<IResult> Command(
        RoomRegistry rooms, Guid roomId, Func<Room, CommandResult> command, UserId? evict = null)
    {
        if (!rooms.TryGet(new RoomId(roomId), out var host))
        {
            return Results.NotFound();
        }

        var result = await host.ExecuteAsync(command, evict);
        return result.Error switch
        {
            RoomError.None => Results.NoContent(),
            // 非参加者には部屋の存在も、権限の有無も教えない。
            RoomError.NotFound or RoomError.NotAMember => Results.NotFound(),
            RoomError.Forbidden => Results.StatusCode(StatusCodes.Status403Forbidden),
            RoomError.OwnerMustTransfer => Results.Conflict(new { error = nameof(RoomError.OwnerMustTransfer) }),
            _ => Results.BadRequest(),
        };
    }
}
