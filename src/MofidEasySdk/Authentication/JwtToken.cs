using System.Buffers.Text;
using System.Text.Json;

namespace MofidEasySdk.Authentication;

/// <summary>
/// Reads the expiry from a JWT without validating its signature; the server does that.
/// </summary>
internal sealed record JwtToken(string Value, DateTimeOffset? ExpiresAt)
{
    private const string BearerPrefix = "Bearer ";

    public bool IsExpired(DateTimeOffset now, TimeSpan skew) => ExpiresAt is { } exp && now + skew >= exp;

    public static JwtToken Parse(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new EasyTraderAuthenticationException("No access token is set. Copy the token from d.easytrader.ir and set it before sending requests.");
        }

        var value = token.Trim();
        if (value.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            value = value[BearerPrefix.Length..].Trim();
        }

        var parts = value.Split('.');
        if (parts.Length != 3)
        {
            throw new EasyTraderAuthenticationException("The access token is not a valid JWT (expected three dot-separated parts).");
        }

        try
        {
            var payload = Base64Url.DecodeFromChars(parts[1]);
            using var json = JsonDocument.Parse(payload);
            DateTimeOffset? expiresAt = json.RootElement.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : null;
            return new JwtToken(value, expiresAt);
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentOutOfRangeException)
        {
            throw new EasyTraderAuthenticationException("The access token is not a valid JWT (its payload could not be read).");
        }
    }
}
