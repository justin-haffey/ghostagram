using System.Text.Json;

namespace Ghostagram.Contracts;

/// <summary>Loads the versioned authoring schema shared by MCP capability discovery and project agents.</summary>
public static class GhostagramAuthoringSchema
{
    public const string Version = "1.0.0";
    public const string ResourceName = "Ghostagram.Contracts.Schemas.ghostagram-authoring.v1.schema.json";

    private static readonly Lazy<JsonElement> Schema = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    public static JsonElement Current => Schema.Value.Clone();

    private static JsonElement Load()
    {
        using var stream = typeof(GhostagramAuthoringSchema).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded Ghostagram schema '{ResourceName}' was not found.");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }
}
