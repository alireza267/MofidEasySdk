namespace MofidEasySdk;

/// <summary>
/// Settings for <see cref="EasyTraderClient"/>.
/// </summary>
public sealed class EasyTraderOptions
{
    /// <summary>The default EasyTrader order API address.</summary>
    public static readonly Uri DefaultBaseAddress = new("https://api-mts.orbis.easytrader.ir/");

    /// <summary>
    /// Base address of the EasyTrader order API. Defaults to <see cref="DefaultBaseAddress"/>.
    /// </summary>
    public Uri BaseAddress { get; set; } = DefaultBaseAddress;

    /// <summary>
    /// JWT access token copied from the browser after logging in to d.easytrader.ir.
    /// A leading <c>"Bearer "</c> prefix is accepted and removed. Can also be supplied or
    /// replaced later through <see cref="StaticTokenProvider.SetToken(string)"/>.
    /// </summary>
    public string? AccessToken { get; set; }

    /// <summary>
    /// Value sent as <c>orderFrom</c> on every request. Defaults to <c>1000</c>.
    /// </summary>
    public int OrderFrom { get; set; } = 1000;

    /// <summary>
    /// A token is treated as expired this long before its <c>exp</c> claim, so a request
    /// is not sent with a token that expires in transit. Defaults to 30 seconds.
    /// </summary>
    public TimeSpan TokenExpirySkew { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long to wait for a response. Defaults to 30 seconds. When it runs out, the call throws
    /// <see cref="EasyTraderOrderStateUnknownException"/>, because the server may still have applied the request.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
