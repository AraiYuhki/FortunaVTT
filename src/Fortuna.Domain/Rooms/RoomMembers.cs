using System.Collections.Immutable;

namespace Fortuna.Domain.Rooms;

/// <summary>
/// ルーム内のロール。GM は固定ロールではないため存在しない（ADR-003）。
/// Owner は管理権限（追放・削除・移譲）のみを持ち、閲覧上の特権は持たない。
/// </summary>
public enum RoomRole
{
    Owner,
    Member,
    Observer,
}

/// <param name="IsActive">退室済み（false）のメンバーは何も閲覧できない。所有物は残る。</param>
public sealed record RoomMember(UserId Id, RoomRole Role, bool IsActive = true);

/// <summary>ルームの所属状況の不変スナップショット。</summary>
public sealed class RoomMembers
{
    public static readonly RoomMembers Empty = new(ImmutableDictionary<UserId, RoomMember>.Empty);

    private readonly ImmutableDictionary<UserId, RoomMember> _members;

    private RoomMembers(ImmutableDictionary<UserId, RoomMember> members) => _members = members;

    public static RoomMembers Of(params RoomMember[] members) =>
        new(members.ToImmutableDictionary(m => m.Id));

    public RoomMembers With(RoomMember member) => new(_members.SetItem(member.Id, member));

    public RoomMembers Without(UserId id) => new(_members.Remove(id));

    public IEnumerable<RoomMember> All => _members.Values;

    public bool TryGet(UserId id, out RoomMember member) => _members.TryGetValue(id, out member!);

    /// <summary>ルームに参加中（退室していない）か。</summary>
    public bool IsActive(UserId id) => _members.TryGetValue(id, out var m) && m.IsActive;
}
