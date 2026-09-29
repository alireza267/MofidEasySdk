using System.Globalization;
using System.Text.Json;

namespace MofidEasySdk.Internal;

/// <summary>
/// Reads the server's order response:
/// <c>{ "isSuccessful": bool, "id": "...", "message": "...", "omsError": [ { "name", "error", "code" } ] }</c>.
/// </summary>
internal static class OrderResponseParser
{
    /// <summary>Returns <c>null</c> when <paramref name="content"/> is not an order response.</summary>
    public static ParsedOrderResponse? TryParse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || GetProperty(root, "isSuccessful") is not { ValueKind: JsonValueKind.True or JsonValueKind.False } isSuccessful)
            {
                return null;
            }

            return new ParsedOrderResponse(
                isSuccessful.GetBoolean(),
                GetString(root, "id"),
                GetString(root, "message"),
                ParseError(GetProperty(root, "omsError")));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static OmsError? ParseError(JsonElement? element)
    {
        // The server sends the error wrapped in a one-item array; accept a bare object too.
        var error = element switch
        {
            { ValueKind: JsonValueKind.Array } array when array.GetArrayLength() > 0 => array[0],
            { ValueKind: JsonValueKind.Object } obj => obj,
            _ => (JsonElement?)null,
        };
        if (error is not { ValueKind: JsonValueKind.Object } e)
        {
            return null;
        }

        var name = GetString(e, "name") ?? "";
        return new OmsError(OmsErrorKindNames.FromName(name), name, GetString(e, "error") ?? "", GetInt(e, "code"));
    }

    private static JsonElement? GetProperty(JsonElement obj, string name)
    {
        foreach (var property in obj.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }

    private static string? GetString(JsonElement obj, string name) => GetProperty(obj, name) switch
    {
        { ValueKind: JsonValueKind.String } value => value.GetString(),
        { ValueKind: JsonValueKind.Number } value => value.GetRawText(),
        _ => null,
    };

    private static int GetInt(JsonElement obj, string name) => GetProperty(obj, name) switch
    {
        { ValueKind: JsonValueKind.Number } value when value.TryGetInt32(out var code) => code,
        { ValueKind: JsonValueKind.String } value when int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var code) => code,
        _ => 0,
    };
}

internal sealed record ParsedOrderResponse(bool IsSuccessful, string? Id, string? Message, OmsError? Error);
