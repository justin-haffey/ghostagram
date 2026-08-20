using System.Text.Json;
using Ghostagram.Contracts;
using Ghostagram.Server.Export;

namespace Ghostagram.Server.Mcp;

public sealed record GhostagramCapabilitiesResult(
    int ProtocolVersion,
    string AuthoringSchemaVersion,
    JsonElement AuthoringSchema,
    IReadOnlyList<string> Operations,
    IReadOnlyList<string> Connectors,
    IReadOnlyList<string> Endpoints,
    IReadOnlyList<string> Overlays,
    NodeRendererCapabilities NodeRenderers,
    IReadOnlyList<string> PropertyTypes,
    IReadOnlyList<string> PropertyModes,
    IReadOnlyList<string> LayoutAlgorithms,
    IReadOnlyList<string> RecommendedWorkflow);

/// <summary>
/// Serializable renderer contract metadata. Browser registrations are runtime-local and are
/// intentionally not reported as if the server could observe them.
/// </summary>
public sealed record NodeRendererCapabilities(
    string KeyField,
    string VersionField,
    string MatchPolicy,
    string Fallback,
    string BrowserRegistrationScope,
    IReadOnlyList<string> BrowserLifecycleHooks,
    IReadOnlyList<NodeSvgRendererRegistration> ServerSvgRenderers);

public sealed record DiagramCollaborationSummary(
    string DocumentId,
    string DisplayName,
    long Revision,
    DateTimeOffset UpdatedUtc,
    int LiveBrowserViews,
    long? OldestBrowserRevision,
    long? LatestBrowserRevision,
    string BrowserUrl);

public sealed record DiagramCatalogResult(IReadOnlyList<DiagramCollaborationSummary> Diagrams);

/// <summary>Single machine-readable inventory for agent authoring and collaboration workflows.</summary>
public sealed class GhostagramCapabilityCatalog(INodeSvgRendererRegistry? nodeRenderers = null)
{
    private readonly INodeSvgRendererRegistry nodeRenderers = nodeRenderers ?? new NodeSvgRendererRegistry();
    private static readonly string[] Operations =
    [
        "node.upsert", "port.upsert", "edge.upsert", "group.upsert", "edgeType.upsert",
        "node.remove", "port.remove", "edge.remove", "group.remove", "edgeType.remove",
        "group.assignNode", "group.assignGroup", "selection.replace", "viewport.set"
    ];

    private static readonly string[] Connectors = ["straight", "flowchart", "bezier", "curved", "state-machine"];
    private static readonly string[] Endpoints = ["blank", "dot", "rectangle"];
    private static readonly string[] Overlays =
    [
        "label", "arrow", "plain-arrow", "triangle-open", "diamond", "diamond-open",
        "erd-one", "erd-zero-one", "erd-one-many", "erd-zero-many"
    ];
    private static readonly string[] PropertyTypes = ["string", "boolean", "integer", "decimal", "date", "dateTime", "enum", "json"];
    private static readonly string[] PropertyModes = ["display", "edit", "displayAndEdit", "hidden"];
    private static readonly string[] RecommendedWorkflow =
    [
        "list_diagrams to identify the human's live document instead of guessing an id",
        "open_session with a distinct stable actorId for this participant",
        "get_diagram for the authoritative snapshot and current revision",
        "apply_operations with a new commandId and the current baseRevision",
        "get_diagram after the original base revision to verify the command and semantic postconditions",
        "list_diagrams to require browser revisions at or beyond the committed revision when live rendering matters",
        "close_session only after outstanding writes and human review are reconciled"
    ];

    public GhostagramCapabilitiesResult Describe() => new(
        ProtocolVersion: 1,
        AuthoringSchemaVersion: GhostagramAuthoringSchema.Version,
        AuthoringSchema: GhostagramAuthoringSchema.Current,
        Operations: Operations,
        Connectors: Connectors,
        Endpoints: Endpoints,
        Overlays: Overlays,
        NodeRenderers: new(
            KeyField: "rendererKey",
            VersionField: "rendererVersion",
            MatchPolicy: "exact-key-and-version",
            Fallback: "standard-node-body",
            BrowserRegistrationScope: "browser-runtime-only; server registrations are not browser registrations",
            BrowserLifecycleHooks: ["mount", "update", "measure", "dispose", "exportSvg"],
            ServerSvgRenderers: nodeRenderers.Registrations),
        PropertyTypes: PropertyTypes,
        PropertyModes: PropertyModes,
        LayoutAlgorithms: ["ghost-layered"],
        RecommendedWorkflow: RecommendedWorkflow);
}
