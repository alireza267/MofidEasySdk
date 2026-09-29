namespace MofidEasySdk;

/// <summary>
/// How long an order stays active. Sent to the API as <c>validityType</c>.
/// Only <see cref="Day"/> is supported for now; any other value is rejected before sending.
/// </summary>
public enum OrderValidity
{
    /// <summary>The order is valid until the end of the trading day (روز).</summary>
    Day = 0,
}
