using System.Net.Http.Json;
using Fortuna.Server.Realtime;

namespace Fortuna.Server.Tests.Support;

/// <summary>REST 経由でルームを操作する、テスト用の小さな補助。</summary>
public static class Rooms
{
    public static async Task<LobbyEndpoints.RoomSummary> CreateAsync(TestUser owner, string name)
    {
        var response = await owner.Http.PostAsJsonAsync("/api/rooms", new { name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LobbyEndpoints.RoomSummary>())!;
    }

    public static async Task<LobbyEndpoints.RoomSummary> JoinAsync(TestUser user, string inviteCode)
    {
        var response = await user.Http.PostAsJsonAsync("/api/rooms/join", new { inviteCode });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LobbyEndpoints.RoomSummary>())!;
    }

    public static async Task<List<LobbyEndpoints.RoomSummary>> ListAsync(TestUser user) =>
        (await user.Http.GetFromJsonAsync<List<LobbyEndpoints.RoomSummary>>("/api/rooms"))!;
}
