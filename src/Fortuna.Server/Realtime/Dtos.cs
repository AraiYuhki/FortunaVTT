using Fortuna.Domain;
using Fortuna.Domain.Rooms;
using Fortuna.Domain.Visibility;

namespace Fortuna.Server.Realtime;

/// <summary>公開先のワイヤ表現。Kind は "owner" | "members" | "room"。</summary>
public sealed record AudienceDto(string Kind, Guid[]? UserIds = null)
{
    public const int MaxMembers = 100;

    public const string Owner = "owner";
    public const string MembersKind = "members";
    public const string RoomKind = "room";

    /// <summary>不正な入力は null を返す（呼び出し側が BadRequest にする）。</summary>
    public Audience? ToDomain() => Kind switch
    {
        Owner => Audience.Private,
        RoomKind => Audience.Everyone,
        MembersKind when UserIds is { Length: > 0 and <= MaxMembers } =>
            new Audience.Members(UserIds.Select(id => new UserId(id))),
        _ => null,
    };

    public static AudienceDto From(Audience audience) => audience switch
    {
        Audience.Members m => new(MembersKind, m.UserIds.Select(u => u.Value).ToArray()),
        Audience.Room => new(RoomKind),
        _ => new(Owner),
    };
}

/// <param name="Audience">所有者にだけ送る。他の受信者には公開先の内訳（誰に見せているか）を教えない。</param>
public sealed record ObjectDto(Guid Id, Guid OwnerId, long Version, string Content, AudienceDto? Audience);

/// <param name="Type">"appeared" | "changed" | "disappeared"。disappeared は ID のみで内容を含まない。</param>
public sealed record ViewEventDto(string Type, Guid Id, ObjectDto? Object)
{
    public static ViewEventDto From(Delivery delivery) => delivery.Event switch
    {
        ViewEvent.Appeared a => new("appeared", a.Id.Value, ToDto(a.Object, delivery.Recipient)),
        ViewEvent.Changed c => new("changed", c.Id.Value, ToDto(c.Object, delivery.Recipient)),
        ViewEvent.Disappeared d => new("disappeared", d.Id.Value, null),
        _ => throw new InvalidOperationException($"未対応のイベント: {delivery.Event.GetType().Name}"),
    };

    private static ObjectDto ToDto(RoomObject obj, UserId recipient) =>
        new(obj.Id.Value, obj.OwnerId.Value, obj.Version, obj.Content,
            obj.OwnerId == recipient ? AudienceDto.From(obj.Audience) : null);
}

public sealed record CommandAck(Guid CommandId, bool Ok, string? Error, Guid? CreatedId = null)
{
    public const string BadRequest = "BadRequest";

    public static CommandAck From(Guid commandId, CommandResult r) =>
        new(commandId, r.Succeeded, r.Succeeded ? null : r.Error.ToString(), r.CreatedId?.Value);

    public static CommandAck Bad(Guid commandId) => new(commandId, false, BadRequest);
}

public sealed record EnterResult(bool Ok, string? Error);
