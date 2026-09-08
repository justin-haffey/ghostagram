using System.Security.Cryptography;
using Ghostagram.Bridge.DeclarativeCompositionProjection;
using Ghostagram.Bridge.Tests.DeclarativeComposition;
using Ghostagram.Core;
using Ghostworx.System.Composition;
using Ghostworx.System.Composition.Compiler;

namespace Ghostagram.Graph.Conformance.Composition;

public sealed record CompositionScenarioResult(CompositionProjectionResult? Result, bool Passed, string Assertion,
    string? LocalObservation = null, CompositionResultStatus? AdmissionOutcome = null,
    IReadOnlyDictionary<string, object?>? AdditionalFields = null);

/// <summary>Concrete consumer operations over actual compiler results. Inventory construction never executes a case.</summary>
public static class CompositionProjectionScenarios
{
    public static IReadOnlyList<string> CaseIds { get; } = Array.AsReadOnly(new[]
    {
        "GRAM-NESTED-OPAQUE", "GRAM-PUBLIC-EXPORTS", "GRAM-DIAGNOSTICS", "GRAM-IR-ANCHOR",
        "GRAM-INERT-SURFACES", "GRAM-SAME-SOURCE-DELTA", "GRAM-SOURCE-STALE",
        "GRAM-UNSUPPORTED", "GRAM-SKEWED", "GRAM-COLLISION", "GRAM-PRIVATE-LEAK", "GRAM-CANCEL", "GRAM-DEADLINE",
        "GRAM-NONMUTATION", "GRAM-NO-RELABEL"
    });

    public static string Operation(string caseId) => caseId is "GRAM-SAME-SOURCE-DELTA" or "GRAM-SOURCE-STALE" or "GRAM-NO-RELABEL"
        ? "DeclarativeCompositionProjection.Refresh" : "DeclarativeCompositionProjection.Project";

    public static CompositionScenarioResult Execute(string caseId)
    {
        var clock = new ScenarioClock();
        var profiles = new CompositionProfileAdmission();
        var contexts = new CompositionOperationContextAdmission(clock);
        var projector = new DeclarativeCompositionProjector(profiles, contexts, clock);
        var source = caseId switch
        {
            "GRAM-DIAGNOSTICS" => ProjectionFixtures.CompileFailed(clock),
            "GRAM-UNSUPPORTED" => ProjectionFixtures.CompileUnsupported(clock),
            "GRAM-SKEWED" => ProjectionFixtures.CompileSkewed(clock),
            "GRAM-COLLISION" => ProjectionFixtures.CompileCollision(clock),
            _ => ProjectionFixtures.CompileNested(timeProvider: clock)
        };
        var sidecar = Sidecar(0);
        var beforeSource = source.Ir?.CanonicalBytes.ToArray();
        var result = projector.Project(source, ProjectionFixtures.Profile(), ProjectionFixtures.Context(clock), sidecar);
        bool passed;
        string assertion;
        switch (caseId)
        {
            case "GRAM-NESTED-OPAQUE":
                passed = result is CompositionProjectionResult.Fresh nested && nested.Document.Nodes.Count == 3 &&
                    nested.Document.Groups.Count == 2 && nested.Document.Groups.Count(group => group.ParentGroupId is not null) == 1 &&
                    nested.Elements.Count(element => element.Kind == "component" && element.IsOpaque) == 1;
                assertion = "Recursive groups retain one nested parent and one opaque component.";
                break;
            case "GRAM-PUBLIC-EXPORTS":
                passed = result is CompositionProjectionResult.Fresh exported && source.Ir is { } exportedIr &&
                    exported.Elements.Count(element => element.Kind == "surface") == 4 &&
                    exportedIr.PublicView.Root.PublicRequirements.Count == 1 &&
                    exportedIr.PublicView.Root.PublicAssignments.Count == 1 &&
                    exportedIr.PublicView.Root.PublicImplementationReferences.Count == 1 &&
                    exportedIr.PublicView.Root.Children.Single().Surfaces.All(surface => surface.ExportChain.Count == 2) &&
                    exported.PublicFacts.GetRawText().Contains(ProjectionFixtures.Address(25).CanonicalText, StringComparison.Ordinal);
                assertion = "Compiler-exported surfaces and public requirement, assignment and implementation facts survive projection.";
                break;
            case "GRAM-DIAGNOSTICS":
                passed = result is CompositionProjectionResult.Failed && !source.IsAccepted &&
                    result.SourceDiagnostics.SequenceEqual(source.Diagnostics.Diagnostics) && result.SourceDiagnostics.Count > 0;
                assertion = "Rejected producer diagnostic fields and order are retained without a diagram.";
                break;
            case "GRAM-COLLISION":
                passed = !source.IsAccepted && result is CompositionProjectionResult.Failed &&
                    result.SourceDiagnostics.SequenceEqual(source.Diagnostics.Diagnostics) &&
                    result.SourceDiagnostics.Count > 0;
                assertion = "An actual producer export collision never publishes a partial accepted diagram.";
                break;
            case "GRAM-IR-ANCHOR":
                passed = result is CompositionProjectionResult.Fresh anchored && source.Ir is { } anchoredIr &&
                    anchored.SourceAnchor!.RootIdentity == anchoredIr.RootIdentity && anchored.SourceAnchor.RootRevision == 1 &&
                    anchored.SourceAnchor.ContentDigest == anchoredIr.ContentIdentity.DigestHex &&
                    anchored.SourceAnchor.Compatibility.Profile == anchoredIr.Profile.Value &&
                    anchored.SourceAnchor.Compatibility.ContractVersion == anchoredIr.ContractVersion.Value &&
                    anchored.SourceAnchor.Compatibility.CompilerVersion == anchoredIr.ContentIdentity.CompilerVersion.Value;
                assertion = "Root and enclosing IR identities retain the exact compiler, contract and profile tuple.";
                break;
            case "GRAM-INERT-SURFACES":
                passed = result is CompositionProjectionResult.Fresh inert && inert.Elements.Count(element => element.Kind == "surface") == 4 &&
                    inert.Document.Ports.Count == 0 && inert.Document.Edges.Count == 0 &&
                    inert.Document.Nodes.All(node => !node.LabelEditable && !node.Resizable && !node.Rotatable &&
                        node.Properties!.All(property => property.Mode == DiagramPropertyModes.Display && !property.Connectable && property.Editor is null));
                assertion = "All four declared surface kinds are display-only without connectors, editing or control handlers.";
                break;
            case "GRAM-SAME-SOURCE-DELTA":
                if (result is not CompositionProjectionResult.Fresh previous) return new(result, false, "Initial actual projection was not Fresh.");
                var element = previous.Elements.Single(item => item.Kind == "component" && item.IsOpaque);
                var node = previous.Document.Nodes.Single(item => item.Id == element.DiagramId);
                result = projector.Refresh(previous, source, ProjectionFixtures.Profile(), ProjectionFixtures.Context(clock),
                    Sidecar(1, [new(element.SourceKey, node.X + 10, node.Y + 10, node.Width, node.Height)]));
                passed = result is CompositionProjectionResult.Fresh changed && changed.Delta is { BaseRevision: 0, Revision: 1 } &&
                    changed.Delta.Nodes.Any(item => item.Id == node.Id && item.X == node.X + 10) &&
                    changed.SourceAnchor!.Equals(previous.SourceAnchor) && changed.PublicFacts.GetRawText() == previous.PublicFacts.GetRawText();
                assertion = "A same-source revision emits only the actual presentation delta and preserves public facts.";
                break;
            case "GRAM-SOURCE-STALE":
                if (result is not CompositionProjectionResult.Fresh old) return new(result, false, "Initial actual projection was not Fresh.");
                var revised = ProjectionFixtures.CompileNested(2, clock);
                result = projector.Refresh(old, revised, ProjectionFixtures.Profile(), ProjectionFixtures.Context(clock), Sidecar(0));
                passed = result is CompositionProjectionResult.Stale stale && stale.FullReprojectionRequired &&
                    ReferenceEquals(stale.LastKnown, old) && stale.SourceAnchor!.RootRevision == 2 &&
                    projector.Project(revised, ProjectionFixtures.Profile(), ProjectionFixtures.Context(clock), Sidecar(0)) is CompositionProjectionResult.Fresh;
                assertion = "Semantic revision discontinuity requires explicit full reprojection and marks retained output stale.";
                break;
            case "GRAM-UNSUPPORTED":
                passed = !source.IsAccepted && result is CompositionProjectionResult.Unsupported && result.SourceDiagnostics.SequenceEqual(source.Diagnostics.Diagnostics);
                assertion = "An actual unsupported producer tuple remains Unsupported with original diagnostics.";
                break;
            case "GRAM-SKEWED":
                passed = !source.IsAccepted && result is CompositionProjectionResult.Skewed && result.SourceDiagnostics.SequenceEqual(source.Diagnostics.Diagnostics);
                assertion = "An actual incompatible producer source remains Skewed with original diagnostics.";
                break;
            case "GRAM-PRIVATE-LEAK":
                var privateKey = CompositionDiagramIdentity.Encode("surface", ProjectionFixtures.Address(30).CanonicalText);
                result = projector.Project(source, ProjectionFixtures.Profile(), ProjectionFixtures.Context(clock),
                    Sidecar(1, [new(privateKey, 1, 2, 100, 100)]));
                passed = result is CompositionProjectionResult.Fresh filtered &&
                    !filtered.PublicFacts.GetRawText().Contains("PRIVATE-DO-NOT-DISPLAY", StringComparison.Ordinal) &&
                    filtered.Elements.All(item => item.SourceIdentity != ProjectionFixtures.Address(30).CanonicalText) &&
                    filtered.Document.Nodes.Count == 3;
                assertion = "A sidecar naming private source data cannot introduce private facts or elements; the orphan is ignored.";
                break;
            case "GRAM-CANCEL":
                result = projector.Project(source, ProjectionFixtures.Profile(), ProjectionFixtures.Context(clock, new CancellationToken(true)), sidecar);
                passed = source.IsAccepted && result is CompositionProjectionResult.Failed;
                assertion = "Cancellation prevents a newly accepted projection.";
                break;
            case "GRAM-DEADLINE":
                result = projector.Project(source, ProjectionFixtures.Profile(), new("ghostagram-expired", clock.GetUtcNow(), CancellationToken.None), sidecar);
                passed = source.IsAccepted && result is CompositionProjectionResult.Failed;
                assertion = "An expired deadline prevents a newly accepted projection.";
                break;
            case "GRAM-NONMUTATION":
                passed = result is CompositionProjectionResult.Fresh && beforeSource is not null &&
                    beforeSource.AsSpan().SequenceEqual(source.Ir!.CanonicalBytes.Span) && sidecar.RawEntryCount == 0 &&
                    sidecar.Revision == 0 && sidecar.Layout.Count == 0;
                assertion = "Actual source canonical bytes and immutable input presentation are unchanged.";
                break;
            case "GRAM-NO-RELABEL":
                if (result is not CompositionProjectionResult.Fresh prior) return new(result, false, "Initial actual projection was not Fresh.");
                var rejected = ProjectionFixtures.CompileFailed(clock);
                result = projector.Refresh(prior, rejected, ProjectionFixtures.Profile(), ProjectionFixtures.Context(clock), Sidecar(1));
                passed = result is CompositionProjectionResult.Failed && result.SourceDiagnostics.SequenceEqual(rejected.Diagnostics.Diagnostics) &&
                    !CompositionConsumerProtocol.SerializeProjection(result).AsSpan().SequenceEqual(CompositionConsumerProtocol.SerializeProjection(prior));
                assertion = "A failed refresh never relabels the prior accepted diagram Fresh.";
                break;
            default: throw new ArgumentException("Unknown concrete projection case.", nameof(caseId));
        }
        return new(result, passed, assertion);

        CompositionPresentationSidecar Sidecar(long revision, IEnumerable<CompositionLayoutEntry>? layout = null)
        {
            var context = contexts.Admit(ProjectionFixtures.Context(clock), CompositionLimitProfile.ConformanceV1);
            var admission = CompositionPresentationSidecar.Admit(revision, layout ?? [], [], [], [], null,
                CompositionLimitProfile.ConformanceV1, context.Context!, clock);
            return admission.Sidecar ?? throw new InvalidOperationException("Local fixture sidecar failed admission.");
        }
    }

    // Fixed per-scenario time makes deadline boundaries repeatable without weakening real owner admission.
    private sealed class ScenarioClock : TimeProvider
    {
        private readonly DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
    }
}
