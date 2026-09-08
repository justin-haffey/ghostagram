using Ghostworx.System.Primitives;
using Ghostworx.System.Graph.Links;
using Ghostworx.System.Graph.Runtime;
using System.Text;
using System.Text.Json;
using Ghostagram.Bridge;
using Ghostagram.Core;
using Ghostworx.System.Graph;
using Ghostworx.System.Graph.Serialization.Links;
using Ghostworx.System.SemanticLinks.Conformance;

namespace Ghostagram.Graph.Conformance;

public static class Feature003BridgeHarness
{
    public static Feature003BridgeObservation VerifyProjectionFreshness(
        IReadOnlyList<SemanticLinkConformanceVector> vectors)
    {
        RequireVectorOperation(vectors, "projection.freshness");
        var state = CreateState();
        var presentationBefore = state.Presentation.Capture();
        var initialGraphVersion = state.Graph.Version;
        var initial = Project(state);
        Require(state.Graph.Version == initialGraphVersion,
            "Full Bridge projection mutated the authoritative graph.");
        VerifyProjectedLinkAndEntries(state, initial, "fresh", projectionRevision: 1);

        var context = SemanticLinkOperationContext.Create(
            SemanticLinkConformanceProfile.V1, default, "ghostagram-projection-invalidation");
        var invalidated = new SemanticLinkProjectionBuilder().Invalidate(
            state.SystemProjection,
            [state.Link.Address],
            "ghostagram-public-invalidation",
            2,
            context);
        Require(invalidated.IsSuccess && invalidated.Value.Entries.All(item =>
                item.Freshness == SemanticLinkProjectionFreshness.Stale),
            "System did not produce a stale public backreference view.");

        var baseVersion = state.Graph.Version;
        var staleBytes = state.Serializer.Serialize(invalidated.Value.Entries[0], "link-changed");
        state.Record.Set(ProjectionProperty(0), Encoding.UTF8.GetString(staleBytes));
        var authoritative = state.Graph.CaptureSnapshot();
        var batch = state.Graph.ReadChangesSince(baseVersion).Single();
        var delta = new GraphDiagramDeltaProjector(state.Projection)
            .Project(batch, initial, authoritative, state.Presentation.Capture());
        Require(!delta.RequiresFullProjection && delta.BaseGraphVersion == baseVersion &&
                delta.GraphVersion == authoritative.Version && delta.Operations.Length > 0,
            "A contiguous public Link invalidation did not traverse the Bridge delta seam.");
        Require(state.Graph.Version == authoritative.Version,
            "Bridge delta projection mutated the authoritative graph.");
        var afterInvalidation = Project(state);
        var stale = ReadProjection(state, afterInvalidation, 0);
        Require(stale.Freshness == "stale" && stale.ProjectionRevision == 2 &&
                stale.InvalidationToken == "ghostagram-public-invalidation" &&
                stale.InvalidationReason == "link-changed",
            "Bridge projection did not expose the delivered stale backreference facts.");

        var rebuilt = new SemanticLinkProjectionBuilder().Rebuild(
            [state.Link], [state.Observation], 3, context);
        Require(rebuilt.IsSuccess && rebuilt.Value.Entries.All(item =>
                item.Freshness == SemanticLinkProjectionFreshness.Fresh),
            "System did not produce a fresh rebuilt backreference view.");
        for (var ordinal = 0; ordinal < rebuilt.Value.Entries.Count; ordinal++)
            state.Record.Set(ProjectionProperty(ordinal), Encoding.UTF8.GetString(
                state.Serializer.Serialize(rebuilt.Value.Entries[ordinal])));
        var rebuildGraphVersion = state.Graph.Version;
        var rebuiltDocument = Project(state);
        Require(state.Graph.Version == rebuildGraphVersion,
            "Full Bridge rebuild projection mutated the authoritative graph.");
        VerifyProjectedLinkAndEntries(state, rebuiltDocument, "fresh", projectionRevision: 3);

        var duplicate = new SemanticLinkProjectionBuilder().Build(
            [state.Link, state.Link], [state.Observation], 4, context);
        Require(!duplicate.IsSuccess && duplicate.Outcome?.Code == SemanticLinkOutcomeCode.ValidationFailure,
            "System support accepted duplicate canonical Link identity.");
        Require(state.Serializer.Serialize(state.Link).AsSpan().SequenceEqual(state.LinkBytes) &&
                state.Link.Address == state.OriginalAddress && state.Link.Revision == state.OriginalRevision,
            "Bridge projection mutated canonical Link identity or revision.");
        Require(PresentationEquivalent(presentationBefore, state.Presentation.Capture()),
            "Semantic Link projection mutated the Ghostagram presentation sidecar.");

        return Observation(
            vectors,
            projectionInvocations: 3,
            deltaInvocations: 1,
            canonicalStateUnchanged: true,
            presentationStateSeparate: true,
            protectedDataExcluded: PortableValuesExcludePresentation(state, rebuiltDocument));
    }

    public static Feature003BridgeObservation VerifySecurityBoundary(
        IReadOnlyList<SemanticLinkConformanceVector> vectors)
    {
        RequireVectorOperation(vectors, "security.boundary");
        var forbidden = vectors.SelectMany(vector =>
                vector.Input.GetProperty("forbidden").EnumerateArray())
            .Select(item => item.GetString() ?? string.Empty)
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var state = CreateState();
        var graphVersion = state.Graph.Version;
        var presentationBefore = state.Presentation.Capture();
        var projected = Project(state);
        VerifyProjectedLinkAndEntries(state, projected, "fresh", projectionRevision: 1);

        var portable = PortableSemanticJson(state, projected);
        Require(forbidden.All(marker => !portable.Contains(marker, StringComparison.OrdinalIgnoreCase)),
            "A protected or runtime field entered a portable Semantic Link Bridge observation.");
        Require(!typeof(SemanticLinkDefinition).GetProperties().Any(property =>
                forbidden.Contains(property.Name, StringComparer.OrdinalIgnoreCase)) &&
                !typeof(SemanticLinkEndpointReference.Federated).GetProperties().Any(property =>
                    forbidden.Contains(property.Name, StringComparer.OrdinalIgnoreCase)),
            "A portable Semantic Link contract exposes a forbidden authority or runtime field.");

        return Observation(
            vectors,
            projectionInvocations: 1,
            deltaInvocations: 0,
            canonicalStateUnchanged: state.Graph.Version == graphVersion &&
                                     state.Serializer.Serialize(state.Link).AsSpan().SequenceEqual(state.LinkBytes),
            presentationStateSeparate: PresentationEquivalent(
                presentationBefore, state.Presentation.Capture()),
            protectedDataExcluded: true);
    }

    public static Feature003BridgeObservation VerifyEnvelopeIntegrity(
        IReadOnlyList<SemanticLinkConformanceVector> vectors)
    {
        RequireVectorOperation(vectors, "envelope.integrity");
        var vector = vectors.Single();
        var external = vector.Input.GetProperty("externalParticipants").EnumerateArray()
            .Select(item => item.GetString()).OfType<string>().ToArray();
        var local = vector.Input.GetProperty("localParticipants").EnumerateArray()
            .Select(item => item.GetString()).OfType<string>().ToArray();
        Require(external.Contains(SemanticLinkConformanceParticipants.Ghostagram, StringComparer.Ordinal) &&
                !local.Contains(SemanticLinkConformanceParticipants.Ghostagram, StringComparer.Ordinal) &&
                Feature003ConformanceRunner.Participant == SemanticLinkConformanceParticipants.Ghostagram,
            "The frozen participant partition does not identify the real Ghostagram runner.");

        var state = CreateState();
        var graphVersion = state.Graph.Version;
        var presentationBefore = state.Presentation.Capture();
        var projected = Project(state);
        VerifyProjectedLinkAndEntries(state, projected, "fresh", projectionRevision: 1);
        var projectedLink = CanonicalPropertyBytes(state, projected, LinkProperty);
        Require(projectedLink.AsSpan().SequenceEqual(state.LinkBytes),
            "The Bridge output is not the canonical System Link input.");

        return Observation(
            vectors,
            projectionInvocations: 1,
            deltaInvocations: 0,
            canonicalStateUnchanged: state.Graph.Version == graphVersion,
            presentationStateSeparate: PresentationEquivalent(
                presentationBefore, state.Presentation.Capture()),
            protectedDataExcluded: PortableValuesExcludePresentation(state, projected));
    }

    private const string LinkProperty = "semanticLink";

    private static ProjectionState CreateState()
    {
        var authority = new SemanticAuthority("ghostagram.conformance");
        var graph = new GraphStore("ghostagram-semantic-link-conformance", null, new GraphStoreOptions
        {
            Authority = authority,
            NodeRetention = GraphNodeRetentionMode.Strong
        });
        var sourceNode = new ObjectNode(new object(), graph, nodeName: "semantic-link-source");
        var source = SemanticAddress.ForNode(authority, graph.Id, sourceNode.Id);
        var remote = SemanticAddress.ForNode(
            new SemanticAuthority("remote.ghostagram.conformance"), NodeId.New(), NodeId.New(), 7);
        var target = new SemanticLinkEndpointReference.Federated(new FederationReference(
            remote,
            GraphContractVersion.Current,
            SemanticLinkConformanceProfile.V1.ProfileId,
            SemanticLinkConformanceProfile.V1.Version,
            [SemanticLinkConformanceProfile.BuiltInVocabulary]));
        var link = new SemanticLinkDefinition(
            SemanticAddress.ForRelationship(authority, graph.Id, EdgeId.New()),
            SemanticLinkConformanceProfile.RelatedDefinitionKind,
            [
                new SemanticLinkEndpoint(0, SemanticLinkConformanceProfile.SourceRole,
                    new SemanticLinkEndpointReference.Local(source)),
                new SemanticLinkEndpoint(1, SemanticLinkConformanceProfile.TargetRole, target)
            ],
            new SemanticLinkRevision(1, graph.Version + 1),
            SemanticLinkConformanceProfile.V1.ProfileId,
            SemanticLinkConformanceProfile.V1.Version);
        var observedAt = new DateTimeOffset(2026, 8, 28, 0, 0, 0, TimeSpan.Zero);
        var observation = new SemanticLinkObservation(
            link.Address,
            link.Revision.LinkRevision,
            link.ProfileId,
            link.ProfileVersion,
            link.Endpoints.Select(endpoint => new SemanticLinkEndpointObservation(
                endpoint.Ordinal,
                endpoint.Reference,
                endpoint.Reference.Address,
                endpoint.Reference.Address.Revision ?? 1,
                SemanticLinkOutcome.FromCode(SemanticLinkOutcomeCode.ResolvedExact),
                observedAt,
                ["ghostagram-public-observation"],
                graphVisits: 1,
                authorityVisits: 1,
                federationDepth: endpoint.Reference.IsFederated ? 1 : 0)),
            observedAt,
            isFresh: true,
            completionOutcome: SemanticLinkOutcome.FromCode(SemanticLinkOutcomeCode.ResolvedExact),
            correlationId: "ghostagram-conformance");
        var context = SemanticLinkOperationContext.Create(
            SemanticLinkConformanceProfile.V1, default, "ghostagram-projection-build");
        var built = new SemanticLinkProjectionBuilder().Build([link], [observation], 1, context);
        Require(built.IsSuccess && !built.Value.IsAuthoritative && built.Value.Entries.Count == 2,
            "System did not produce the required two-entry non-authoritative projection.");

        var serializer = new SemanticLinkJsonSerializer();
        var linkBytes = serializer.Serialize(link);
        _ = serializer.DeserializeDefinition(linkBytes);
        var record = new SemanticLinkRecordNode(graph, NodeId.New());
        record.Set(LinkProperty, Encoding.UTF8.GetString(linkBytes));
        for (var ordinal = 0; ordinal < built.Value.Entries.Count; ordinal++)
        {
            var bytes = serializer.Serialize(built.Value.Entries[ordinal]);
            _ = serializer.DeserializeProjection(bytes);
            record.Set(ProjectionProperty(ordinal), Encoding.UTF8.GetString(bytes));
        }

        var presentation = new GraphPresentationStore();
        var commit = presentation.Execute(presentation.Revision, editor =>
        {
            editor.SetNode(record.Id, new NodePresentationState(new DiagramBounds(240, 160, 420, 180, 2)));
            editor.SetSelection([record.Id.ToString()]);
            return true;
        });
        Require(commit.Accepted, "The synthetic presentation sidecar could not be prepared.");

        return new ProjectionState(
            graph,
            record,
            presentation,
            new GraphDiagramProjection(),
            serializer,
            link,
            observation,
            built.Value,
            linkBytes,
            link.Address,
            link.Revision);
    }

    private static DiagramDocument Project(ProjectionState state) =>
        state.Projection.Project(state.Graph.CaptureSnapshot(), state.Presentation.Capture());

    private static void VerifyProjectedLinkAndEntries(
        ProjectionState state,
        DiagramDocument document,
        string freshness,
        long projectionRevision)
    {
        var link = state.Serializer.DeserializeLinkDocument(CanonicalPropertyBytes(state, document, LinkProperty));
        Require(link.Address == state.Link.Address.CanonicalText &&
                link.OwnerAuthority == state.Link.OwnerAuthority.Value &&
                link.Kind == state.Link.Kind.CanonicalText &&
                link.LinkRevision == state.Link.Revision.LinkRevision &&
                link.Endpoints.Count == 2 &&
                link.Endpoints.Select(item => item.Ordinal).SequenceEqual([0, 1]) &&
                link.Endpoints.Select(item => item.Role).SequenceEqual(
                    state.Link.Endpoints.Select(item => item.Role.CanonicalText), StringComparer.Ordinal),
            "Bridge projection changed canonical Link identity, kind, revision, or ordered roles.");

        for (var ordinal = 0; ordinal < 2; ordinal++)
        {
            var projection = ReadProjection(state, document, ordinal);
            var endpoint = state.Link.Endpoints[ordinal];
            Require(projection.NonAuthoritative && projection.LinkAddress == state.Link.Address.CanonicalText &&
                    projection.LinkRevision == state.Link.Revision.LinkRevision &&
                    projection.EndpointOrdinal == ordinal &&
                    projection.EndpointRole == endpoint.Role.CanonicalText &&
                    projection.EndpointIdentity == endpoint.Reference.Address.CanonicalText &&
                    projection.EndpointOrigin == endpoint.Reference.Address.Authority.Value &&
                    projection.ObservedRevision == (endpoint.Reference.Address.Revision ?? 1) &&
                    projection.ProjectionRevision == projectionRevision &&
                    projection.Freshness == freshness,
                "Bridge projection changed public backreference identity, origin, role, revision, or freshness.");
            if (endpoint.Reference is SemanticLinkEndpointReference.Federated federated)
            {
                Require(projection.EndpointReference.ReferenceKind == "federated" &&
                        projection.EndpointReference.FederationReference?.Origin == federated.Reference.Origin.CanonicalText &&
                        projection.EndpointReference.FederationReference.RequiredProfileId ==
                        federated.Reference.RequiredProfileId,
                    "Bridge projection lost the origin-preserving Federation Reference.");
            }
            else
            {
                Require(projection.EndpointReference.ReferenceKind == "local" &&
                        projection.EndpointReference.LocalAddress == endpoint.Reference.Address.CanonicalText,
                    "Bridge projection changed a link-local Semantic Address.");
            }
        }
    }

    private static SemanticLinkProjectionEntryDocumentV1 ReadProjection(
        ProjectionState state,
        DiagramDocument document,
        int ordinal) => state.Serializer.DeserializeProjectionDocument(
            CanonicalPropertyBytes(state, document, ProjectionProperty(ordinal)));

    private static byte[] CanonicalPropertyBytes(
        ProjectionState state,
        DiagramDocument document,
        string propertyId)
    {
        var node = document.Nodes.Single(item => item.Id == state.Record.Id.ToString());
        var value = node.Properties.Single(item => item.Id == propertyId).Value
            ?? throw new InvalidDataException($"Projected property '{propertyId}' has no value.");
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException($"Projected property '{propertyId}' is not canonical JSON text.");
        return state.Serializer.Canonicalize(Encoding.UTF8.GetBytes(value.GetString()!));
    }

    private static string PortableSemanticJson(ProjectionState state, DiagramDocument document)
    {
        var values = new List<string>
        {
            Encoding.UTF8.GetString(CanonicalPropertyBytes(state, document, LinkProperty))
        };
        values.AddRange(Enumerable.Range(0, 2).Select(ordinal =>
            Encoding.UTF8.GetString(CanonicalPropertyBytes(state, document, ProjectionProperty(ordinal)))));
        return string.Join('\n', values);
    }

    private static bool PortableValuesExcludePresentation(ProjectionState state, DiagramDocument document)
    {
        var portable = PortableSemanticJson(state, document);
        string[] forbidden =
        [
            "layout", "bounds", "viewport", "selection", "waypoint", "presentationRevision",
            "credential", "secret", "privateLocator", "physicalEndpoint", "runtimeBinding",
            "authorizationGrant", "payload", "providerDetail"
        ];
        return forbidden.All(marker => !portable.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool PresentationEquivalent(
        GraphPresentationSnapshot left,
        GraphPresentationSnapshot right) =>
        left.Revision == right.Revision && left.Viewport == right.Viewport &&
        left.Selection.SequenceEqual(right.Selection, StringComparer.Ordinal) &&
        left.Nodes.Count == right.Nodes.Count && left.Nodes.All(pair =>
            right.Nodes.TryGetValue(pair.Key, out var value) && value == pair.Value) &&
        left.Groups.Count == right.Groups.Count && left.Groups.All(pair =>
            right.Groups.TryGetValue(pair.Key, out var value) && value == pair.Value) &&
        left.EdgeWaypoints.Count == right.EdgeWaypoints.Count && left.EdgeWaypoints.All(pair =>
            right.EdgeWaypoints.TryGetValue(pair.Key, out var value) &&
            pair.Value.SequenceEqual(value));

    private static void RequireVectorOperation(
        IReadOnlyList<SemanticLinkConformanceVector> vectors,
        string operation)
    {
        if (vectors.Count == 0 || vectors.Any(vector =>
                !string.Equals(vector.Operation, operation, StringComparison.Ordinal)))
            throw new InvalidDataException("The assigned fixture vectors do not match the reviewed Bridge operation.");
    }

    private static Feature003BridgeObservation Observation(
        IReadOnlyList<SemanticLinkConformanceVector> vectors,
        int projectionInvocations,
        int deltaInvocations,
        bool canonicalStateUnchanged,
        bool presentationStateSeparate,
        bool protectedDataExcluded) => new(
            "pass",
            vectors.Select(item => item.VectorId).ToArray(),
            projectionInvocations,
            deltaInvocations,
            canonicalStateUnchanged,
            presentationStateSeparate,
            protectedDataExcluded);

    private static string ProjectionProperty(int ordinal) => $"semanticLinkBackreference{ordinal}";

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed record ProjectionState(
        GraphStore Graph,
        SemanticLinkRecordNode Record,
        GraphPresentationStore Presentation,
        GraphDiagramProjection Projection,
        SemanticLinkJsonSerializer Serializer,
        SemanticLinkDefinition Link,
        SemanticLinkObservation Observation,
        SemanticLinkProjection SystemProjection,
        byte[] LinkBytes,
        SemanticAddress OriginalAddress,
        SemanticLinkRevision OriginalRevision);

    private sealed class SemanticLinkRecordNode(GraphStore graph, NodeId id)
        : GraphNode(NodeKind.Define("Ghostworx.System.SemanticLinks", "Projection"),
            graph, "Semantic Link Projection", id)
    {
        public void Set(string key, object? value) => SetMetadata(key, value);
    }
}

public sealed record Feature003BridgeObservation(
    string PublicOutcome,
    IReadOnlyList<string> ExecutedVectorIds,
    int ProjectionInvocations,
    int DeltaInvocations,
    bool CanonicalStateUnchanged,
    bool PresentationStateSeparate,
    bool ProtectedDataExcluded);
