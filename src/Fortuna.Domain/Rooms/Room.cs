using Fortuna.Domain.Visibility;

namespace Fortuna.Domain.Rooms;

public enum RoomError
{
    None,

    /// <summary>対象が存在しない。見えないオブジェクトに対しても同じ値を返す（存在の漏洩を防ぐ。ADR-002 §7）。</summary>
    NotFound,

    /// <summary>見えているが、その操作をする権限がない。</summary>
    Forbidden,

    /// <summary>ルームに参加していない（退室済みを含む）。</summary>
    NotAMember,

    /// <summary>Owner は、移譲しないと退室できない。</summary>
    OwnerMustTransfer,
}

/// <summary>特定のユーザーへ送るべき可視性の変化。</summary>
public sealed record Delivery(UserId Recipient, ViewEvent Event);

public sealed record CommandResult(
    RoomError Error,
    IReadOnlyList<Delivery> Deliveries,
    ObjectId? CreatedId = null)
{
    public bool Succeeded => Error == RoomError.None;

    internal static CommandResult Fail(RoomError error) => new(error, []);
    internal static CommandResult Ok(IReadOnlyList<Delivery> deliveries, ObjectId? createdId = null) =>
        new(RoomError.None, deliveries, createdId);
}

/// <summary>
/// ルームの集約。状態変更を受け付け、変更ごとに「誰にどの変化を送るか」を返す（ADR-002 §3, ADR-003）。
/// 受信者ごとの <see cref="ViewState"/> を保持し、可視性の判定は <see cref="VisibilityPolicy"/> に任せる。
/// </summary>
/// <remarks>スレッドセーフではない。ルームごとの単一ライター（ADR-002 §2）からのみ操作する。</remarks>
public sealed class Room
{
    private sealed record Connection(ViewState View, bool IsServerAdmin);

    private readonly Dictionary<ObjectId, RoomObject> _objects = [];
    private readonly Dictionary<UserId, Connection> _connections = [];
    private RoomMembers _members;

    private Room(RoomId id, RoomMembers members)
    {
        Id = id;
        _members = members;
    }

    public RoomId Id { get; }

    public RoomMembers Members => _members;

    public int ObjectCount => _objects.Count;

    public static Room Create(RoomId id, UserId owner) =>
        new(id, RoomMembers.Of(new RoomMember(owner, RoomRole.Owner)));

    // ---- 接続 ----------------------------------------------------------------

    /// <summary>
    /// 接続し、現在見えているものを <see cref="ViewEvent.Appeared"/> として返す（受信者向けスナップショット）。
    /// 再接続した場合は、見えている集合を作り直す。
    /// </summary>
    /// <param name="isServerAdmin">サーバー側のアカウント属性。クライアントの申告を渡してはならない。</param>
    public CommandResult Connect(UserId user, bool isServerAdmin = false)
    {
        if (!isServerAdmin && !_members.IsActive(user))
        {
            return CommandResult.Fail(RoomError.NotAMember);
        }

        var view = new ViewState(user);
        _connections[user] = new Connection(view, isServerAdmin);

        var events = view.Reconcile(_objects.Values, _members, isServerAdmin);
        return CommandResult.Ok(ToDeliveries(user, events));
    }

    public void Disconnect(UserId user) => _connections.Remove(user);

    public bool IsConnected(UserId user) => _connections.ContainsKey(user);

    // ---- メンバー ------------------------------------------------------------

    /// <summary>参加、または退室済みメンバーの再参加。Owner ロールでは参加できない。</summary>
    public CommandResult Join(UserId user, RoomRole role = RoomRole.Member)
    {
        if (role == RoomRole.Owner)
        {
            throw new ArgumentException("Owner は Create / TransferOwnership でのみ設定する。", nameof(role));
        }

        _members = _members.TryGet(user, out var existing)
            ? _members.With(existing with { IsActive = true })
            : _members.With(new RoomMember(user, role));

        // 参加前から自分宛ての Members 公開が設定されていた場合も、見えるようになるのは接続時（Connect の
        // スナップショット）。退室で接続は切れているため、ここで配信すべき相手はいない。
        return CommandResult.Ok([]);
    }

    /// <summary>退室。所有物は残る（ADR-003 §6）。</summary>
    public CommandResult Leave(UserId user)
    {
        if (!_members.TryGet(user, out var member) || !member.IsActive)
        {
            return CommandResult.Fail(RoomError.NotAMember);
        }

        if (member.Role == RoomRole.Owner)
        {
            return CommandResult.Fail(RoomError.OwnerMustTransfer);
        }

        _members = _members.With(member with { IsActive = false });
        DropConnection(user);
        return CommandResult.Ok([]);
    }

    /// <summary>追放。対象の所有物はすべて削除される（ADR-003 §6）。</summary>
    public CommandResult Kick(UserId actor, UserId target)
    {
        if (!IsActiveOwner(actor) || actor == target)
        {
            return CommandResult.Fail(RoomError.Forbidden);
        }

        if (!_members.TryGet(target, out _))
        {
            return CommandResult.Fail(RoomError.NotFound);
        }

        // 追放した本人には何も配信しない。先に接続を切ってから、他の受信者へ削除を伝える。
        _members = _members.Without(target);
        DropConnection(target);

        var deliveries = new List<Delivery>();
        foreach (var id in _objects.Values.Where(o => o.OwnerId == target).Select(o => o.Id).ToList())
        {
            _objects.Remove(id);
            deliveries.AddRange(ForgetForAll(id));
        }

        return CommandResult.Ok(deliveries);
    }

    public CommandResult TransferOwnership(UserId actor, UserId newOwner)
    {
        if (!IsActiveOwner(actor))
        {
            return CommandResult.Fail(RoomError.Forbidden);
        }

        if (actor == newOwner)
        {
            return CommandResult.Ok([]);
        }

        if (!_members.TryGet(newOwner, out var next) || !next.IsActive)
        {
            return CommandResult.Fail(RoomError.NotFound);
        }

        _members = _members
            .With(new RoomMember(actor, RoomRole.Member))
            .With(next with { Role = RoomRole.Owner });
        return CommandResult.Ok([]);
    }

    // ---- オブジェクト --------------------------------------------------------

    /// <summary>オブジェクトを追加する。Observer は追加できない。</summary>
    public CommandResult AddObject(UserId actor, Audience audience, string content = "")
    {
        if (!_members.TryGet(actor, out var member) || !member.IsActive)
        {
            return CommandResult.Fail(RoomError.NotAMember);
        }

        if (member.Role == RoomRole.Observer)
        {
            return CommandResult.Fail(RoomError.Forbidden);
        }

        var obj = new RoomObject(ObjectId.New(), actor, audience, Version: 1, content);
        _objects[obj.Id] = obj;
        return CommandResult.Ok(Broadcast(obj), obj.Id);
    }

    public CommandResult UpdateContent(UserId actor, ObjectId id, string content) =>
        Mutate(actor, id, o => o with { Content = content, Version = o.Version + 1 });

    /// <summary>公開先を変更する。所有者のみ（ADR-003 §3）。</summary>
    public CommandResult SetAudience(UserId actor, ObjectId id, Audience audience) =>
        Mutate(actor, id, o => o with { Audience = audience, Version = o.Version + 1 });

    public CommandResult RemoveObject(UserId actor, ObjectId id)
    {
        var error = Authorize(actor, id, out _);
        if (error != RoomError.None)
        {
            return CommandResult.Fail(error);
        }

        _objects.Remove(id);
        return CommandResult.Ok(ForgetForAll(id));
    }

    // ---- 内部 ----------------------------------------------------------------

    private CommandResult Mutate(UserId actor, ObjectId id, Func<RoomObject, RoomObject> change)
    {
        var error = Authorize(actor, id, out var obj);
        if (error != RoomError.None)
        {
            return CommandResult.Fail(error);
        }

        var updated = change(obj!);
        _objects[id] = updated;
        return CommandResult.Ok(Broadcast(updated));
    }

    /// <summary>
    /// 編集・削除・公開先変更の認可。
    /// 見えない対象は、存在しない対象と区別がつかないよう <see cref="RoomError.NotFound"/> を返す。
    /// 管理者の閲覧特権は編集権限を含まない（所有者でない限り <see cref="RoomError.Forbidden"/>）。
    /// </summary>
    private RoomError Authorize(UserId actor, ObjectId id, out RoomObject? obj)
    {
        obj = null;

        if (!_members.IsActive(actor))
        {
            return RoomError.NotAMember;
        }

        if (!_objects.TryGetValue(id, out var found) ||
            !VisibilityPolicy.CanView(found, actor, _members, IsServerAdmin(actor)))
        {
            return RoomError.NotFound;
        }

        if (found.OwnerId != actor)
        {
            return RoomError.Forbidden;
        }

        obj = found;
        return RoomError.None;
    }

    private bool IsActiveOwner(UserId user) =>
        _members.TryGet(user, out var m) && m.IsActive && m.Role == RoomRole.Owner;

    private bool IsServerAdmin(UserId user) =>
        _connections.TryGetValue(user, out var c) && c.IsServerAdmin;

    private List<Delivery> Broadcast(RoomObject obj)
    {
        var deliveries = new List<Delivery>();
        foreach (var (user, conn) in _connections)
        {
            if (conn.View.Observe(obj, _members, conn.IsServerAdmin) is { } e)
            {
                deliveries.Add(new Delivery(user, e));
            }
        }

        return deliveries;
    }

    private List<Delivery> ForgetForAll(ObjectId id)
    {
        var deliveries = new List<Delivery>();
        foreach (var (user, conn) in _connections)
        {
            if (conn.View.Forget(id) is { } e)
            {
                deliveries.Add(new Delivery(user, e));
            }
        }

        return deliveries;
    }

    /// <summary>
    /// 退室・追放したユーザーの接続を切る。ただしサーバー管理者の接続は閲覧を続けられるため残す。
    /// </summary>
    private void DropConnection(UserId user)
    {
        if (_connections.TryGetValue(user, out var conn) && !conn.IsServerAdmin)
        {
            _connections.Remove(user);
        }
    }

    private static List<Delivery> ToDeliveries(UserId user, IEnumerable<ViewEvent> events) =>
        events.Select(e => new Delivery(user, e)).ToList();
}
