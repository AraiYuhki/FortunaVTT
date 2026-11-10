namespace Fortuna.Domain.Visibility;

/// <summary>
/// ルームに属する共有対象（コマ・カード・チャットなど）の最小表現。
/// </summary>
/// <param name="Version">変更のたびに単調増加する版。古い通知の無視に使う。</param>
public sealed record RoomObject(
    ObjectId Id,
    UserId OwnerId,
    Audience Audience,
    long Version,
    string Content = "");
