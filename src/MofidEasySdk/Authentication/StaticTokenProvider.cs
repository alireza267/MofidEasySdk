using MofidEasySdk.Authentication;

namespace MofidEasySdk;

/// <summary>
/// Holds a token copied from the browser. Call <see cref="SetToken(string)"/> with a fresh
/// token when the old one expires. Safe to use from multiple threads.
/// </summary>
public sealed class StaticTokenProvider : IEasyTraderTokenProvider
{
    private volatile JwtToken? _token;

    /// <summary>Creates a provider, optionally with an initial token.</summary>
    public StaticTokenProvider(string? token = null)
    {
        if (!string.IsNullOrWhiteSpace(token))
        {
            SetToken(token);
        }
    }

    /// <summary>When the current token expires, or <c>null</c> if no token is set or it has no <c>exp</c> claim.</summary>
    public DateTimeOffset? ExpiresAt => _token?.ExpiresAt;

    /// <summary>Replaces the current token.</summary>
    /// <exception cref="EasyTraderAuthenticationException">The token is empty or not a JWT.</exception>
    public void SetToken(string token) => _token = JwtToken.Parse(token);

    /// <inheritdoc />
    public ValueTask<string?> GetTokenAsync(CancellationToken cancellationToken = default) => new(_token?.Value);
}
