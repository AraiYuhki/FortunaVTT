using Fortuna.Domain;
using Fortuna.Domain.Rooms;
using Fortuna.Domain.Visibility;

namespace Fortuna.Tests.Visibility;

public class ViewStateTests
{
    private readonly UserId _owner = UserId.New();
    private readonly UserId _alice = UserId.New();
    private readonly RoomMembers _members;
    private readonly ViewState _aliceView;

    public ViewStateTests()
    {
        _members = RoomMembers.Of(
            new RoomMember(_owner, RoomRole.Member),
            new RoomMember(_alice, RoomRole.Member));
        _aliceView = new ViewState(_alice);
    }

    private RoomObject Obj(Audience audience, long version = 1, string content = "") =>
        new(ObjectId.New(), _owner, audience, version, content);

    [Fact]
    public void Hidden_object_produces_no_event()
    {
        var obj = Obj(Audience.Private);

        Assert.Null(_aliceView.Observe(obj, _members));
        Assert.False(_aliceView.IsVisible(obj.Id));
    }

    [Fact]
    public void Visible_object_appears_once_then_changes()
    {
        var v1 = Obj(Audience.Everyone, 1, "a");
        var v2 = v1 with { Version = 2, Content = "b" };

        var appeared = Assert.IsType<ViewEvent.Appeared>(_aliceView.Observe(v1, _members));
        Assert.Equal("a", appeared.Object.Content);

        var changed = Assert.IsType<ViewEvent.Changed>(_aliceView.Observe(v2, _members));
        Assert.Equal("b", changed.Object.Content);
    }

    [Fact]
    public void Stale_or_duplicate_versions_are_ignored()
    {
        var v2 = Obj(Audience.Everyone, 2);
        _aliceView.Observe(v2, _members);

        Assert.Null(_aliceView.Observe(v2, _members));
        Assert.Null(_aliceView.Observe(v2 with { Version = 1 }, _members));
    }

    [Fact]
    public void Hiding_an_object_sends_only_its_id_and_no_content()
    {
        var shown = Obj(Audience.Everyone, 1, "secret");
        _aliceView.Observe(shown, _members);

        var hidden = shown with { Audience = Audience.Private, Version = 2 };
        var e = Assert.IsType<ViewEvent.Disappeared>(_aliceView.Observe(hidden, _members));

        Assert.Equal(shown.Id, e.Id);
        Assert.False(_aliceView.IsVisible(shown.Id));
    }

    [Fact]
    public void Revealing_a_hidden_object_sends_it_as_Appeared_with_full_content()
    {
        var hidden = Obj(Audience.Private, 1, "later");
        _aliceView.Observe(hidden, _members);

        var revealed = hidden with { Audience = Audience.Only(_alice), Version = 2 };
        var e = Assert.IsType<ViewEvent.Appeared>(_aliceView.Observe(revealed, _members));

        Assert.Equal("later", e.Object.Content);
    }

    [Fact]
    public void Hide_then_reveal_with_a_lower_version_still_appears()
    {
        // 非公開になった時点で既知リストから外れるため、再公開は版に関わらず Appeared になる。
        var obj = Obj(Audience.Everyone, 5);
        _aliceView.Observe(obj, _members);
        _aliceView.Observe(obj with { Audience = Audience.Private, Version = 6 }, _members);

        var e = _aliceView.Observe(obj with { Audience = Audience.Everyone, Version = 7 }, _members);

        Assert.IsType<ViewEvent.Appeared>(e);
    }

    [Fact]
    public void Forget_returns_Disappeared_only_if_it_was_visible()
    {
        var visible = Obj(Audience.Everyone);
        var hidden = Obj(Audience.Private);
        _aliceView.Observe(visible, _members);
        _aliceView.Observe(hidden, _members);

        Assert.IsType<ViewEvent.Disappeared>(_aliceView.Forget(visible.Id));
        Assert.Null(_aliceView.Forget(hidden.Id));
        Assert.Null(_aliceView.Forget(visible.Id));
    }

    [Fact]
    public void Reconcile_on_fresh_state_emits_Appeared_for_visible_objects_only()
    {
        var pub = Obj(Audience.Everyone);
        var shared = Obj(Audience.Only(_alice));
        var secret = Obj(Audience.Private);

        var events = _aliceView.Reconcile([pub, shared, secret], _members);

        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.IsType<ViewEvent.Appeared>(e));
        Assert.DoesNotContain(events, e => e.Id == secret.Id);
    }

    [Fact]
    public void Reconcile_emits_Disappeared_for_objects_no_longer_present()
    {
        var kept = Obj(Audience.Everyone);
        var removed = Obj(Audience.Everyone);
        _aliceView.Reconcile([kept, removed], _members);

        var events = _aliceView.Reconcile([kept], _members);

        var e = Assert.Single(events);
        Assert.Equal(removed.Id, Assert.IsType<ViewEvent.Disappeared>(e).Id);
    }

    [Fact]
    public void Reconcile_after_leaving_the_room_hides_everything()
    {
        var a = Obj(Audience.Everyone);
        var b = Obj(Audience.Only(_alice));
        _aliceView.Reconcile([a, b], _members);

        var left = _members.With(new RoomMember(_alice, RoomRole.Member, IsActive: false));
        var events = _aliceView.Reconcile([a, b], left);

        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.IsType<ViewEvent.Disappeared>(e));
        Assert.Equal(0, _aliceView.VisibleCount);
    }

    [Fact]
    public void Reconcile_is_idempotent()
    {
        var obj = Obj(Audience.Everyone);
        _aliceView.Reconcile([obj], _members);

        Assert.Empty(_aliceView.Reconcile([obj], _members));
    }
}
