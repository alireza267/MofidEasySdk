using System.Collections.Frozen;

namespace MofidEasySdk;

/// <summary>
/// Why the order system rejected a request. Each value matches the server error name without
/// its <c>Error</c> suffix (for example <see cref="PriceIsNotInRange"/> is <c>PriceIsNotInRangeError</c>).
/// </summary>
public enum OmsErrorKind
{
    /// <summary>An error name this SDK version doesn't recognize. See <see cref="OmsError.Name"/>.</summary>
    Unknown = 0,

    /// <summary>Not enough money in the account.</summary>
    MoneyBalanceIsNotEnough,

    /// <summary>Price is outside the allowed range for the symbol.</summary>
    PriceIsNotInRange,

    /// <summary>The symbol is closed.</summary>
    SymbolIsClose,

    /// <summary>Quantity is outside the allowed range for the symbol.</summary>
    VolumeIsNotInRange,

    /// <summary>Internal error in the order system.</summary>
    InternalSystem,

    /// <summary>Symbol information could not be loaded.</summary>
    LoadSymbol,

    /// <summary>The order could not be edited.</summary>
    ModifyOrder,

    /// <summary>The order could not be deleted.</summary>
    DeleteOrder,

    /// <summary>Order rules (limits) could not be read.</summary>
    ErrorInGetOrderRule,

    /// <summary>The symbol group's state doesn't allow trading.</summary>
    GroupStateNotAllow,

    /// <summary>Buying is not allowed on this symbol.</summary>
    BuyNotAllowedInThisSymbol,

    /// <summary>Selling is not allowed on this symbol.</summary>
    SellNotAllowedInThisSymbol,

    /// <summary>Blocked assets can't be sold.</summary>
    SellOnBlockAsset,

    /// <summary>The account balance isn't ready yet; try again shortly.</summary>
    UserMoneyIsNotReady,

    /// <summary>The sell order is below the minimum allowed value.</summary>
    LowPricedSellIsNotAllowed,

    /// <summary>The buy order could trade against your own sell order.</summary>
    BuyFromSelf,

    /// <summary>The sell order could trade against your own buy order.</summary>
    SellToSelf,

    /// <summary>The request has expired.</summary>
    RequestIsExpired,

    /// <summary>The order is a duplicate.</summary>
    DuplicatedOrder,

    /// <summary>Orders were sent too close together.</summary>
    OrderInterval,

    /// <summary>A put option on a base symbol (Tabaee) needs the underlying shares.</summary>
    NotEnoughBaseSymbolAssetForTabaee,

    /// <summary>A server-defined error; see <see cref="OmsError.Message"/> and <see cref="OmsError.Code"/>.</summary>
    Custom,
}

internal static class OmsErrorKindNames
{
    private static readonly FrozenDictionary<string, OmsErrorKind> ByName = Enum.GetValues<OmsErrorKind>()
        .Where(kind => kind is not (OmsErrorKind.Unknown or OmsErrorKind.Custom))
        .Select(kind => KeyValuePair.Create($"{kind}Error", kind))
        .Append(KeyValuePair.Create("OmsCustomError", OmsErrorKind.Custom))
        .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public static OmsErrorKind FromName(string? name) =>
        name is not null && ByName.TryGetValue(name, out var kind) ? kind : OmsErrorKind.Unknown;
}
