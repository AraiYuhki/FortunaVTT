using System.Threading.Channels;
using Fortuna.Domain;
using Fortuna.Domain.Rooms;

namespace Fortuna.Server.Realtime;

public sealed record RoomInfo(RoomId Id, string Name, string InviteCode);

/// <summary>
/// ルーム1つ分の実行単位。状態変更は、ルームごとに1本の直列キューで処理する（単一ライター。ADR-002 §2）。
/// 状態変更と配信を同じ直列区間で行うため、同じ受信者へのイベントの順序は入れ替わらない。
/// </summary>
public sealed class RoomHost : IDisposable
{
    private readonly Channel<Func<Task>> _queue =
        Channel.CreateUnbounded<Func<Task>>(new UnboundedChannelOptions { SingleReader = true });

    private readonly Room _room;
    private readonly RoomDispatcher _dispatcher;

    // 以下はキュー内（単一スレッド的）からのみ触る。
    private readonly Dictionary<UserId, HashSet<string>> _connections = [];

    public RoomHost(RoomInfo info, UserId owner, RoomDispatcher dispatcher)
    {
        Info = info;
        _dispatcher = dispatcher;
        _room = Room.Create(info.Id, owner);
        _ = Task.Run(RunLoopAsync);
    }

    public RoomInfo Info { get; }

    /// <summary>ルームの状態を読む。キュー内で実行されるため、書き込みと競合しない。</summary>
    public Task<T> QueryAsync<T>(Func<Room, T> read) => EnqueueAsync(() => Task.FromResult(read(_room)));

    /// <summary>
    /// 状態を変更し、成功したら受信者ごとの配信を行う。
    /// <paramref name="evictIfDisconnected"/> のユーザーが変更後に切断扱いなら（退室・追放）、その接続も外す。
    /// </summary>
    public Task<CommandResult> ExecuteAsync(Func<Room, CommandResult> command, UserId? evictIfDisconnected = null) =>
        EnqueueAsync(async () =>
        {
            var result = command(_room);
            if (!result.Succeeded)
            {
                return result;
            }

            await _dispatcher.SendAsync(Info.Id, result.Deliveries);

            if (evictIfDisconnected is { } user && !_room.IsConnected(user))
            {
                await EvictAsync(user);
            }

            return result;
        });

    /// <summary>接続を入室させ、見えているものをその接続だけにスナップショットとして送る。</summary>
    public Task<EnterResult> EnterAsync(string connectionId, UserId user, bool isServerAdmin) =>
        EnqueueAsync(async () =>
        {
            var result = _room.Connect(user, isServerAdmin);
            if (!result.Succeeded)
            {
                return new EnterResult(false, result.Error.ToString());
            }

            if (!_connections.TryGetValue(user, out var ids))
            {
                _connections[user] = ids = [];
            }

            ids.Add(connectionId);

            // グループ参加 → スナップショット送信を同じ直列区間で行うので、間に他のイベントは割り込まない。
            await _dispatcher.AddToGroupAsync(connectionId, Info.Id, user);
            await _dispatcher.SendToConnectionAsync(connectionId, result.Deliveries);
            return new EnterResult(true, null);
        });

    /// <summary>接続が切れた。そのユーザーの接続が全て無くなったらドメイン側も切断する。</summary>
    public Task DetachAsync(string connectionId, UserId user) =>
        EnqueueAsync(() =>
        {
            if (_connections.TryGetValue(user, out var ids))
            {
                ids.Remove(connectionId);
                if (ids.Count == 0)
                {
                    _connections.Remove(user);
                    _room.Disconnect(user);
                }
            }

            return Task.FromResult(0);
        });

    private async Task EvictAsync(UserId user)
    {
        if (!_connections.Remove(user, out var ids))
        {
            return;
        }

        foreach (var id in ids)
        {
            await _dispatcher.RemoveFromGroupAsync(id, Info.Id, user);
            await _dispatcher.NotifyRemovedAsync(id, Info.Id);
        }
    }

    private Task<T> EnqueueAsync<T>(Func<Task<T>> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        var written = _queue.Writer.TryWrite(async () =>
        {
            try
            {
                tcs.SetResult(await work());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        if (!written)
        {
            tcs.SetException(new ObjectDisposedException(nameof(RoomHost)));
        }

        return tcs.Task;
    }

    private async Task RunLoopAsync()
    {
        await foreach (var job in _queue.Reader.ReadAllAsync())
        {
            await job(); // job は例外を TaskCompletionSource に流すため、ここでは投げない。
        }
    }

    public void Dispose() => _queue.Writer.TryComplete();
}
