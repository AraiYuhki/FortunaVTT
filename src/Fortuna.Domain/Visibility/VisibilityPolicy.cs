using Fortuna.Domain.Rooms;

namespace Fortuna.Domain.Visibility;

/// <summary>
/// 「誰に見せてよいか」の判定を集約する唯一の場所（CLAUDE.md の原則2）。
/// ほかの場所で可視性を判定してはならない。
/// </summary>
public static class VisibilityPolicy
{
    /// <param name="isServerAdmin">
    /// 閲覧者がサーバー管理者か（ADR-003 §3）。唯一の閲覧特権で、既定は false（最小権限）。
    /// 閲覧のみで、編集権限は与えない。
    /// </param>
    public static bool CanView(RoomObject obj, UserId viewer, RoomMembers members, bool isServerAdmin = false)
    {
        // サーバー管理者のみ、参加状況・公開先に関わらずすべて見える。
        if (isServerAdmin)
        {
            return true;
        }

        // 退室済み・未参加は何も見えない（自分の所有物も含む）。
        if (!members.IsActive(viewer))
        {
            return false;
        }

        // 所有者は常に自分のものを見られる。
        if (obj.OwnerId == viewer)
        {
            return true;
        }

        // ルームの Owner ロールや GM に特権はない。公開先に含まれる場合のみ見える。
        return obj.Audience switch
        {
            Audience.Room => true,
            Audience.Members m => m.UserIds.Contains(viewer),
            Audience.OwnerOnly => false,
            _ => false,
        };
    }
}
