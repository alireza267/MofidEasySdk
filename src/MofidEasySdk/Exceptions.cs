using System.Net;

namespace MofidEasySdk;

/// <summary>Base type for every error thrown by the SDK.</summary>
public class EasyTraderException : Exception
{
    /// <inheritdoc />
    public EasyTraderException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The access token is missing, malformed, expired, or was rejected by the server (HTTP 401/403).
/// Copy a fresh token from the browser and set it again.
/// </summary>
public class EasyTraderAuthenticationException : EasyTraderException
{
    /// <inheritdoc />
    public EasyTraderAuthenticationException(string message, HttpStatusCode? statusCode = null, string? responseContent = null)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseContent = responseContent;
    }

    /// <summary>HTTP status code, when the server rejected the token.</summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>Response body, when the server rejected the token.</summary>
    public string? ResponseContent { get; }
}

/// <summary>
/// The order system rejected the request (<c>isSuccessful: false</c>), for example because the
/// price is out of range or the balance is too low. Check <see cref="Error"/> for the reason.
/// </summary>
/// <remarks>
/// The server may create an order, assign it an ID, and then have the OMS reject it. In that case
/// <see cref="OrderId"/> holds the ID of the rejected order. It is not live and doesn't need deleting.
/// </remarks>
public class EasyTraderOrderException : EasyTraderException
{
    /// <inheritdoc />
    public EasyTraderOrderException(string? orderId, string? serverMessage, OmsError? error, HttpStatusCode statusCode)
        : base(BuildMessage(orderId, serverMessage, error))
    {
        OrderId = orderId;
        ServerMessage = serverMessage;
        Error = error;
        StatusCode = statusCode;
    }

    /// <summary>ID the server assigned to the rejected order, or <c>null</c> if none was assigned.</summary>
    public string? OrderId { get; }

    /// <summary>The server's <c>message</c> field, if any.</summary>
    public string? ServerMessage { get; }

    /// <summary>Why the order was rejected, or <c>null</c> if the server gave no error details.</summary>
    public OmsError? Error { get; }

    /// <summary>HTTP status code.</summary>
    public HttpStatusCode StatusCode { get; }

    private static string BuildMessage(string? orderId, string? serverMessage, OmsError? error)
    {
        var reason = error is not null
            ? $"{error.Name} ({error.Code}): {error.Message}"
            : string.IsNullOrWhiteSpace(serverMessage) ? "no reason given" : serverMessage;
        return orderId is null ? $"Order rejected: {reason}" : $"Order {orderId} rejected: {reason}";
    }
}

/// <summary>
/// The request may or may not have reached the order system: it timed out or the connection broke
/// after it was sent. <b>The order state is unknown.</b> Check your orders on d.easytrader.ir before
/// retrying, or you may place a duplicate order.
/// </summary>
public class EasyTraderOrderStateUnknownException : EasyTraderException
{
    /// <inheritdoc />
    public EasyTraderOrderStateUnknownException(OrderOperation operation, string? targetOrderId, string reason, Exception? innerException = null)
        : base(BuildMessage(operation, targetOrderId, reason), innerException)
    {
        Operation = operation;
        TargetOrderId = targetOrderId;
    }

    /// <summary>The request whose outcome is unknown.</summary>
    public OrderOperation Operation { get; }

    /// <summary>For an edit or delete, the ID of the order it targeted; <c>null</c> for an add.</summary>
    public string? TargetOrderId { get; }

    private static string BuildMessage(OrderOperation operation, string? targetOrderId, string reason)
    {
        var target = targetOrderId is null ? "" : $" of order {targetOrderId}";
        return $"The order state is unknown: the {operation.ToString().ToLowerInvariant()} request{target} may or may not have been applied ({reason}). "
            + "Check your orders on d.easytrader.ir before retrying.";
    }
}

/// <summary>
/// The SDK could not connect to EasyTrader (DNS, TCP or TLS failure). The request was not sent,
/// so no order was changed and it is safe to retry.
/// </summary>
public class EasyTraderConnectionException : EasyTraderException
{
    /// <inheritdoc />
    public EasyTraderConnectionException(OrderOperation operation, Exception innerException)
        : base($"Could not connect to EasyTrader; the {operation.ToString().ToLowerInvariant()} request was not sent: {innerException.Message}", innerException)
    {
        Operation = operation;
    }

    /// <summary>The request that was not sent.</summary>
    public OrderOperation Operation { get; }
}

/// <summary>The EasyTrader API returned an unsuccessful status code or an unexpected response.</summary>
public class EasyTraderApiException : EasyTraderException
{
    /// <inheritdoc />
    public EasyTraderApiException(HttpStatusCode statusCode, string responseContent, string? message = null)
        : base(message ?? $"EasyTrader API returned {(int)statusCode} ({statusCode}): {responseContent}")
    {
        StatusCode = statusCode;
        ResponseContent = responseContent;
    }

    /// <summary>HTTP status code.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>Response body exactly as received.</summary>
    public string ResponseContent { get; }
}
