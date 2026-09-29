namespace MofidEasySdk;

/// <summary>Adds, edits and deletes orders on EasyTrader.</summary>
/// <remarks>
/// Every method can throw:
/// <list type="bullet">
/// <item><see cref="EasyTraderOrderException"/>: the order system rejected the request; see <see cref="EasyTraderOrderException.Error"/>.</item>
/// <item><see cref="EasyTraderOrderStateUnknownException"/>: timed out or the connection broke after sending; check your orders before retrying.</item>
/// <item><see cref="EasyTraderConnectionException"/>: could not connect; nothing was sent and it is safe to retry.</item>
/// <item><see cref="EasyTraderAuthenticationException"/>: the token is missing, expired or rejected.</item>
/// <item><see cref="EasyTraderApiException"/>: an HTTP error or an unexpected response.</item>
/// <item><see cref="ArgumentException"/>: invalid input; nothing was sent.</item>
/// </list>
/// Cancelling through the <see cref="CancellationToken"/> throws <see cref="OperationCanceledException"/>.
/// If it happens after the request was sent, the order state is unknown.
/// </remarks>
public interface IEasyTraderClient
{
    /// <summary>Sends a new order.</summary>
    /// <returns>The accepted order; <see cref="OrderResponse.Id"/> identifies it.</returns>
    Task<OrderResponse> AddOrderAsync(AddOrderRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes the price or quantity of an existing order. The edit creates a new order and takes
    /// the old one out of the matching engine: use the returned <see cref="OrderResponse.Id"/> for any
    /// later edit or delete.
    /// </summary>
    /// <returns>The new order that replaced the edited one.</returns>
    Task<OrderResponse> EditOrderAsync(EditOrderRequest request, CancellationToken cancellationToken = default);

    /// <summary>Deletes (cancels) an existing order.</summary>
    /// <param name="orderId">ID of the order to delete.</param>
    /// <param name="cancellationToken">Cancels the HTTP request.</param>
    /// <returns>The deleted order.</returns>
    Task<OrderResponse> DeleteOrderAsync(string orderId, CancellationToken cancellationToken = default);
}
