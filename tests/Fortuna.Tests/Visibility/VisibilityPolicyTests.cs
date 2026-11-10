using Fortuna.Domain;
using Fortuna.Domain.Rooms;
using Fortuna.Domain.Visibility;

namespace Fortuna.Tests.Visibility;

public class VisibilityPolicyTests
{
    private readonly UserId _owner = UserId.New();   // オブジェクトの所有者（ルームの Owner とは限らない）
    private readonly UserId _alice = UserId.New();
    private readonly UserId _bob = UserId.New();
    private readonly UserId _roomOwner = UserId.New();
    private readonly UserId _observer = UserId.New();
    private readonly UserId _outsider = UserId.New();
    private readonly RoomMembers _members;

    public VisibilityPolicyTests()
    {
        _members = RoomMembers.Of(
            new RoomMember(_roomOwner, RoomRole.Owner),
            new RoomMember(_owner, RoomRole.Member),
            new RoomMember(_alice, RoomRole.Member),
            new RoomMember(_bob, RoomRole.Member),
            new RoomMember(_observer, RoomRole.Observer));
    }

    private RoomObject Obj(Audience audience) => new(ObjectId.New(), _owner, audience, 1);

    [Fact]
    public void Owner_can_always_see_own_object_even_if_audience_excludes_them()
    {
        Assert.True(VisibilityPolicy.CanView(Obj(Audience.Private), _owner, _members));
        Assert.True(VisibilityPolicy.CanView(Obj(Audience.Only(_alice)), _owner, _members));
    }

    [Fact]
    public void OwnerOnly_is_hidden_from_everyone_else()
    {
        var obj = Obj(Audience.Private);

        Assert.False(VisibilityPolicy.CanView(obj, _alice, _members));
        Assert.False(VisibilityPolicy.CanView(obj, _observer, _members));
    }

    [Fact]
    public void Room_owner_role_has_no_viewing_privilege()
    {
        var obj = Obj(Audience.Private);

        Assert.False(VisibilityPolicy.CanView(obj, _roomOwner, _members));
    }

    [Fact]
    public void Members_audience_is_visible_only_to_listed_users()
    {
        var obj = Obj(Audience.Only(_alice));

        Assert.True(VisibilityPolicy.CanView(obj, _alice, _members));
        Assert.False(VisibilityPolicy.CanView(obj, _bob, _members));
        Assert.False(VisibilityPolicy.CanView(obj, _roomOwner, _members));
    }

    [Fact]
    public void Room_audience_is_visible_to_all_active_members_including_observers()
    {
        var obj = Obj(Audience.Everyone);

        Assert.True(VisibilityPolicy.CanView(obj, _alice, _members));
        Assert.True(VisibilityPolicy.CanView(obj, _observer, _members));
        Assert.True(VisibilityPolicy.CanView(obj, _roomOwner, _members));
    }

    [Fact]
    public void Non_members_cannot_see_anything()
    {
        Assert.False(VisibilityPolicy.CanView(Obj(Audience.Everyone), _outsider, _members));
        Assert.False(VisibilityPolicy.CanView(Obj(Audience.Only(_outsider)), _outsider, _members));
    }

    [Fact]
    public void Left_member_cannot_see_anything_including_own_objects()
    {
        var left = _members.With(new RoomMember(_owner, RoomRole.Member, IsActive: false));

        Assert.False(VisibilityPolicy.CanView(Obj(Audience.Private), _owner, left));
        Assert.False(VisibilityPolicy.CanView(Obj(Audience.Everyone), _owner, left));
    }

    [Fact]
    public void Server_admin_can_see_everything_even_without_being_a_member()
    {
        var left = _members.With(new RoomMember(_alice, RoomRole.Member, IsActive: false));

        Assert.True(VisibilityPolicy.CanView(Obj(Audience.Private), _outsider, _members, isServerAdmin: true));
        Assert.True(VisibilityPolicy.CanView(Obj(Audience.Only(_bob)), _alice, left, isServerAdmin: true));
    }

    [Fact]
    public void Server_admin_privilege_is_off_by_default()
    {
        Assert.False(VisibilityPolicy.CanView(Obj(Audience.Private), _outsider, _members));
    }

    [Fact]
    public void Members_audience_equality_is_set_based()
    {
        Assert.Equal(Audience.Only(_alice, _bob), Audience.Only(_bob, _alice));
        Assert.NotEqual(Audience.Only(_alice), Audience.Only(_alice, _bob));
        Assert.Equal(
            Audience.Only(_alice, _bob).GetHashCode(),
            Audience.Only(_bob, _alice).GetHashCode());
    }
}
