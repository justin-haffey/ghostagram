using System.Text.Json;
using System.Text.Json.Serialization;
using Diagrams.Core.Abstractions;
using Diagrams.Core.Models;

namespace Diagrams.Core.Serialization;

public sealed class DiagramDocumentSerializer : IDocumentSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    public string Serialize(DiagramDocument document) => JsonSerializer.Serialize(document, Options);

    public DiagramDocument Deserialize(string json)
        => JsonSerializer.Deserialize<DiagramDocument>(json, Options)
            ?? throw new InvalidOperationException("Unable to deserialize diagram document.");
}

