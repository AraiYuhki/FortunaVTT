using Fortuna.Domain;
using Fortuna.Domain.Rooms;
using Fortuna.Domain.Visibility;
using Fortuna.Server.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;

namespace Fortuna.Server.Realtime;

/// <summary>
/// クライアントは Command（メソッド呼び出し）だけを送り、状態の変化は <c>ViewEvent</c> で受け取る（ADR-002 §1）。
/// ユーザーは認証済みクレームから決める。引数でユーザー ID を受け取るメソッドは作らない。
/// </summary>
[Authorize]
public sealed class RoomHub(RoomRegistry rooms, UserManager<ApplicationUser> users) : Hub
{
    public const int MaxContentLength = 16_000;
    private const string RoomKey = "room";

    private UserId CurrentUser => Context.User!.GetUserId();

    private RoomHost? CurrentRoom => Context.Items.TryGetValue(RoomKey, out var h) ? h as RoomHost : null;

    /// <summary>入室する。参加者でなければ失敗する。サーバー管理者のみ未参加でも閲覧できる。</summary>
    public async Task<EnterResult> EnterRoom(Guid roomId)
    {
        await LeaveCurrentRoomAsync();

        // 管理者判定は毎回 DB から引く。クッキー内のクレームや、クライアントの申告は信用しない。
        var account = await users.FindByIdAsync(CurrentUser.ToString());
        if (account is null)
        {
            return new EnterResult(false, nameof(RoomError.NotFound));
        }

        if (!rooms.TryGet(new RoomId(roomId), out var host))
        {
            // 部屋が無い場合と、入れない場合を区別しない（存在の漏洩を防ぐ）。
            return new EnterResult(false, nameof(RoomError.NotFound));
        }

        var result = await host.EnterAsync(Context.ConnectionId, CurrentUser, account.IsServerAdmin);
        if (!result.Ok)
        {
            return new EnterResult(false, nameof(RoomError.NotFound));
        }

        Context.Items[RoomKey] = host;
        return result;
    }

    public Task<CommandAck> AddObject(Guid commandId, AudienceDto audience, string content)
    {
        if (audience.ToDomain() is not { } domainAudience || !IsValidContent(content))
        {
            return Task.FromResult(CommandAck.Bad(commandId));
        }

        var actor = CurrentUser;
        return Run(commandId, room => room.AddObject(actor, domainAudience, content));
    }

    public Task<CommandAck> UpdateContent(Guid commandId, Guid objectId, string content)
    {
        if (!IsValidContent(content))
        {
            return Task.FromResult(CommandAck.Bad(commandId));
        }

        var actor = CurrentUser;
        return Run(commandId, room => room.UpdateContent(actor, new ObjectId(objectId), content));
    }

    public Task<CommandAck> SetAudience(Guid commandId, Guid objectId, AudienceDto audience)
    {
        if (audience.ToDomain() is not { } domainAudience)
        {
            return Task.FromResult(CommandAck.Bad(commandId));
        }

        var actor = CurrentUser;
        return Run(commandId, room => room.SetAudience(actor, new ObjectId(objectId), domainAudience));
    }

    public Task<CommandAck> RemoveObject(Guid commandId, Guid objectId)
    {
        var actor = CurrentUser;
        return Run(commandId, room => room.RemoveObject(actor, new ObjectId(objectId)));
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await LeaveCurrentRoomAsync();
        await base.OnDisconnectedAsync(exception);
    }

    private async Task<CommandAck> Run(Guid commandId, Func<Room, CommandResult> command)
    {
        if (CurrentRoom is not { } host)
        {
            // 入室前の操作。部屋の存在には触れない。
            return new CommandAck(commandId, false, nameof(RoomError.NotAMember));
        }

        return CommandAck.From(commandId, await host.ExecuteAsync(command));
    }

    private async Task LeaveCurrentRoomAsync()
    {
        if (CurrentRoom is { } host)
        {
            Context.Items.Remove(RoomKey);
            await host.DetachAsync(Context.ConnectionId, CurrentUser);
        }
    }

    private static bool IsValidContent(string? content) =>
        content is not null && content.Length <= MaxContentLength;
}
