using System.Text.Json;
using System.Text.Json.Serialization;
using Ghostworx.System.Graph;
using Ghostworx.System.Composition;

namespace Ghostagram.Bridge.DeclarativeCompositionProjection;

/// <summary>Write-only display encoding of admitted owner values; this does not admit or decode semantic documents.</summary>
internal static class CompositionPresentationJson
{
    internal static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new AddressWriter());
        options.Converters.Add(new ValueWriter());
        options.Converters.Add(new ExtensionWriter());
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed class AddressWriter : JsonConverter<SemanticAddress>
    {
        public override SemanticAddress Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => throw new NotSupportedException();
        public override void Write(Utf8JsonWriter writer, SemanticAddress value, JsonSerializerOptions options) => writer.WriteStringValue(value.CanonicalText);
        public override void WriteAsPropertyName(Utf8JsonWriter writer, SemanticAddress value, JsonSerializerOptions options) => writer.WritePropertyName(value.CanonicalText);
    }

    // The owner's constructor takes ReadOnlySpan<byte>. Explicit output avoids reflection
    // configuring that ref-struct constructor, even for an empty Extensions collection.
    private sealed class ExtensionWriter : JsonConverter<OpaqueExtension>
    {
        public override OpaqueExtension Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => throw new NotSupportedException();
        public override void Write(Utf8JsonWriter writer, OpaqueExtension value, JsonSerializerOptions options)
        {
            writer.WriteStartObject(); writer.WriteString("identity", value.Identity);
            writer.WriteBase64String("payload", value.Payload.Span);
            writer.WriteBoolean("isInert", value.IsInert); writer.WriteEndObject();
        }
    }

    private sealed class ValueWriter : JsonConverter<GraphSemanticValue>
    {
        public override GraphSemanticValue Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => throw new NotSupportedException();
        public override void Write(Utf8JsonWriter writer, GraphSemanticValue value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("kind", value.Kind.ToString());
            writer.WritePropertyName("value");
            switch (value.Kind)
            {
                case GraphSemanticValueKind.Null: writer.WriteNullValue(); break;
                case GraphSemanticValueKind.Boolean: writer.WriteBooleanValue(value.GetScalar<bool>()); break;
                case GraphSemanticValueKind.Int8: writer.WriteNumberValue(value.GetScalar<sbyte>()); break;
                case GraphSemanticValueKind.Int16: writer.WriteNumberValue(value.GetScalar<short>()); break;
                case GraphSemanticValueKind.Int32: writer.WriteNumberValue(value.GetScalar<int>()); break;
                case GraphSemanticValueKind.Int64: writer.WriteNumberValue(value.GetScalar<long>()); break;
                case GraphSemanticValueKind.Double: writer.WriteNumberValue(value.GetScalar<double>()); break;
                case GraphSemanticValueKind.Decimal: writer.WriteNumberValue(value.GetScalar<decimal>()); break;
                case GraphSemanticValueKind.String: writer.WriteStringValue(value.GetScalar<string>()); break;
                case GraphSemanticValueKind.Guid: writer.WriteStringValue(value.GetScalar<Guid>()); break;
                case GraphSemanticValueKind.DateTimeOffset: writer.WriteStringValue(value.GetScalar<DateTimeOffset>()); break;
                case GraphSemanticValueKind.Bytes: writer.WriteBase64StringValue(value.GetBytes().Span); break;
                case GraphSemanticValueKind.Array:
                    writer.WriteStartArray();
                    foreach (var item in value.GetArray()) Write(writer, item, options);
                    writer.WriteEndArray(); break;
                case GraphSemanticValueKind.Object:
                    writer.WriteStartObject();
                    foreach (var item in value.GetObject().OrderBy(item => item.Key, StringComparer.Ordinal))
                    { writer.WritePropertyName(item.Key); Write(writer, item.Value, options); }
                    writer.WriteEndObject(); break;
                case GraphSemanticValueKind.OpaqueExtension:
                    var opaque = value.GetOpaqueExtension();
                    writer.WriteStartObject(); writer.WriteString("codecId", opaque.CodecId);
                    writer.WriteString("codecVersion", opaque.CodecVersion.CanonicalText);
                    writer.WriteBase64String("canonicalJson", opaque.CanonicalJson.Span);
                    writer.WriteEndObject(); break;
                default: throw new JsonException("Unknown admitted semantic value kind.");
            }
            writer.WriteEndObject();
        }
    }
}

/// <summary>Display serialization only: preserves the closed result arm, canonical addresses and typed admitted values.</summary>
public static class CompositionProjectionJson
{
    public static string Serialize(CompositionProjectionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return JsonSerializer.Serialize(result, result.GetType(), DeclarativeCompositionProjector.Json);
    }

    public static byte[] SerializeToUtf8Bytes(CompositionProjectionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return JsonSerializer.SerializeToUtf8Bytes(result, result.GetType(), DeclarativeCompositionProjector.Json);
    }
}
