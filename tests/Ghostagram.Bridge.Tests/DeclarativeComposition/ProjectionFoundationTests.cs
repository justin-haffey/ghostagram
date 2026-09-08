using Ghostagram.Bridge.DeclarativeCompositionProjection;
using Ghostworx.System.Composition;
using Ghostworx.System.Composition.Definitions;
using Ghostworx.System.Composition.Exchange;
using Ghostworx.System.Graph;
using Ghostworx.System.Primitives;

namespace Ghostagram.Bridge.Tests.DeclarativeComposition;

public static class ProjectionFoundationTests
{
    public static void Run()
    {
        var now = new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
        var clock = new FixedClock(now);
        var context = new CompositionOperationContext("projection-foundation", now.AddSeconds(20), CancellationToken.None);
        var profile = CompositionLimitProfile.ConformanceV1;
        var layouts = new List<CompositionLayoutEntry> { new("canonical-source", 10, 20, 100, 60) };
        var selection = new List<string> { "canonical-source" };
        var admitted = Admit(layouts, selection: selection);
        Require(admitted.IsAccepted, "Valid presentation state must be admitted.");
        layouts.Clear(); selection.Clear();
        Require(admitted.Sidecar!.Layout.Count == 1 && admitted.Sidecar.Selection.Count == 1,
            "Caller collection mutation must not change admitted state.");
        Require(admitted.Sidecar.Layout is not CompositionLayoutEntry[], "An admitted collection must not expose its mutable array.");

        var maximum = Enumerable.Range(0, CompositionPresentationProfile.MaximumRawSidecarEntries)
            .Select(index => new CompositionLayoutEntry("orphan-" + index, 0, 0, 1, 1));
        Require(Admit(maximum).IsAccepted, "The exact raw sidecar maximum must pass independently of semantic element count.");
        Reject(Admit(maximum.Append(new("extra", 0, 0, 1, 1))), "BOUND");
        Reject(Admit([new("duplicate", 0, 0, 1, 1), new("duplicate", 0, 0, 1, 1)]), "COLLISION");
        Reject(Admit([new("nonfinite", double.NaN, 0, 1, 1)]), "GEOMETRY");
        Reject(Admit([new("negative-size", 0, 0, -1, 1)]), "GEOMETRY");
        Require(Admit([new("edge", 1_000_000, -1_000_000, 1, 1)]).IsAccepted, "Exact coordinate bounds must pass.");
        Reject(Admit([new("outside", 1_000_001, 0, 1, 1)]), "GEOMETRY");
        Reject(CompositionPresentationSidecar.Admit(0, [], [], [], [], null, profile,
            new("cancel", now.AddSeconds(20), new CancellationToken(true)), clock), "CANCELLED");
        Reject(CompositionPresentationSidecar.Admit(0, [], [], [], [], null, profile,
            new("deadline", now, CancellationToken.None), clock), "DEADLINE");
        Reject(CompositionPresentationSidecar.Admit(0, [], [], [], [], null, profile, context, clock, "unknown/1"), "PROFILE");

        using var cancellation = new CancellationTokenSource();
        IEnumerable<CompositionLayoutEntry> CancelDuringTraversal()
        {
            yield return new("first", 0, 0, 1, 1);
            cancellation.Cancel();
            yield return new("second", 0, 0, 1, 1);
        }
        Reject(CompositionPresentationSidecar.Admit(0, CancelDuringTraversal(), [], [], [], null, profile,
            new("mid-traversal", now.AddSeconds(20), cancellation.Token), clock), "CANCELLED");

        var root = SemanticAddress.ForNode(new SemanticAuthority("ghostagram.tests"),
            new NodeId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            new NodeId(Guid.Parse("22222222-2222-2222-2222-222222222222")));
        var digest = new byte[32];
        var identity = new CompositionContentIdentity(CompositionContentIdentity.CurrentAlgorithm,
            CompositionContentIdentity.CurrentCanonicalization, digest,
            CompositionContractVersion.Current, CompositionCompilerVersion.Current,
            CompositionProfileIdentity.ConformanceV1, "gwx.component-composition.ir/1.0");
        var tuple = CompatibilityTuple.Current(CompositionExchangeRecordKind.Ir);
        var anchor = new CompositionProjectionSourceAnchor(root, 1, identity, tuple, [new DefinitionProvenance("source", "1")]);
        var same = new CompositionProjectionSourceAnchor(root, 1, identity, tuple, [new DefinitionProvenance("source", "1")]);
        var changed = new CompositionProjectionSourceAnchor(root, 2, identity, tuple, [new DefinitionProvenance("source", "2")]);
        var tupleChanged = new CompositionProjectionSourceAnchor(root, 1, identity,
            tuple with { SchemaVersion = "different-source-schema" }, [new DefinitionProvenance("source", "1")]);
        Require(CompositionProjectionFreshness.Compare(anchor, 1, same, 2) == CompositionRefreshDisposition.SameSource,
            "Same source permits a presentation diff, without claiming Fresh output.");
        Require(CompositionProjectionFreshness.Compare(anchor, 1, changed, 2) == CompositionRefreshDisposition.FullReprojectionRequired,
            "Source revision/provenance change requires full reprojection.");
        Require(CompositionProjectionFreshness.Compare(anchor, 2, changed, 1) == CompositionRefreshDisposition.FullReprojectionRequired,
            "Source discontinuity takes precedence over a lower presentation revision.");
        Require(CompositionProjectionFreshness.Compare(anchor, 1, tupleChanged, 2) == CompositionRefreshDisposition.FullReprojectionRequired,
            "An exact source tuple discontinuity requires full reprojection even when the digest is unchanged.");
        Require(CompositionProjectionFreshness.Compare(anchor, 2, same, 1) == CompositionRefreshDisposition.InvalidPresentationRevision,
            "Presentation revision must not move backwards.");
        var typedOne = new CompositionProjectionSourceAnchor(root, 1, identity, tuple,
            [new DefinitionProvenance("source", "1", metadata: [new(root, GraphSemanticValue.Int32(1))])]);
        var typedTwo = new CompositionProjectionSourceAnchor(root, 1, identity, tuple,
            [new DefinitionProvenance("source", "1", metadata: [new(root, GraphSemanticValue.Int32(2))])]);
        var typedWide = new CompositionProjectionSourceAnchor(root, 1, identity, tuple,
            [new DefinitionProvenance("source", "1", metadata: [new(root, GraphSemanticValue.Int64(1))])]);
        Require(!typedOne.Equals(typedTwo) && !typedOne.Equals(typedWide),
            "Freshness must preserve both typed semantic metadata content and scalar kind.");
        var reference = new DefinitionReference(root, CompositionCompatibilitySet.Current);
        var resolvedAnchor = new CompositionProjectionSourceAnchor(root, 1, identity, tuple, [], referenceProvenance:
            [new CompositionReferenceProvenance(reference, root.Authority, null, 0, 0, CompositionReferenceDisposition.Resolved)]);
        var deniedAnchor = new CompositionProjectionSourceAnchor(root, 1, identity, tuple, [], referenceProvenance:
            [new CompositionReferenceProvenance(reference, root.Authority, null, 0, 0, CompositionReferenceDisposition.Denied)]);
        Require(!resolvedAnchor.Equals(deniedAnchor) && resolvedAnchor.ReferenceProvenance.GetArrayLength() == 1,
            "Ordered producer reference provenance must be visible and included in freshness comparisons.");
        Require(CompositionDiagramIdentity.Encode("component", "ab", "c") != CompositionDiagramIdentity.Encode("component", "a", "bc"),
            "Tuple members must not collide when concatenated.");
        Require(CompositionDiagramIdentity.Encode("component", "a:b", "c") != CompositionDiagramIdentity.Encode("component", "a", "b:c"),
            "Delimiter characters must not collide.");
        Console.WriteLine("PASS composition foundation: immutable sidecars, exact bounds, cancellation, identity and freshness");

        CompositionSidecarAdmission Admit(IEnumerable<CompositionLayoutEntry> source, IEnumerable<string>? selection = null) =>
            CompositionPresentationSidecar.Admit(0, source, [], [], selection ?? [], null, profile, context, clock);
    }

    private static void Reject(CompositionSidecarAdmission result, string code) =>
        Require(!result.IsAccepted && result.Sidecar is null && result.Diagnostic?.Code == "GRAM-COMP-" + code,
            "Rejected admission must expose its explicit code and no partial sidecar: " + code);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
