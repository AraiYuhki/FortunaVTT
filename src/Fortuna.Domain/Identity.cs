namespace Fortuna.Domain;

/// <summary>ユーザー（アカウント）の識別子。認証手段に依存しない。</summary>
public readonly record struct UserId(Guid Value)
{
    public static UserId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>ルームの識別子。</summary>
public readonly record struct RoomId(Guid Value)
{
    public static RoomId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>ルーム内オブジェクト（コマ・カード・素材参照など）の識別子。</summary>
public readonly record struct ObjectId(Guid Value)
{
    public static ObjectId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
