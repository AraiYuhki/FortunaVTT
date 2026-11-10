using Fortuna.Domain;
using Fortuna.Domain.Rooms;
using Fortuna.Domain.Visibility;

namespace Fortuna.Tests.Rooms;

public class RoomTests
{
    private readonly UserId _gm = UserId.New();      // ルームの Owner。GM という固定ロールは無い。
    private readonly UserId _alice = UserId.New();
    private readonly UserId _bob = UserId.New();
    private readonly UserId _observer = UserId.New();
    private readonly UserId _admin = UserId.New();   // サーバー管理者。ルームには参加していない。
    private readonly Room _room;

    public RoomTests()
    {
        _room = Room.Create(RoomId.New(), _gm);
        _room.Join(_alice);
        _room.Join(_bob);
        _room.Join(_observer, RoomRole.Observer);

        _room.Connect(_gm);
        _room.Connect(_alice);
        _room.Connect(_bob);
        _room.Connect(_observer);
        _room.Connect(_admin, isServerAdmin: true);
    }

    private static HashSet<UserId> Recipients(CommandResult r) =>
        r.Deliveries.Select(d => d.Recipient).ToHashSet();

    private static HashSet<UserId> Set(params UserId[] users) => users.ToHashSet();

    private static T EventFor<T>(CommandResult r, UserId user) where T : ViewEvent =>
        Assert.IsType<T>(Assert.Single(r.Deliveries, d => d.Recipient == user).Event);

    private ObjectId Add(UserId actor, Audience audience, string content = "") =>
        _room.AddObject(actor, audience, content).CreatedId!.Value;

    // ---- 作成・追加 ------------------------------------------------------------

    [Fact]
    public void Creator_is_the_active_owner()
    {
        var room = Room.Create(RoomId.New(), _gm);

        Assert.True(room.Members.TryGet(_gm, out var m));
        Assert.Equal(RoomRole.Owner, m.Role);
        Assert.True(m.IsActive);
    }

    [Fact]
    public void Public_object_is_delivered_to_everyone_including_observer_and_admin()
    {
        var r = _room.AddObject(_alice, Audience.Everyone, "map");

        Assert.True(r.Succeeded);
        Assert.Equal(Set(_gm, _alice, _bob, _observer, _admin), Recipients(r));
        Assert.All(r.Deliveries, d => Assert.IsType<ViewEvent.Appeared>(d.Event));
    }

    [Fact]
    public void Private_object_is_delivered_only_to_owner_and_server_admin()
    {
        var r = _room.AddObject(_alice, Audience.Private, "secret");

        Assert.Equal(Set(_alice, _admin), Recipients(r));
    }

    [Fact]
    public void Room_owner_does_not_receive_private_objects_of_others()
    {
        var r = _room.AddObject(_alice, Audience.Private, "secret");

        Assert.DoesNotContain(_gm, Recipients(r));
    }

    [Fact]
    public void Observer_cannot_add_objects()
    {
        var r = _room.AddObject(_observer, Audience.Everyone);

        Assert.Equal(RoomError.Forbidden, r.Error);
        Assert.Equal(0, _room.ObjectCount);
    }

    [Fact]
    public void Non_member_cannot_add_objects()
    {
        var r = _room.AddObject(UserId.New(), Audience.Everyone);

        Assert.Equal(RoomError.NotAMember, r.Error);
    }

    // ---- 公開先の変更 ----------------------------------------------------------

    [Fact]
    public void Hiding_an_object_disappears_it_for_others_and_only_changes_it_for_owner_and_admin()
    {
        var id = Add(_alice, Audience.Everyone, "x");

        var r = _room.SetAudience(_alice, id, Audience.Private);

        Assert.IsType<ViewEvent.Disappeared>(EventFor<ViewEvent.Disappeared>(r, _bob));
        Assert.IsType<ViewEvent.Disappeared>(EventFor<ViewEvent.Disappeared>(r, _gm));
        Assert.IsType<ViewEvent.Disappeared>(EventFor<ViewEvent.Disappeared>(r, _observer));
        EventFor<ViewEvent.Changed>(r, _alice);
        EventFor<ViewEvent.Changed>(r, _admin);
    }

    [Fact]
    public void Revealing_to_specific_members_appears_only_for_them()
    {
        var id = Add(_alice, Audience.Private, "for bob");

        var r = _room.SetAudience(_alice, id, Audience.Only(_bob));

        var appeared = EventFor<ViewEvent.Appeared>(r, _bob);
        Assert.Equal("for bob", appeared.Object.Content);
        Assert.DoesNotContain(_gm, Recipients(r));
        Assert.DoesNotContain(_observer, Recipients(r));
    }

    [Fact]
    public void Update_content_is_delivered_as_Changed_only_to_those_who_can_see_it()
    {
        var id = Add(_alice, Audience.Only(_bob), "v1");

        var r = _room.UpdateContent(_alice, id, "v2");

        Assert.Equal(Set(_alice, _bob, _admin), Recipients(r));
        Assert.All(r.Deliveries, d => Assert.IsType<ViewEvent.Changed>(d.Event));
    }

    // ---- 認可 ------------------------------------------------------------------

    [Fact]
    public void Non_owner_cannot_modify_a_visible_object()
    {
        var id = Add(_alice, Audience.Everyone);

        Assert.Equal(RoomError.Forbidden, _room.UpdateContent(_bob, id, "hack").Error);
        Assert.Equal(RoomError.Forbidden, _room.SetAudience(_bob, id, Audience.Private).Error);
        Assert.Equal(RoomError.Forbidden, _room.RemoveObject(_bob, id).Error);
    }

    [Fact]
    public void Modifying_an_invisible_object_is_indistinguishable_from_a_missing_one()
    {
        var hidden = Add(_alice, Audience.Private);
        var missing = ObjectId.New();

        var onHidden = _room.UpdateContent(_bob, hidden, "probe");
        var onMissing = _room.UpdateContent(_bob, missing, "probe");

        Assert.Equal(RoomError.NotFound, onHidden.Error);
        Assert.Equal(onMissing.Error, onHidden.Error);
    }

    [Fact]
    public void Room_owner_cannot_modify_or_delete_others_objects()
    {
        var id = Add(_alice, Audience.Everyone);

        Assert.Equal(RoomError.Forbidden, _room.RemoveObject(_gm, id).Error);
    }

    [Fact]
    public void Server_admin_can_view_but_not_modify()
    {
        var carol = UserId.New();
        _room.Join(carol);
        _room.Connect(carol, isServerAdmin: true);
        var id = Add(_alice, Audience.Private);

        Assert.Equal(RoomError.Forbidden, _room.UpdateContent(carol, id, "x").Error);
        Assert.Equal(RoomError.Forbidden, _room.RemoveObject(carol, id).Error);
        Assert.Equal(RoomError.Forbidden, _room.SetAudience(carol, id, Audience.Everyone).Error);
    }

    [Fact]
    public void Remove_object_disappears_it_for_those_who_saw_it()
    {
        var id = Add(_alice, Audience.Only(_bob));

        var r = _room.RemoveObject(_alice, id);

        Assert.Equal(Set(_alice, _bob, _admin), Recipients(r));
        Assert.All(r.Deliveries, d => Assert.IsType<ViewEvent.Disappeared>(d.Event));
        Assert.Equal(0, _room.ObjectCount);
    }

    // ---- 接続・スナップショット ------------------------------------------------

    [Fact]
    public void Connect_returns_only_what_the_user_can_see()
    {
        Add(_alice, Audience.Everyone);
        Add(_alice, Audience.Only(_bob));
        Add(_alice, Audience.Private);

        var bob = _room.Connect(_bob);
        var gm = _room.Connect(_gm);
        var alice = _room.Connect(_alice);
        var admin = _room.Connect(_admin, isServerAdmin: true);

        Assert.Equal(2, bob.Deliveries.Count);
        Assert.Single(gm.Deliveries);
        Assert.Equal(3, alice.Deliveries.Count);
        Assert.Equal(3, admin.Deliveries.Count);
    }

    [Fact]
    public void Non_member_cannot_connect_unless_server_admin()
    {
        var stranger = UserId.New();

        Assert.Equal(RoomError.NotAMember, _room.Connect(stranger).Error);
        Assert.True(_room.Connect(stranger, isServerAdmin: true).Succeeded);
    }

    [Fact]
    public void Disconnected_user_receives_nothing()
    {
        _room.Disconnect(_bob);

        var r = _room.AddObject(_alice, Audience.Everyone);

        Assert.DoesNotContain(_bob, Recipients(r));
    }

    // ---- 退室・追放・Owner ----------------------------------------------------

    [Fact]
    public void Leaving_keeps_objects_and_they_return_on_rejoin()
    {
        var pub = Add(_alice, Audience.Everyone, "pub");
        Add(_alice, Audience.Private, "priv");

        Assert.True(_room.Leave(_alice).Succeeded);

        Assert.Equal(2, _room.ObjectCount);
        Assert.Equal(RoomError.NotAMember, _room.Connect(_alice).Error);

        // 公開済みのものは他のメンバーに見え続ける。
        var bob = _room.Connect(_bob);
        Assert.Equal(pub.Value, Assert.IsType<ViewEvent.Appeared>(Assert.Single(bob.Deliveries).Event).Id.Value);

        // 再参加すると所有権を保ったまま、自分の非公開物も見える。
        _room.Join(_alice);
        Assert.Equal(2, _room.Connect(_alice).Deliveries.Count);
    }

    [Fact]
    public void Leaving_drops_the_connection()
    {
        _room.Leave(_alice);

        var r = _room.AddObject(_bob, Audience.Everyone);

        Assert.DoesNotContain(_alice, Recipients(r));
        Assert.False(_room.IsConnected(_alice));
    }

    [Fact]
    public void Leave_by_non_member_fails()
    {
        Assert.Equal(RoomError.NotAMember, _room.Leave(UserId.New()).Error);
    }

    [Fact]
    public void Owner_must_transfer_before_leaving()
    {
        Assert.Equal(RoomError.OwnerMustTransfer, _room.Leave(_gm).Error);

        Assert.True(_room.TransferOwnership(_gm, _alice).Succeeded);

        Assert.True(_room.Leave(_gm).Succeeded);
    }

    [Fact]
    public void Transfer_ownership_moves_kick_authority()
    {
        _room.TransferOwnership(_gm, _alice);

        Assert.Equal(RoomError.Forbidden, _room.Kick(_gm, _bob).Error);
        Assert.True(_room.Kick(_alice, _bob).Succeeded);
    }

    [Fact]
    public void Transfer_to_a_non_active_member_fails()
    {
        _room.Leave(_alice);

        Assert.Equal(RoomError.NotFound, _room.TransferOwnership(_gm, _alice).Error);
        Assert.Equal(RoomError.NotFound, _room.TransferOwnership(_gm, UserId.New()).Error);
    }

    [Fact]
    public void Kick_deletes_the_targets_objects_and_notifies_others_but_not_the_target()
    {
        Add(_bob, Audience.Everyone);
        Add(_bob, Audience.Private);
        var alices = Add(_alice, Audience.Everyone);

        var r = _room.Kick(_gm, _bob);

        Assert.True(r.Succeeded);
        Assert.Equal(1, _room.ObjectCount);
        Assert.DoesNotContain(_bob, Recipients(r));
        Assert.All(r.Deliveries, d => Assert.IsType<ViewEvent.Disappeared>(d.Event));
        // 公開物は gm / alice / observer / admin、非公開物は admin に届く。
        Assert.Equal(4 + 1, r.Deliveries.Count);
        Assert.DoesNotContain(r.Deliveries, d => d.Event.Id == alices);

        Assert.False(_room.Members.TryGet(_bob, out _));
        Assert.Equal(RoomError.NotAMember, _room.Connect(_bob).Error);
    }

    [Fact]
    public void Kick_also_deletes_objects_of_a_member_who_already_left()
    {
        Add(_bob, Audience.Everyone);
        _room.Leave(_bob);

        _room.Kick(_gm, _bob);

        Assert.Equal(0, _room.ObjectCount);
    }

    [Fact]
    public void Only_the_owner_can_kick_and_not_themselves()
    {
        Assert.Equal(RoomError.Forbidden, _room.Kick(_alice, _bob).Error);
        Assert.Equal(RoomError.Forbidden, _room.Kick(_gm, _gm).Error);
        Assert.Equal(RoomError.NotFound, _room.Kick(_gm, UserId.New()).Error);
    }

    [Fact]
    public void Owner_role_cannot_be_granted_through_Join()
    {
        Assert.Throws<ArgumentException>(() => _room.Join(UserId.New(), RoomRole.Owner));
    }
}
