using System.Net;
using System.Net.Http.Json;
using Fortuna.Server.Realtime;
using Fortuna.Server.Tests.Support;

namespace Fortuna.Server.Tests;

/// <summary>HTTP と SignalR を通しても秘匿が保たれることを確認する。</summary>
public class RoomHubTests(FortunaFactory factory) : IClassFixture<FortunaFactory>
{
    private static readonly AudienceDto Private = new(AudienceDto.Owner);
    private static readonly AudienceDto Everyone = new(AudienceDto.RoomKind);
    private static AudienceDto Only(params Guid[] ids) => new(AudienceDto.MembersKind, ids);

    private sealed record Table(
        TestUser Alice, TestUser Bob, LobbyEndpoints.RoomSummary Room, HubClient AliceHub, HubClient BobHub);

    /// <summary>alice（Owner）と bob（Member）が入室した状態の卓。</summary>
    private async Task<Table> SeatAsync()
    {
        var alice = await TestUser.RegisterAsync(factory, "alice");
        var bob = await TestUser.RegisterAsync(factory, "bob");
        var room = await Rooms.CreateAsync(alice, "卓");
        await Rooms.JoinAsync(bob, room.InviteCode!);

        var aliceHub = await HubClient.ConnectAsync(factory, alice);
        var bobHub = await HubClient.ConnectAsync(factory, bob);
        Assert.True((await aliceHub.EnterAsync(room.Id)).Ok);
        Assert.True((await bobHub.EnterAsync(room.Id)).Ok);

        return new Table(alice, bob, room, aliceHub, bobHub);
    }

    [Fact]
    public async Task Private_object_never_reaches_other_members_but_public_one_does()
    {
        var t = await SeatAsync();

        var secret = await t.AliceHub.AddAsync(Private, "secret");
        var pub = await t.AliceHub.AddAsync(Everyone, "public");
        Assert.True(secret.Ok && pub.Ok);

        // 後から出した公開物が届いていれば、先の非公開物が「遅れているだけ」ではないと言える。
        var bobEvents = await t.BobHub.DrainAsync();

        var only = Assert.Single(bobEvents);
        Assert.Equal("appeared", only.Type);
        Assert.Equal("public", only.Object!.Content);
        Assert.DoesNotContain(bobEvents, e => e.Object?.Content == "secret");

        Assert.Equal(2, (await t.AliceHub.DrainAsync()).Count);
    }

    [Fact]
    public async Task Audience_breakdown_is_only_sent_to_the_owner()
    {
        var t = await SeatAsync();

        await t.AliceHub.AddAsync(Only(t.Bob.Id), "for bob");

        var forBob = await t.BobHub.NextAsync();
        var forAlice = await t.AliceHub.NextAsync();

        Assert.Null(forBob.Object!.Audience);
        Assert.Equal(AudienceDto.MembersKind, forAlice.Object!.Audience!.Kind);
    }

    [Fact]
    public async Task Changing_the_audience_reveals_and_hides_without_leaking_content_on_hide()
    {
        var t = await SeatAsync();
        var created = await t.AliceHub.AddAsync(Private, "later");
        var id = created.CreatedId!.Value;
        await t.AliceHub.NextAsync();

        await t.AliceHub.SetAudienceAsync(id, Only(t.Bob.Id));
        var appeared = await t.BobHub.NextAsync();
        Assert.Equal("appeared", appeared.Type);
        Assert.Equal("later", appeared.Object!.Content);

        await t.AliceHub.SetAudienceAsync(id, Private);
        var disappeared = await t.BobHub.NextAsync();
        Assert.Equal("disappeared", disappeared.Type);
        Assert.Equal(id, disappeared.Id);
        Assert.Null(disappeared.Object);
    }

    [Fact]
    public async Task Non_member_cannot_enter_and_cannot_send_commands()
    {
        var t = await SeatAsync();
        var outsider = await TestUser.RegisterAsync(factory);
        await using var hub = await HubClient.ConnectAsync(factory, outsider);

        var enter = await hub.EnterAsync(t.Room.Id);
        var missing = await hub.EnterAsync(Guid.NewGuid());
        var add = await hub.AddAsync(Everyone, "intrusion");

        Assert.False(enter.Ok);
        Assert.Equal(missing, enter); // 存在しない部屋と区別できない。
        Assert.Equal("NotAMember", add.Error);
        Assert.Empty(await t.AliceHub.DrainAsync());
    }

    [Fact]
    public async Task Other_members_cannot_modify_or_probe_objects()
    {
        var t = await SeatAsync();
        var visible = (await t.AliceHub.AddAsync(Everyone, "mine")).CreatedId!.Value;
        var hidden = (await t.AliceHub.AddAsync(Private, "hidden")).CreatedId!.Value;

        Assert.Equal("Forbidden", (await t.BobHub.UpdateAsync(visible, "x")).Error);
        Assert.Equal("NotFound", (await t.BobHub.UpdateAsync(hidden, "x")).Error);
        Assert.Equal("NotFound", (await t.BobHub.UpdateAsync(Guid.NewGuid(), "x")).Error);
    }

    [Fact]
    public async Task Invalid_input_is_rejected()
    {
        var t = await SeatAsync();

        Assert.Equal("BadRequest", (await t.AliceHub.AddAsync(new AudienceDto("bogus"), "x")).Error);
        Assert.Equal("BadRequest", (await t.AliceHub.AddAsync(new AudienceDto(AudienceDto.MembersKind, []), "x")).Error);
        Assert.Equal("BadRequest",
            (await t.AliceHub.AddAsync(Everyone, new string('a', RoomHub.MaxContentLength + 1))).Error);
        Assert.Empty(await t.AliceHub.DrainAsync());
    }

    [Fact]
    public async Task Server_admin_sees_private_objects_without_joining_but_cannot_modify_them()
    {
        var t = await SeatAsync();
        var secret = (await t.AliceHub.AddAsync(Private, "secret")).CreatedId!.Value;

        var admin = await TestUser.LoginAsync(factory, FortunaFactory.AdminEmail, FortunaFactory.AdminPassword);
        await using var adminHub = await HubClient.ConnectAsync(factory, admin);

        Assert.True((await adminHub.EnterAsync(t.Room.Id)).Ok);
        var snapshot = await adminHub.NextAsync();
        Assert.Equal("secret", snapshot.Object!.Content);

        // 新しい非公開物にも、参加していない管理者へ届く。
        await t.AliceHub.AddAsync(Private, "secret2");
        Assert.Equal("secret2", (await adminHub.NextAsync()).Object!.Content);

        // 閲覧のみ。参加者ではないので編集はできない。
        Assert.Equal("NotAMember", (await adminHub.UpdateAsync(secret, "x")).Error);
        Assert.Empty(await t.BobHub.DrainAsync());
    }

    [Fact]
    public async Task Reconnecting_gets_a_snapshot_of_only_what_the_user_can_see()
    {
        var t = await SeatAsync();
        await t.AliceHub.AddAsync(Everyone, "public");
        await t.AliceHub.AddAsync(Private, "secret");
        await t.AliceHub.AddAsync(Only(t.Bob.Id), "for bob");

        await using var again = await HubClient.ConnectAsync(factory, t.Bob);
        Assert.True((await again.EnterAsync(t.Room.Id)).Ok);

        var snapshot = await again.DrainAsync();
        Assert.Equal(["for bob", "public"], snapshot.Select(e => e.Object!.Content).Order().ToArray());
    }

    [Fact]
    public async Task Events_for_one_room_do_not_leak_to_the_same_users_other_room()
    {
        var alice = await TestUser.RegisterAsync(factory, "alice");
        var roomA = await Rooms.CreateAsync(alice, "A");
        var roomB = await Rooms.CreateAsync(alice, "B");

        await using var hubA = await HubClient.ConnectAsync(factory, alice);
        await using var hubB = await HubClient.ConnectAsync(factory, alice);
        await hubA.EnterAsync(roomA.Id);
        await hubB.EnterAsync(roomB.Id);

        await hubA.AddAsync(Everyone, "only in A");

        Assert.Equal("only in A", (await hubA.NextAsync()).Object!.Content);
        Assert.Empty(await hubB.DrainAsync());
    }

    [Fact]
    public async Task Events_arrive_in_the_order_they_were_applied()
    {
        var t = await SeatAsync();
        var id = (await t.AliceHub.AddAsync(Everyone, "v1")).CreatedId!.Value;

        for (var i = 2; i <= 30; i++)
        {
            await t.AliceHub.UpdateAsync(id, $"v{i}");
        }

        var versions = (await t.BobHub.DrainAsync(TimeSpan.FromSeconds(1)))
            .Select(e => e.Object!.Version)
            .ToList();

        Assert.Equal(30, versions.Count);
        Assert.Equal(versions.Order().ToList(), versions);
    }

    [Fact]
    public async Task Kicking_removes_the_member_deletes_their_objects_and_notifies_them()
    {
        var t = await SeatAsync();
        var bobsObject = (await t.BobHub.AddAsync(Everyone, "bob's token")).CreatedId!.Value;
        await t.AliceHub.NextAsync(); // bob の追加が alice に届く
        await t.BobHub.NextAsync();

        var kick = await t.Alice.Http.PostAsJsonAsync($"/api/rooms/{t.Room.Id}/kick", new { userId = t.Bob.Id });
        Assert.Equal(HttpStatusCode.NoContent, kick.StatusCode);

        Assert.Equal(t.Room.Id, await t.BobHub.NextRemovedAsync());

        var gone = await t.AliceHub.NextAsync();
        Assert.Equal("disappeared", gone.Type);
        Assert.Equal(bobsObject, gone.Id);

        // 追放後は何も受け取れず、操作もできない。
        Assert.Equal("NotAMember", (await t.BobHub.AddAsync(Everyone, "still here?")).Error);
        Assert.Empty(await t.BobHub.DrainAsync());
        Assert.False((await t.BobHub.EnterAsync(t.Room.Id)).Ok);
    }

    [Fact]
    public async Task Leaving_keeps_objects_for_others_and_stops_delivery_to_the_leaver()
    {
        var t = await SeatAsync();
        await t.BobHub.AddAsync(Everyone, "stays");
        await t.AliceHub.NextAsync();
        await t.BobHub.NextAsync();

        var leave = await t.Bob.Http.PostAsync($"/api/rooms/{t.Room.Id}/leave", null);
        Assert.Equal(HttpStatusCode.NoContent, leave.StatusCode);
        Assert.Equal(t.Room.Id, await t.BobHub.NextRemovedAsync());

        // 退室しても所有物は残り、他のメンバーには見え続ける（新規接続のスナップショットで確認）。
        await using var carolHub = await HubClient.ConnectAsync(factory, t.Alice);
        await carolHub.EnterAsync(t.Room.Id);
        var snapshot = await carolHub.DrainAsync();
        Assert.Contains(snapshot, e => e.Object!.Content == "stays");

        await t.AliceHub.AddAsync(Everyone, "after leave");
        Assert.Empty(await t.BobHub.DrainAsync());
    }
}
