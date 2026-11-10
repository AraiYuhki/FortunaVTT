using System.Collections.Concurrent;
using System.Security.Cryptography;
using Fortuna.Domain;
using Microsoft.AspNetCore.WebUtilities;

namespace Fortuna.Server.Realtime;

/// <summary>稼働中のルームの一覧。現状はメモリのみ（永続化は後続）。</summary>
public sealed class RoomRegistry(RoomDispatcher dispatcher) : IDisposable
{
    private readonly ConcurrentDictionary<RoomId, RoomHost> _rooms = new();
    private readonly ConcurrentDictionary<string, RoomId> _invites = new();

    public IEnumerable<RoomHost> All => _rooms.Values;

    public RoomHost Create(string name, UserId owner)
    {
        // 招待コードは参加の鍵になるため、推測できない 128bit の乱数にする。
        var invite = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(16));
        var info = new RoomInfo(RoomId.New(), name, invite);

        var host = new RoomHost(info, owner, dispatcher);
        _rooms[info.Id] = host;
        _invites[invite] = info.Id;
        return host;
    }

    public bool TryGet(RoomId id, out RoomHost host) => _rooms.TryGetValue(id, out host!);

    public bool TryGetByInvite(string code, out RoomHost host)
    {
        host = null!;
        return _invites.TryGetValue(code, out var id) && _rooms.TryGetValue(id, out host!);
    }

    public void Dispose()
    {
        foreach (var host in _rooms.Values)
        {
            host.Dispose();
        }
    }
}
