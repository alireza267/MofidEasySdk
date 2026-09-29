namespace MofidEasySdk.Internal;

// Request bodies exactly as the EasyTrader API expects them (serialized camelCase, enums as numbers).

internal sealed record AddOrderBody(AddOrderPayload Order);

internal sealed record AddOrderPayload(
    long Price,
    long Quantity,
    OrderSide Side,
    OrderValidity ValidityType,
    string SymbolIsin,
    int OrderFrom);

internal sealed record EditOrderBody(EditOrderPayload ModifyOrder);

internal sealed record EditOrderPayload(
    long Price,
    long Quantity,
    OrderValidity ValidityType,
    string SymbolIsin,
    int OrderFrom,
    string ParentId);

internal sealed record DeleteOrderBody(string OrderId, int OrderFrom);
