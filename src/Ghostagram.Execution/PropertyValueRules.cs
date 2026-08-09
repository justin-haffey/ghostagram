using System.Globalization;
using System.Text.Json;
using Ghostagram.Core;

namespace Ghostagram.Execution;

internal static class PropertyValueRules
{
    public static bool IsMissing(JsonElement? value) =>
        value is null || value.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ||
        value.Value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.Value.GetString());

    public static bool IsCompatible(string type, JsonElement? value, IReadOnlyList<string>? options, out string reason)
    {
        reason = string.Empty;
        if (value is null || value.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return true;
        var item = value.Value;
        var valid = type switch
        {
            DiagramPropertyTypes.String => item.ValueKind == JsonValueKind.String,
            DiagramPropertyTypes.Boolean => item.ValueKind is JsonValueKind.True or JsonValueKind.False,
            DiagramPropertyTypes.Integer => item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out _),
            DiagramPropertyTypes.Decimal => item.ValueKind == JsonValueKind.Number && item.TryGetDecimal(out _),
            DiagramPropertyTypes.Date => item.ValueKind == JsonValueKind.String && DateOnly.TryParse(item.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            DiagramPropertyTypes.DateTime => item.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(item.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _),
            DiagramPropertyTypes.Enum => item.ValueKind == JsonValueKind.String && options is { Count: > 0 } && options.Contains(item.GetString()!, StringComparer.Ordinal),
            DiagramPropertyTypes.Json => true,
            _ => true
        };
        if (!valid) reason = $"Value is incompatible with property type '{type}'.";
        return valid;
    }
}
