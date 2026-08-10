using System.ComponentModel;
using Ghostagram.Contracts;
using Ghostagram.Server.Sessions;
using ModelContextProtocol.Server;

namespace Ghostagram.Server.Mcp;

/// <summary>
/// Thin MCP adapter. All durable work is delegated to the same session, command, layout,
/// and export services used by the HTTP and real-time surfaces.
/// </summary>
[McpServerToolType]
public sealed class GhostagramMcpTools(
    DiagramSessionService sessions,
    IDocumentCatalog documents,
    IDocumentChangeNotifier documentChanges,
    GhostagramCapabilityCatalog capabilities)
{
    [McpServerTool(Name = "describe_capabilities")]
    [Description("Returns Ghostagram's protocol version, supported authoring vocabulary, recommended collaboration workflow, and full JSON Schema for documents and operation values. Call this before constructing unfamiliar nodes, properties, ports, edge markers, or groups.")]
    public GhostagramCapabilitiesResult DescribeCapabilities() => capabilities.Describe();

    [McpServerTool(Name = "list_diagrams")]
    [Description("Lists durable diagrams with authoritative revisions, direct Laboratory URLs, and live browser-view revision acknowledgements. Use this to identify the human's collaboration target instead of guessing a document ID.")]
    public async Task<DiagramCatalogResult> ListDiagrams(CancellationToken cancellationToken = default)
    {
        var summaries = await documents.ListAsync(cancellationToken);
        return new(summaries.Select(summary =>
        {
            var presence = documentChanges.GetPresence(summary.DocumentId);
            return new DiagramCollaborationSummary(
                summary.DocumentId,
                summary.DisplayName,
                summary.Revision,
                summary.UpdatedUtc,
                presence.ViewCount,
                presence.OldestRevision,
                presence.LatestRevision,
                $"http://127.0.0.1:5256/?documentId={Uri.EscapeDataString(summary.DocumentId)}");
        }).ToArray());
    }

    [McpServerTool(Name = "open_session")]
    [Description("Opens a new collaboration handle for an existing diagram, or joins a known session. Call this before reading or editing a diagram.")]
    public Task<DiagramSessionResult> OpenSession(
        [Description("The stable diagram document identifier.")] string documentId,
        [Description("Stable identity for the user or agent joining the session.")] string actorId,
        [Description("An existing Ghostagram session ID to join; omit to create a new session handle.")] string? sessionId = null,
        CancellationToken cancellationToken = default)
        => sessions.OpenAsync(documentId, actorId, sessionId, cancellationToken);

    [McpServerTool(Name = "create_diagram")]
    [Description("Creates a persistent diagram and opens its first collaboration session. The command ID is an idempotency key.")]
    public Task<DiagramSessionResult> CreateDiagram(
        [Description("The new stable diagram document identifier.")] string documentId,
        [Description("Stable identity for the creating user or agent.")] string actorId,
        [Description("A unique idempotency key for this creation intent.")] string commandId,
        [Description("Optional initial Ghostagram operations. When omitted, an empty diagram is created.")] GhostagramOperation[]? initialOperations = null,
        CancellationToken cancellationToken = default)
        => sessions.CreateAsync(documentId, actorId, commandId, initialOperations, cancellationToken);

    [McpServerTool(Name = "get_diagram")]
    [Description("Reads the authoritative diagram snapshot and optionally all committed changes after a known revision. Use this to verify edits and recover from revision conflicts.")]
    public Task<DiagramReadResult> GetDiagram(
        [Description("The collaboration session returned by open_session or create_diagram.")] string sessionId,
        [Description("The participant actor identity used to join the session.")] string actorId,
        [Description("Optional last known revision. Changes after this revision are returned with the current snapshot.")] long? afterRevision = null,
        CancellationToken cancellationToken = default)
        => sessions.ReadAsync(sessionId, actorId, afterRevision, cancellationToken);

    [McpServerTool(Name = "apply_operations")]
    [Description("Atomically applies Ghostagram operations to the shared diagram. Requires the current base revision and a unique command ID; committed changes are broadcast to external SignalR clients and synchronized into open Laboratory browser views.")]
    public Task<DiagramCommandResult> ApplyOperations(
        [Description("The active collaboration session.")] string sessionId,
        [Description("The participant actor performing the edit.")] string actorId,
        [Description("A unique idempotency key. Reuse only when retrying the identical intent.")] string commandId,
        [Description("The authoritative revision on which these operations were prepared.")] long baseRevision,
        [Description("One or more Ghostagram operations such as node.upsert, port.upsert, edge.upsert, group.assignNode, or remove operations.")] GhostagramOperation[] operations,
        CancellationToken cancellationToken = default)
        => sessions.ApplyAsync(sessionId, actorId, commandId, baseRevision, operations, cancellationToken);

    [McpServerTool(Name = "layout_diagram")]
    [Description("Computes deterministic server-side layout for the shared diagram and optionally commits it. Use dryRun to preview operations without advancing the document revision.")]
    public Task<DiagramLayoutResult> LayoutDiagram(
        [Description("The active collaboration session.")] string sessionId,
        [Description("The participant actor requesting layout.")] string actorId,
        [Description("A unique idempotency key for this layout intent.")] string commandId,
        [Description("The authoritative revision to lay out.")] long baseRevision,
        [Description("Layout strategy name. Use ghost-layered for the MVP deterministic compound layout.")] string algorithm = "ghost-layered",
        [Description("Deterministic tie-breaking seed.")] int seed = 0,
        [Description("When true, returns operations and metrics without committing them.")] bool dryRun = false,
        [Description("Optional direction, spacing, packing, and safety limits.")] DiagramLayoutOptions? options = null,
        CancellationToken cancellationToken = default)
        => sessions.LayoutAsync(sessionId, actorId, commandId, baseRevision, algorithm, seed, dryRun, options, cancellationToken);

    [McpServerTool(Name = "export_svg")]
    [Description("Exports the authoritative session revision as deterministic SVG without mutating the diagram.")]
    public Task<DiagramExportResult> ExportSvg(
        [Description("The active collaboration session.")] string sessionId,
        [Description("The participant actor requesting the export.")] string actorId,
        CancellationToken cancellationToken = default)
        => sessions.ExportSvgAsync(sessionId, actorId, cancellationToken);

    [McpServerTool(Name = "close_session")]
    [Description("Removes an actor from a collaboration session without deleting the persistent diagram.")]
    public Task<DiagramSessionResult> CloseSession(
        [Description("The active collaboration session.")] string sessionId,
        [Description("The participant actor leaving the session.")] string actorId)
        => sessions.CloseAsync(sessionId, actorId);
}
