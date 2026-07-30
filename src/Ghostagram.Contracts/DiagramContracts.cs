using System.Collections.Immutable;
using System.Text.Json;

namespace Ghostagram.Contracts;

/// <summary>JSON operation vocabulary shared by Razor, REST, layout, and MCP adapters.</summary>
public sealed record GhostagramOperation(string Type, JsonElement Value, string? Id = null);

/// <summary>A caller's exactly-once intent against one authoritative document revision.</summary>
public sealed record DiagramCommand(
    string DocumentId,
    string ActorId,
    string CommandId,
    long BaseRevision,
    ImmutableArray<GhostagramOperation> Operations,
    JsonElement? Metadata = null);

public sealed record DiagramSnapshot(string DocumentId, long Revision, JsonElement Model);

public sealed record DiagramChange(
    string DocumentId,
    long Revision,
    string ActorId,
    string CommandId,
    ImmutableArray<GhostagramOperation> Operations,
    DateTimeOffset CommittedUtc,
    JsonElement? Metadata = null);

public sealed record DiagramCommandResult(
    bool Accepted,
    string Code,
    string Message,
    long Revision,
    DiagramSnapshot? Snapshot = null,
    DiagramChange? Change = null);

/// <summary>Deterministic layout intent; the server must commit its output through DiagramCommand.</summary>
public sealed record DiagramLayoutRequest(
    string DocumentId,
    string ActorId,
    string CommandId,
    long BaseRevision,
    string Algorithm,
    int Seed,
    bool DryRun = false,
    DiagramLayoutOptions? Options = null);

public sealed record DiagramLayoutOptions(
    string Direction = "right",
    double OriginX = 64,
    double OriginY = 64,
    double LayerSpacing = 140,
    double NodeSpacing = 56,
    double ComponentSpacing = 120,
    double GroupPadding = 32,
    double GroupHeader = 28,
    int CrossingSweeps = 8,
    int MaximumNodes = 10_000,
    int MaximumEdges = 50_000);

public sealed record DiagramLayoutMetrics(
    int NodeCount,
    int EdgeCount,
    int GroupCount,
    int ComponentCount,
    int StronglyConnectedComponentCount,
    int CyclicComponentCount,
    long EstimatedCrossingsBefore,
    long EstimatedCrossingsAfter,
    double Width,
    double Height,
    double ElapsedMilliseconds);

public sealed record DiagramLayoutResult(
    bool Accepted,
    string Code,
    string Message,
    long BaseRevision,
    long ProposedRevision,
    string Algorithm,
    string AlgorithmVersion,
    int Seed,
    bool DryRun,
    ImmutableArray<GhostagramOperation> Operations,
    DiagramLayoutMetrics? Metrics = null,
    DiagramCommandResult? Commit = null);

/// <summary>A durable handle for a collaborative user-and-agent diagramming session.</summary>
public sealed record DiagramSession(
    string SessionId,
    string DocumentId,
    string ActorId,
    long Revision,
    DateTimeOffset OpenedUtc,
    DateTimeOffset LastActiveUtc,
    IReadOnlyList<string> Participants,
    DiagramSnapshot Snapshot);

public sealed record DiagramSessionResult(
    bool Accepted,
    string Code,
    string Message,
    DiagramSession? Session = null,
    DiagramCommandResult? Command = null);

public sealed record DiagramReadResult(
    bool Accepted,
    string Code,
    string Message,
    string SessionId,
    string DocumentId,
    long Revision,
    DiagramSnapshot? Snapshot,
    IReadOnlyList<DiagramChange> Changes,
    IReadOnlyList<string> Participants);

public sealed record DiagramExportResult(
    bool Accepted,
    string Code,
    string Message,
    string SessionId,
    string DocumentId,
    long Revision,
    string MediaType,
    string? Content);
