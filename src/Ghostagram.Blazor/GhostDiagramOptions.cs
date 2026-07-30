using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ghostagram.Blazor;

public sealed record GhostDiagramOptions(
    string? CssClass = null,
    string Height = "600px",
    int GridSize = 16,
    double MinZoom = .2,
    double MaxZoom = 3,
    bool RespectReducedMotion = true)
{
    internal object ToInteropOptions(object eventSink) => new
    {
        protocolVersion = 1,
        gridSize = GridSize,
        minZoom = MinZoom,
        maxZoom = MaxZoom,
        respectReducedMotion = RespectReducedMotion,
        eventSink,
        eventMethod = "OnGhostagramEvent"
    };
}

public sealed record GhostagramEvent(
    int ProtocolVersion,
    long EventId,
    string InstanceId,
    string? DocumentId,
    long RenderRevision,
    string Type,
    string Origin,
    long Timestamp,
    JsonElement Payload);

public sealed record GhostagramHello(bool Ok, string InstanceId, int ProtocolVersion, JsonElement Capabilities, string Lifecycle);
public sealed record GhostagramResult(bool Ok, string? RequestId, long RenderedRevision, JsonElement Stats, GhostagramProblem? Problem = null);
public sealed record GhostagramProblem(string Code, string Message, JsonElement? Details = null);
public sealed record GhostagramInspection(string InstanceId, string? DocumentId, long Revision, JsonElement Model, JsonElement Stats);
