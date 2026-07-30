using System.Collections.Immutable;
using System.Text.Json;

namespace Ghostplumb.Contracts;

/// <summary>JSON operation vocabulary shared by Razor, REST, layout, and MCP adapters.</summary>
public sealed record GhostplumbOperation(string Type, JsonElement Value, string? Id = null);

/// <summary>A caller's exactly-once intent against one authoritative document revision.</summary>
public sealed record DiagramCommand(
    string DocumentId,
    string ActorId,
    string CommandId,
    long BaseRevision,
    ImmutableArray<GhostplumbOperation> Operations);

public sealed record DiagramSnapshot(string DocumentId, long Revision, JsonElement Model);

public sealed record DiagramChange(
    string DocumentId,
    long Revision,
    string ActorId,
    string CommandId,
    ImmutableArray<GhostplumbOperation> Operations,
    DateTimeOffset CommittedUtc);

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
    bool DryRun = false);
