using Fortuna.Domain;
using Fortuna.Domain.Rooms;
using Microsoft.AspNetCore.SignalR;

namespace Fortuna.Server.Realtime;

/// <summary>
/// Domain が決めた <see cref="Delivery"/> を SignalR で送る。
/// 受信者ごとに配信するため「ルーム×ユーザー」単位のグループを使う（ルーム全体へのグループ配信は使わない。ADR-002 §3）。
/// 同じユーザーが別ルームを開いていても、イベントが混ざらない。
/// </summary>
public sealed class RoomDispatcher(IHubContext<RoomHub> hub)
{
    public const string ViewEventMethod = "ViewEvent";
    public const string RemovedFromRoomMethod = "RemovedFromRoom";

    private static string Group(RoomId room, UserId user) => $"room:{room}:{user}";

    public Task AddToGroupAsync(string connectionId, RoomId room, UserId user) =>
        hub.Groups.AddToGroupAsync(connectionId, Group(room, user));

    public Task RemoveFromGroupAsync(string connectionId, RoomId room, UserId user) =>
        hub.Groups.RemoveFromGroupAsync(connectionId, Group(room, user));

    public async Task SendAsync(RoomId room, IEnumerable<Delivery> deliveries)
    {
        foreach (var d in deliveries)
        {
            await hub.Clients.Group(Group(room, d.Recipient))
                .SendAsync(ViewEventMethod, ViewEventDto.From(d));
        }
    }

    /// <summary>入室時のスナップショットなど、1接続だけに送る。</summary>
    public async Task SendToConnectionAsync(string connectionId, IEnumerable<Delivery> deliveries)
    {
        foreach (var d in deliveries)
        {
            await hub.Clients.Client(connectionId).SendAsync(ViewEventMethod, ViewEventDto.From(d));
        }
    }

    public Task NotifyRemovedAsync(string connectionId, RoomId room) =>
        hub.Clients.Client(connectionId).SendAsync(RemovedFromRoomMethod, room.Value);
}
