using System.Collections.Immutable;

namespace Fortuna.Domain.Visibility;

/// <summary>
/// オブジェクトの公開先。所有者が指定する（ADR-003 §3）。
/// 所有者自身は常に閲覧できるため、公開先に含める必要はない。
/// </summary>
public abstract record Audience
{
    private Audience() { }

    /// <summary>所有者のみ。</summary>
    public sealed record OwnerOnly : Audience;

    /// <summary>指定したメンバーのみ。</summary>
    public sealed record Members : Audience
    {
        public ImmutableHashSet<UserId> UserIds { get; }

        public Members(IEnumerable<UserId> userIds) => UserIds = userIds.ToImmutableHashSet();

        // ImmutableHashSet は参照比較になるため、集合として等価判定する。
        public bool Equals(Members? other) => other is not null && UserIds.SetEquals(other.UserIds);

        public override int GetHashCode() =>
            UserIds.Aggregate(0, (hash, id) => hash ^ id.GetHashCode());
    }

    /// <summary>ルームの参加中メンバー全員（Observer を含む）。</summary>
    public sealed record Room : Audience;

    public static Audience Private { get; } = new OwnerOnly();
    public static Audience Everyone { get; } = new Room();
    public static Audience Only(params UserId[] userIds) => new Members(userIds);
}
