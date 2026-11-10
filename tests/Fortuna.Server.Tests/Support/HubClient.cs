using System.Threading.Channels;
using Fortuna.Server.Realtime;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace Fortuna.Server.Tests.Support;

/// <summary>Hub への接続。受け取った ViewEvent をためておき、テストから順に取り出す。</summary>
public sealed class HubClient : IAsyncDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly HubConnection _connection;
    private readonly Channel<ViewEventDto> _events = Channel.CreateUnbounded<ViewEventDto>();
    private readonly Channel<Guid> _removed = Channel.CreateUnbounded<Guid>();

    private HubClient(HubConnection connection)
    {
        _connection = connection;
        _connection.On<ViewEventDto>(RoomDispatcher.ViewEventMethod, e => _events.Writer.TryWrite(e));
        _connection.On<Guid>(RoomDispatcher.RemovedFromRoomMethod, id => _removed.Writer.TryWrite(id));
    }

    /// <param name="user">null なら未認証で接続を試みる。</param>
    public static async Task<HubClient> ConnectAsync(FortunaFactory factory, TestUser? user)
    {
        var connection = Build(factory, user);
        var client = new HubClient(connection);
        await connection.StartAsync();
        return client;
    }

    /// <summary>接続の確立自体を試す（未認証で失敗することの確認用）。</summary>
    public static HubConnection Build(FortunaFactory factory, TestUser? user) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "/hubs/room"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                if (user is not null)
                {
                    options.Headers["Cookie"] = user.CookieHeader;
                }
            })
            .Build();

    public Task<EnterResult> EnterAsync(Guid roomId) =>
        _connection.InvokeAsync<EnterResult>("EnterRoom", roomId);

    public Task<CommandAck> AddAsync(AudienceDto audience, string content) =>
        _connection.InvokeAsync<CommandAck>("AddObject", Guid.NewGuid(), audience, content);

    public Task<CommandAck> UpdateAsync(Guid objectId, string content) =>
        _connection.InvokeAsync<CommandAck>("UpdateContent", Guid.NewGuid(), objectId, content);

    public Task<CommandAck> SetAudienceAsync(Guid objectId, AudienceDto audience) =>
        _connection.InvokeAsync<CommandAck>("SetAudience", Guid.NewGuid(), objectId, audience);

    public Task<CommandAck> RemoveAsync(Guid objectId) =>
        _connection.InvokeAsync<CommandAck>("RemoveObject", Guid.NewGuid(), objectId);

    /// <summary>次のイベントを待つ。来なければテスト失敗。</summary>
    public async Task<ViewEventDto> NextAsync()
    {
        using var cts = new CancellationTokenSource(Timeout);
        try
        {
            return await _events.Reader.ReadAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("ViewEvent が届きませんでした。");
        }
    }

    public async Task<Guid> NextRemovedAsync()
    {
        using var cts = new CancellationTokenSource(Timeout);
        try
        {
            return await _removed.Reader.ReadAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("RemovedFromRoom が届きませんでした。");
        }
    }

    /// <summary>
    /// しばらく新しいイベントが来なくなるまで集める。「届かないこと」の確認に使う。
    /// 呼ぶ前に、確認したい操作より後に必ず届くイベントを発生させて、取りこぼしを避けること。
    /// </summary>
    public async Task<IReadOnlyList<ViewEventDto>> DrainAsync(TimeSpan? quiet = null)
    {
        var list = new List<ViewEventDto>();
        var wait = quiet ?? TimeSpan.FromMilliseconds(400);

        while (true)
        {
            using var cts = new CancellationTokenSource(wait);
            try
            {
                list.Add(await _events.Reader.ReadAsync(cts.Token));
            }
            catch (OperationCanceledException)
            {
                return list;
            }
        }
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
