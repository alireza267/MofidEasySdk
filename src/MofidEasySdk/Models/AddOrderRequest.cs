namespace MofidEasySdk;

/// <summary>A new order to send to EasyTrader.</summary>
/// <param name="SymbolIsin">ISIN of the instrument, for example <c>IRO1TBAN0001</c>.</param>
/// <param name="Side">Buy or sell.</param>
/// <param name="Price">Limit price in Rials.</param>
/// <param name="Quantity">Number of shares.</param>
/// <param name="Validity">How long the order stays active. Defaults to <see cref="OrderValidity.Day"/>.</param>
public sealed record AddOrderRequest(
    string SymbolIsin,
    OrderSide Side,
    long Price,
    long Quantity,
    OrderValidity Validity = OrderValidity.Day);
