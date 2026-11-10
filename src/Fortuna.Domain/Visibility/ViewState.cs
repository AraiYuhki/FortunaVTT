using Fortuna.Domain.Rooms;

namespace Fortuna.Domain.Visibility;

/// <summary>受信者へ送る可視性の変化（ADR-003 §4）。</summary>
public abstract record ViewEvent(ObjectId Id)
{
    /// <summary>見えるようになった。完全な内容を送る。</summary>
    public sealed record Appeared(RoomObject Object) : ViewEvent(Object.Id);

    /// <summary>見えたまま内容が変わった。</summary>
    public sealed record Changed(RoomObject Object) : ViewEvent(Object.Id);

    /// <summary>見えなくなった（または削除された）。ID のみを送る。内容は送らない。</summary>
    public sealed record Disappeared(ObjectId ObjectId) : ViewEvent(ObjectId);
}

/// <summary>
/// 1人のユーザーに「今見えている」集合（ID → 版）。
/// 状態変更のたびに前後を比較して <see cref="ViewEvent"/> を生成する。
/// メモリ上のキャッシュであり、正は永続化された所有者・公開先データ。落ちても <see cref="Reconcile"/> で再計算できる。
/// </summary>
/// <remarks>スレッドセーフではない。ルームの単一ライター（ADR-002 §2）からのみ操作する。</remarks>
public sealed class ViewState(UserId viewer)
{
    private readonly Dictionary<ObjectId, long> _seen = [];

    public UserId Viewer { get; } = viewer;

    public int VisibleCount => _seen.Count;

    public bool IsVisible(ObjectId id) => _seen.ContainsKey(id);

    /// <summary>オブジェクトの最新状態を反映し、送るべき変化があれば返す。</summary>
    public ViewEvent? Observe(RoomObject obj, RoomMembers members, bool isServerAdmin = false)
    {
        var known = _seen.TryGetValue(obj.Id, out var knownVersion);

        if (VisibilityPolicy.CanView(obj, Viewer, members, isServerAdmin))
        {
            if (!known)
            {
                _seen[obj.Id] = obj.Version;
                return new ViewEvent.Appeared(obj);
            }

            // 古い通知（版が戻る・同一）は無視する。
            if (obj.Version <= knownVersion)
            {
                return null;
            }

            _seen[obj.Id] = obj.Version;
            return new ViewEvent.Changed(obj);
        }

        // 見えない状態。以前見えていたなら、消えたことだけを伝える。
        if (known)
        {
            _seen.Remove(obj.Id);
            return new ViewEvent.Disappeared(obj.Id);
        }

        return null;
    }

    /// <summary>オブジェクトが削除された。見えていたなら <see cref="ViewEvent.Disappeared"/> を返す。</summary>
    public ViewEvent? Forget(ObjectId id) =>
        _seen.Remove(id) ? new ViewEvent.Disappeared(id) : null;

    /// <summary>
    /// ルーム全体の現在状態と突き合わせて、差分をまとめて返す。
    /// 再接続時やメンバー変更時に使う。<paramref name="all"/> に無い既知のオブジェクトは削除扱いにする。
    /// </summary>
    public IReadOnlyList<ViewEvent> Reconcile(
        IEnumerable<RoomObject> all, RoomMembers members, bool isServerAdmin = false)
    {
        var events = new List<ViewEvent>();
        var present = new HashSet<ObjectId>();

        foreach (var obj in all)
        {
            present.Add(obj.Id);
            if (Observe(obj, members, isServerAdmin) is { } e)
            {
                events.Add(e);
            }
        }

        foreach (var id in _seen.Keys.Where(id => !present.Contains(id)).ToList())
        {
            _seen.Remove(id);
            events.Add(new ViewEvent.Disappeared(id));
        }

        return events;
    }
}
