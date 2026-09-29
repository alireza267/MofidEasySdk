namespace MofidEasySdk;

/// <summary>
/// Supplies the JWT access token sent with each request. Implement this to load the token
/// from your own source (a file, a secret store, ...); otherwise use <see cref="StaticTokenProvider"/>.
/// </summary>
public interface IEasyTraderTokenProvider
{
    /// <summary>Returns the current access token, with or without a <c>"Bearer "</c> prefix.</summary>
    ValueTask<string?> GetTokenAsync(CancellationToken cancellationToken = default);
}
