namespace MofidEasySdk;

/// <summary>New values for an existing order.</summary>
/// <remarks>
/// Editing replaces the order with a new one: the response's <see cref="OrderResponse.Id"/> is the
/// new order's ID, and <paramref name="OrderId"/> can no longer be used.
/// </remarks>
/// <param name="OrderId">ID of the order being edited (sent to the API as <c>parentId</c>).</param>
/// <param name="SymbolIsin">ISIN of the order's instrument.</param>
/// <param name="Price">New limit price in Rials.</param>
/// <param name="Quantity">New number of shares.</param>
/// <param name="Validity">How long the order stays active. Defaults to <see cref="OrderValidity.Day"/>.</param>
public sealed record EditOrderRequest(
    string OrderId,
    string SymbolIsin,
    long Price,
    long Quantity,
    OrderValidity Validity = OrderValidity.Day);
