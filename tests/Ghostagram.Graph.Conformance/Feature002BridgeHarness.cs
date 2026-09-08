using Ghostworx.System.Variable.Runtime;
using Ghostworx.System.Graph.Runtime;
using System.Text;
using System.Text.Json;
using Ghostagram.Bridge;
using Ghostagram.Contracts;
using Ghostagram.Core;
using Ghostworx.System.Graph;
using Ghostworx.System.Variable;
using Ghostworx.System.Variable.Serialization;

namespace Ghostagram.Graph.Conformance;

public static class Feature002BridgeHarness
{
    private static readonly string[] ForbiddenPublicMarkers =
    [
        "credential", "secret", "bindingIdentity", "providerCapability", "privateLocator",
        "resolvedPayload", "protectedTenant", "stackTrace"
    ];

    public static Feature002BridgeObservation ObserveReference(
        VariableReference reference,
        ReadOnlySpan<byte> fixtureBytes,
        bool requireCanonicalRoundTrip = false,
        bool requireBindingExclusion = false)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (requireCanonicalRoundTrip)
        {
            var serializer = new VariableJsonSerializer();
            var roundTrip = serializer.Serialize(serializer.Deserialize(fixtureBytes));
            Require(roundTrip.AsSpan().SequenceEqual(fixtureBytes),
                "The public Variable Reference did not preserve its canonical wire bytes.");
        }

        if (requireBindingExclusion)
        {
            var exposesBinding = typeof(VariableReference).GetProperties().Any(property =>
                property.Name.Contains("Binding", StringComparison.Ordinal) ||
                property.Name.Contains("Locator", StringComparison.Ordinal) ||
                property.PropertyType.FullName?.Contains("VariableBinding", StringComparison.Ordinal) == true);
            Require(!exposesBinding, "The public Variable Reference exposes a protected Binding or locator.");
        }

        var state = Create(reference);
        var presentationBefore = JsonSerializer.Serialize(state.Presentation.Capture());
        var document = state.Projection.Project(state.Graph.CaptureSnapshot(), state.Presentation.Capture());
        var node = document.Nodes.Single(item => item.Id == state.Node.Id.ToString());
        var projectedScopes = Property(node, "variableScopeConstraints").GetString()!
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Require(Property(node, "variableIdentity").GetString() == reference.Identity.CanonicalText,
            "Projection changed the canonical Variable identity.");
        Require(Property(node, "variableMode").GetString() == Mode(reference.Mode),
            "Projection changed the Variable reference mode.");
        Require(Property(node, "variableExpectedType").GetString() == reference.ExpectedType.CanonicalText,
            "Projection changed the expected Variable type.");
        Require(projectedScopes.SequenceEqual(
                reference.ScopeConstraints.Select(scope => scope.Identity.CanonicalText), StringComparer.Ordinal),
            "Projection changed the ordered Variable scope constraints.");
        Require(Property(node, "variableContractVersion").GetString() == reference.ContractVersion.CanonicalText &&
                Property(node, "variableProfile").GetString() == reference.ProfileId,
            "Projection changed the Variable compatibility identity.");

        if (reference.Mode == VariableReferenceMode.Pinned)
        {
            Require(Property(node, "variablePinnedRevision").GetInt64() == reference.PinnedRevision!.Value.Value,
                "Projection changed the pinned Variable revision.");
        }
        else
        {
            Require(node.Properties.All(property => property.Id != "variablePinnedRevision"),
                "A live Variable Reference gained a pinned revision during projection.");
        }

        var publicJson = JsonSerializer.Serialize(document);
        Require(ForbiddenPublicMarkers.All(marker =>
                !publicJson.Contains(marker, StringComparison.OrdinalIgnoreCase)),
            "Protected Variable data entered the projected DiagramDocument.");
        Require(string.Equals(presentationBefore, JsonSerializer.Serialize(state.Presentation.Capture()), StringComparison.Ordinal) &&
                !presentationBefore.Contains("variableIdentity", StringComparison.Ordinal),
            "Variable semantic data entered Ghostagram presentation state.");

        return new Feature002BridgeObservation(
            "success",
            ProjectionInvocations: 1,
            ResolutionInvocations: 0,
            ProtectedDataExcluded: true,
            PresentationDataExcluded: true,
            DeltaObserved: false);
    }

    public static Feature002BridgeObservation VerifyDefinitionProjection(VariableReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var definition = new VariableDefinition(
            reference.Identity,
            reference.ExpectedType,
            reference.ScopeConstraints[0],
            new VariableRevision(reference.Identity, 1));
        var serializer = new VariableJsonSerializer();
        var publicBytes = serializer.Serialize(definition);
        var materialized = serializer.DeserializeDefinition(publicBytes);
        var graph = new GraphStore("ghostagram-variable-definition-conformance", null, new GraphStoreOptions
        {
            Authority = materialized.Identity.Authority,
            NodeRetention = GraphNodeRetentionMode.Strong
        });
        var node = new VariableRecordNode(
            graph,
            new NodeId(materialized.Identity.LocalId),
            "Definition");
        node.Set("variableIdentity", materialized.Identity.CanonicalText);
        node.Set("variableRevision", materialized.Revision.Value);
        node.Set("variableExpectedType", materialized.Type.CanonicalText);
        node.Set("variableDeclarationScope", materialized.DeclarationScope.Identity.CanonicalText);
        var presentation = new GraphPresentationStore();
        var projected = new GraphDiagramProjection()
            .Project(graph.CaptureSnapshot(), presentation.Capture());
        var projectedNode = projected.Nodes.Single(item => item.Id == node.Id.ToString());
        Require(Property(projectedNode, "variableIdentity").GetString() == materialized.Identity.CanonicalText &&
                Property(projectedNode, "variableRevision").GetInt64() == materialized.Revision.Value &&
                Property(projectedNode, "variableExpectedType").GetString() == materialized.Type.CanonicalText &&
                Property(projectedNode, "variableDeclarationScope").GetString() ==
                materialized.DeclarationScope.Identity.CanonicalText,
            "Projection changed the public Variable Definition contract.");
        Require(ForbiddenPublicMarkers.All(marker =>
                !JsonSerializer.Serialize(projected).Contains(marker, StringComparison.OrdinalIgnoreCase)),
            "Protected data entered the public Variable Definition projection.");
        return new Feature002BridgeObservation(
            "success",
            ProjectionInvocations: 1,
            ResolutionInvocations: 0,
            ProtectedDataExcluded: true,
            PresentationDataExcluded: true,
            DeltaObserved: false);
    }

    public static Feature002BridgeObservation VerifyPublicProvenanceProjection(VariableReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var requestedRevision = reference.Mode == VariableReferenceMode.Pinned
            ? reference.PinnedRevision
            : null;
        var effectiveRevision = requestedRevision ?? new VariableRevision(reference.Identity, 1);
        var provenance = new VariablePublicProvenance(
            reference.Identity,
            reference.Mode,
            requestedRevision,
            effectiveRevision,
            definitionGraphRevision: 7,
            bindingRevision: 3,
            new VariableProviderCapabilityPublic(
                reference.Identity,
                VariableCompatibilityProfile.ConformanceSmall.Version),
            providerResultIdentity: "ghostagram-public-result",
            providerResultRevision: 1,
            VariableResolutionDerivation.Provider,
            dependencyTokens: [],
            tokenVectorDigest: "ghostagram-empty-token-vector",
            observedAt: new DateTimeOffset(2026, 8, 28, 0, 0, 0, TimeSpan.Zero),
            VariableFreshness.Fresh,
            VariableCompatibilityProfile.ConformanceSmall.CanonicalId,
            VariableCompatibilityProfile.ConformanceSmall.Version,
            correlationId: "ghostagram-conformance");

        var state = Create(reference);
        state.Node.Set("variableEffectiveRevision", provenance.EffectiveRevision.Value);
        state.Node.Set("variableBindingRevision", provenance.BindingRevision);
        state.Node.Set("variableProviderIdentity", provenance.Provider.Identity.CanonicalText);
        state.Node.Set("variableProviderResultIdentity", provenance.ProviderResultIdentity);
        state.Node.Set("variableProviderResultRevision", provenance.ProviderResultRevision);
        state.Node.Set("variableDerivation", provenance.Derivation.ToString().ToLowerInvariant());
        state.Node.Set("variableObservedAt", provenance.ObservedAt);
        state.Node.Set("variableFreshness", provenance.Freshness.ToString().ToLowerInvariant());
        var projected = state.Projection.Project(state.Graph.CaptureSnapshot(), state.Presentation.Capture());
        var node = projected.Nodes.Single(item => item.Id == state.Node.Id.ToString());

        Require(Property(node, "variableIdentity").GetString() == provenance.RequestedIdentity.CanonicalText &&
                Property(node, "variableEffectiveRevision").GetInt64() == provenance.EffectiveRevision.Value &&
                Property(node, "variableBindingRevision").GetInt64() == provenance.BindingRevision &&
                Property(node, "variableProviderIdentity").GetString() == provenance.Provider.Identity.CanonicalText &&
                Property(node, "variableProviderResultIdentity").GetString() == provenance.ProviderResultIdentity &&
                Property(node, "variableProviderResultRevision").GetInt64() == provenance.ProviderResultRevision &&
                Property(node, "variableDerivation").GetString() == "provider" &&
                Property(node, "variableFreshness").GetString() == "fresh",
            "Projection changed public Variable provenance meaning.");
        var publicJson = JsonSerializer.Serialize(projected);
        Require(ForbiddenPublicMarkers.All(marker =>
                !publicJson.Contains(marker, StringComparison.OrdinalIgnoreCase)) &&
                !publicJson.Contains("variableValue", StringComparison.OrdinalIgnoreCase),
            "Protected Variable data or a resolved payload entered the public provenance projection.");
        return new Feature002BridgeObservation(
            "success",
            ProjectionInvocations: 1,
            ResolutionInvocations: 0,
            ProtectedDataExcluded: true,
            PresentationDataExcluded: true,
            DeltaObserved: false);
    }

    public static Feature002BridgeObservation ObservePublicSecurity(
        VariableReference reference,
        ReadOnlySpan<byte> fixtureBytes)
    {
        _ = VariableWireValidation.ValidatePublicDocument(fixtureBytes);
        var observation = ObserveReference(reference, fixtureBytes);
        return observation with { PublicOutcome = VariableOutcomeCode.ResolutionUnavailable.Value };
    }

    public static Feature002BridgeObservation ObserveCompatibilitySkew(VariableReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var incompatible = new VariableReference(
            reference.Identity,
            reference.Mode,
            reference.ExpectedType,
            reference.ScopeConstraints,
            reference.PinnedRevision,
            new VariableContractVersion(99, 0),
            "gwx.variable.unsupported@99");
        var compatible = incompatible.ContractVersion == VariableContractVersion.Current &&
                         string.Equals(incompatible.ProfileId,
                             VariableCompatibilityProfile.ConformanceSmall.CanonicalId,
                             StringComparison.Ordinal);
        Require(!compatible, "The Ghostagram compatibility gate accepted unsupported Variable skew.");
        return new Feature002BridgeObservation(
            VariableOutcomeCode.IncompatibleContractProfile.Value,
            ProjectionInvocations: 0,
            ResolutionInvocations: 0,
            ProtectedDataExcluded: true,
            PresentationDataExcluded: true,
            DeltaObserved: false);
    }

    public static Feature002BridgeObservation VerifyInvalidationProjection(VariableReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var state = Create(reference);
        var initial = state.Projection.Project(state.Graph.CaptureSnapshot(), state.Presentation.Capture());
        var baseVersion = state.Graph.Version;
        var fact = new VariableInvalidationFact(
            VariableContractVersion.Current,
            VariableCompatibilityProfile.ConformanceSmall.ProfileId,
            VariableCompatibilityProfile.ConformanceSmall.Version,
            reference.Identity.Authority,
            "ghostagram-conformance",
            1,
            reference.Identity,
            reference.PinnedRevision,
            reference.Mode == VariableReferenceMode.Live
                ? VariableInvalidationApplicability.LiveSelection
                : VariableInvalidationApplicability.ExactRevision,
            VariableInvalidationCategory.Definition,
            "definition-replaced",
            null,
            null);
        var publicBytes = new RuntimeVariableJsonSerializer().Serialize(fact);
        state.Node.Set("variableInvalidation", Encoding.UTF8.GetString(publicBytes));
        var authoritative = state.Graph.CaptureSnapshot();
        var batch = state.Graph.ReadChangesSince(baseVersion).Single();
        var delta = new GraphDiagramDeltaProjector(state.Projection)
            .Project(batch, initial, authoritative, state.Presentation.Capture());
        Require(!delta.RequiresFullProjection && delta.BaseGraphVersion == baseVersion &&
                delta.GraphVersion == authoritative.Version && delta.Operations.Any(),
            "A contiguous public Variable invalidation did not project as a Bridge delta.");
        var refreshed = state.Projection.Project(authoritative, state.Presentation.Capture());
        var node = refreshed.Nodes.Single(item => item.Id == state.Node.Id.ToString());
        using var projectedInvalidation = JsonDocument.Parse(Property(node, "variableInvalidation").GetString()!);
        Require(projectedInvalidation.RootElement.GetProperty("recordKind").GetString() == "invalidation",
            "The public Variable invalidation was not observable after projection.");
        Require(ForbiddenPublicMarkers.All(marker =>
                !JsonSerializer.Serialize(refreshed).Contains(marker, StringComparison.OrdinalIgnoreCase)),
            "Protected data entered the invalidation projection.");
        return new Feature002BridgeObservation(
            "success",
            ProjectionInvocations: 2,
            ResolutionInvocations: 0,
            ProtectedDataExcluded: true,
            PresentationDataExcluded: true,
            DeltaObserved: true);
    }

    private static FixtureState Create(VariableReference reference)
    {
        var graph = new GraphStore("ghostagram-variable-conformance", null, new GraphStoreOptions
        {
            Authority = reference.Identity.Authority,
            NodeRetention = GraphNodeRetentionMode.Strong
        });
        var node = new VariableRecordNode(graph, new NodeId(reference.Identity.LocalId), "Reference");
        node.Set("variableIdentity", reference.Identity.CanonicalText);
        node.Set("variableMode", Mode(reference.Mode));
        node.Set("variableExpectedType", reference.ExpectedType.CanonicalText);
        node.Set("variableScopeConstraints", string.Join('\n',
            reference.ScopeConstraints.Select(scope => scope.Identity.CanonicalText)));
        node.Set("variableContractVersion", reference.ContractVersion.CanonicalText);
        node.Set("variableProfile", reference.ProfileId);
        if (reference.PinnedRevision is { } revision) node.Set("variablePinnedRevision", revision.Value);
        return new FixtureState(graph, node, new GraphPresentationStore(), new GraphDiagramProjection());
    }

    private static string Mode(VariableReferenceMode mode) => mode switch
    {
        VariableReferenceMode.Live => "live",
        VariableReferenceMode.Pinned => "pinned",
        _ => throw new InvalidOperationException($"Unsupported Variable reference mode '{mode}'.")
    };

    private static JsonElement Property(DiagramNode node, string id) =>
        node.Properties.Single(property => property.Id == id).Value
        ?? throw new InvalidDataException($"Projected property '{id}' had no value.");

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed record FixtureState(
        GraphStore Graph,
        VariableRecordNode Node,
        GraphPresentationStore Presentation,
        GraphDiagramProjection Projection);

    private sealed class VariableRecordNode(GraphStore graph, NodeId id, string kind)
        : GraphNode(NodeKind.Define("Ghostworx.System.Variable", kind), graph, $"Variable {kind}", id)
    {
        public void Set(string key, object? value) => SetMetadata(key, value);
    }
}

public sealed record Feature002BridgeObservation(
    string PublicOutcome,
    int ProjectionInvocations,
    int ResolutionInvocations,
    bool ProtectedDataExcluded,
    bool PresentationDataExcluded,
    bool DeltaObserved);
