using System.Net;
using System.Net.Http.Json;
using Fortuna.Server.Accounts;
using Fortuna.Server.Realtime;
using Fortuna.Server.Tests.Support;
using Microsoft.AspNetCore.SignalR.Client;

namespace Fortuna.Server.Tests;

public class AccountAndLobbyTests(FortunaFactory factory) : IClassFixture<FortunaFactory>
{
    [Fact]
    public async Task Api_requires_authentication()
    {
        using var anonymous = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/rooms")).StatusCode);
    }

    [Fact]
    public async Task Hub_rejects_unauthenticated_connections()
    {
        await using var connection = HubClient.Build(factory, user: null);

        await Assert.ThrowsAnyAsync<HttpRequestException>(() => connection.StartAsync());
    }

    [Fact]
    public async Task Registered_user_is_not_a_server_admin_and_gets_a_default_display_name()
    {
        var user = await TestUser.RegisterAsync(factory, "carol");

        var me = await user.Http.GetFromJsonAsync<AccountEndpoints.MeResponse>("/api/me");

        Assert.False(me!.IsServerAdmin);
        Assert.StartsWith("carol-", me.DisplayName);
    }

    [Fact]
    public async Task Display_name_can_be_changed_but_admin_flag_cannot()
    {
        var user = await TestUser.RegisterAsync(factory);

        var response = await user.Http.PutAsJsonAsync("/api/me", new { displayName = "ダイス職人", isServerAdmin = true });
        var me = await response.Content.ReadFromJsonAsync<AccountEndpoints.MeResponse>();

        Assert.Equal("ダイス職人", me!.DisplayName);
        Assert.False(me.IsServerAdmin);
    }

    [Fact]
    public async Task Bootstrap_admin_is_a_server_admin()
    {
        var admin = await TestUser.LoginAsync(factory, FortunaFactory.AdminEmail, FortunaFactory.AdminPassword);

        var me = await admin.Http.GetFromJsonAsync<AccountEndpoints.MeResponse>("/api/me");

        Assert.True(me!.IsServerAdmin);
    }

    [Fact]
    public async Task Create_join_and_list_rooms()
    {
        var alice = await TestUser.RegisterAsync(factory);
        var bob = await TestUser.RegisterAsync(factory);
        var outsider = await TestUser.RegisterAsync(factory);

        var room = await Rooms.CreateAsync(alice, "卓A");
        var joined = await Rooms.JoinAsync(bob, room.InviteCode!);

        Assert.Equal("Owner", room.Role);
        Assert.Equal("Member", joined.Role);
        Assert.Null(joined.InviteCode); // 招待コードは Owner にだけ見せる。

        Assert.Contains(await Rooms.ListAsync(alice), r => r.Id == room.Id && r.InviteCode is not null);
        Assert.Contains(await Rooms.ListAsync(bob), r => r.Id == room.Id);
        Assert.DoesNotContain(await Rooms.ListAsync(outsider), r => r.Id == room.Id);
    }

    [Fact]
    public async Task Invalid_invite_code_is_not_found()
    {
        var bob = await TestUser.RegisterAsync(factory);

        var response = await bob.Http.PostAsJsonAsync("/api/rooms/join", new { inviteCode = "nope" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Room_name_is_validated()
    {
        var alice = await TestUser.RegisterAsync(factory);

        Assert.Equal(HttpStatusCode.BadRequest, (await alice.Http.PostAsJsonAsync("/api/rooms", new { name = "  " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await alice.Http.PostAsJsonAsync("/api/rooms", new { name = new string('a', 61) })).StatusCode);
    }

    [Fact]
    public async Task Non_members_cannot_learn_that_a_room_exists()
    {
        var alice = await TestUser.RegisterAsync(factory);
        var outsider = await TestUser.RegisterAsync(factory);
        var room = await Rooms.CreateAsync(alice, "秘密の卓");

        var members = await outsider.Http.GetAsync($"/api/rooms/{room.Id}/members");
        var kick = await outsider.Http.PostAsJsonAsync($"/api/rooms/{room.Id}/kick", new { userId = alice.Id });
        var missing = await outsider.Http.GetAsync($"/api/rooms/{Guid.NewGuid()}/members");

        Assert.Equal(HttpStatusCode.NotFound, members.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, kick.StatusCode);
        Assert.Equal(missing.StatusCode, members.StatusCode); // 存在しない部屋と区別できない。
    }

    [Fact]
    public async Task Members_endpoint_lists_display_names_for_members()
    {
        var alice = await TestUser.RegisterAsync(factory);
        var bob = await TestUser.RegisterAsync(factory);
        var room = await Rooms.CreateAsync(alice, "卓");
        await Rooms.JoinAsync(bob, room.InviteCode!);

        var members = await alice.Http.GetFromJsonAsync<List<LobbyEndpoints.MemberSummary>>($"/api/rooms/{room.Id}/members");

        Assert.Equal(2, members!.Count);
        Assert.Contains(members, m => m.UserId == bob.Id && m.Role == "Member" && m.IsActive);
    }

    [Fact]
    public async Task Owner_can_kick_but_members_cannot()
    {
        var alice = await TestUser.RegisterAsync(factory);
        var bob = await TestUser.RegisterAsync(factory);
        var room = await Rooms.CreateAsync(alice, "卓");
        await Rooms.JoinAsync(bob, room.InviteCode!);

        var byMember = await bob.Http.PostAsJsonAsync($"/api/rooms/{room.Id}/kick", new { userId = alice.Id });
        var byOwner = await alice.Http.PostAsJsonAsync($"/api/rooms/{room.Id}/kick", new { userId = bob.Id });

        Assert.Equal(HttpStatusCode.Forbidden, byMember.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, byOwner.StatusCode);
        Assert.DoesNotContain(await Rooms.ListAsync(bob), r => r.Id == room.Id);
    }

    [Fact]
    public async Task Owner_must_transfer_before_leaving()
    {
        var alice = await TestUser.RegisterAsync(factory);
        var bob = await TestUser.RegisterAsync(factory);
        var room = await Rooms.CreateAsync(alice, "卓");
        await Rooms.JoinAsync(bob, room.InviteCode!);

        var leave = await alice.Http.PostAsync($"/api/rooms/{room.Id}/leave", null);
        Assert.Equal(HttpStatusCode.Conflict, leave.StatusCode);

        var transfer = await alice.Http.PostAsJsonAsync($"/api/rooms/{room.Id}/transfer", new { userId = bob.Id });
        Assert.Equal(HttpStatusCode.NoContent, transfer.StatusCode);

        var leaveAgain = await alice.Http.PostAsync($"/api/rooms/{room.Id}/leave", null);
        Assert.Equal(HttpStatusCode.NoContent, leaveAgain.StatusCode);
    }
}
