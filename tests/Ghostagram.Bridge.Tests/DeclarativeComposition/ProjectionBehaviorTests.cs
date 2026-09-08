using System.Text.Json;
using Ghostagram.Bridge.DeclarativeCompositionProjection;
using Ghostagram.Core;
using Ghostworx.System.Composition;
using Ghostworx.System.Composition.Compiler;
using Ghostworx.System.Composition.Surfaces;

namespace Ghostagram.Bridge.Tests.DeclarativeComposition;

public static class ProjectionBehaviorTests
{
    public static void Run()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero));
        var profiles = new CompositionProfileAdmission();
        var contexts = new CompositionOperationContextAdmission(clock);
        var projector = new DeclarativeCompositionProjector(profiles, contexts, clock);
        var source = ProjectionFixtures.CompileNested(timeProvider: clock);
        Require(source.IsAccepted, "The real nested producer fixture must compile: " +
            string.Join(",", source.Diagnostics.Diagnostics.Select(item => item.Code + ":" + item.Detail)));
        var ir = source.Ir!;
        var originalBytes = ir.CanonicalBytes.ToArray();
        var fresh = Fresh(projector.Project(source, ProjectionFixtures.Profile(), ProjectionFixtures.Context(clock), Sidecar(0)));
        Require(fresh.Document.Nodes.Count == 3 && fresh.Document.Groups.Count == 2,
            "The producer nested hierarchy and opaque component must remain distinct.");
        Require(fresh.Elements.Count(item => item.Kind == "surface") == 4 &&
            ir.PublicView.Root.Children.Single().Surfaces.Select(item => item.Declaration.Kind).Distinct().Count() == 4,
            "Every actual producer surface kind must appear as an inert display row.");
        Require(fresh.Document.Ports.Count == 0 && fresh.Document.Edges.Count == 0 &&
            fresh.Document.Nodes.All(node => !node.Resizable && !node.Rotatable && !node.LabelEditable &&
                node.Properties!.All(property => property.Mode == DiagramPropertyModes.Display && !property.Connectable && property.Editor is null)),
            "Declared surfaces must never acquire runtime connectors or editing controls.");
        Require(fresh.Elements.Single(item => item.Kind == "component" && item.IsOpaque).DefinitionIdentity == ProjectionFixtures.Address(3).CanonicalText,
            "Opaque boundary identity must remain visible without internal facts.");
        Require(!fresh.PublicFacts.GetRawText().Contains("PRIVATE-DO-NOT-DISPLAY", StringComparison.Ordinal),
            "A private declaration must never appear in public facts.");
        Require(fresh.SourceAnchor!.ContentDigest == ir.ContentIdentity.DigestHex && fresh.SourceAnchor.RootRevision == 1,
            "Projection freshness must use the enclosing IR content identity and root revision.");
        Require(fresh.PublicFacts.GetRawText().Contains("\"kind\":\"Int64\",\"value\":1", StringComparison.Ordinal),
            "Public provenance metadata must retain typed semantic content.");
        using (var serialized = JsonDocument.Parse(CompositionProjectionJson.Serialize(fresh)))
        {
            var anchor = serialized.RootElement.GetProperty("sourceAnchor");
            Require(serialized.RootElement.GetProperty("status").GetString() == "Fresh" &&
                anchor.GetProperty("rootIdentity").GetString() == ir.RootIdentity.CanonicalText &&
                anchor.GetProperty("provenance").GetArrayLength() == ir.Provenance.Count,
                "Public serialization must expose string status, canonical root identity and copied provenance facts.");
        }

        var opaque = fresh.Elements.Single(item => item.Kind == "component" && item.IsOpaque);
        var node = fresh.Document.Nodes.Single(item => item.Id == opaque.DiagramId);
        var moved = new CompositionLayoutEntry(opaque.SourceKey, node.X + 16, node.Y + 16, node.Width, node.Height);
        var changedPresentation = Sidecar(1, [moved]);
        var refreshed = Fresh(projector.Refresh(fresh, source, ProjectionFixtures.Profile(), ProjectionFixtures.Context(clock), changedPresentation));
        Require(refreshed.Delta is { BaseRevision: 0, Revision: 1 } && refreshed.Delta.Nodes.Any(item => item.Id == node.Id),
            "A same-source geometry change must yield a presentation delta.");
        Require(fresh.SourceAnchor.Equals(refreshed.SourceAnchor) && fresh.PublicFacts.GetRawText() == refreshed.PublicFacts.GetRawText(),
            "Presentation refresh must preserve exact source facts and anchor.");
        Require(projector.Refresh(fresh, source, ProjectionFixtures.Profile(), ProjectionFixtures.Context(clock), Sidecar(0, [moved]))
            is CompositionProjectionResult.Failed, "Changed presentation cannot reuse an unchanged presentation revision.");

        var changedSource = ProjectionFixtures.CompileNested(2, clock);
        Require(changedSource.IsAccepted, "Changed producer fixture must compile.");
        var stale = projector.Refresh(refreshed, changedSource, ProjectionFixtures.Profile(), ProjectionFixtures.Context(clock), Sidecar(0));
        Require(stale is CompositionProjectionResult.Stale { FullReprojectionRequired: true } old && ReferenceEquals(old.LastKnown, refreshed),
            "Changed source wins over decreasing presentation revision and retains only explicitly last-known output.");
        Require(projector.Refresh(stale, changedSource, ProjectionFixtures.Profile(), ProjectionFixtures.Context(clock), Sidecar(1))
            is CompositionProjectionResult.Stale, "A stale result requires explicit full Project, never an implicit fresh refresh.");
        Fresh(projector.Project(changedSource, ProjectionFixtures.Profile(), ProjectionFixtures.Context(clock), Sidecar(1)));

        CheckFailure(ProjectionFixtures.CompileUnsupported(clock), CompositionProjectionStatus.Unsupported);
        CheckFailure(ProjectionFixtures.CompileSkewed(clock), CompositionProjectionStatus.Skewed);
        CheckFailure(ProjectionFixtures.CompileFailed(clock), CompositionProjectionStatus.Failed);
        CheckFailure(ProjectionFixtures.CompileCancelled(clock), CompositionProjectionStatus.Failed);
        CheckFailure(ProjectionFixtures.CompileCollision(clock), CompositionProjectionStatus.Failed);
        var privateAttempt = Fresh(projector.Project(source, ProjectionFixtures.Profile(), ProjectionFixtures.Context(clock),
            Sidecar(0, [new(ProjectionFixtures.Address(30).CanonicalText, 10, 10, 1000, 1000)])));
        Require(privateAttempt.Diagnostics.Any(item => item.Code == "GRAM-COMP-PRESENTATION-ORPHANS") &&
            !CompositionProjectionJson.Serialize(privateAttempt).Contains("PRIVATE-DO-NOT-DISPLAY", StringComparison.Ordinal) &&
            privateAttempt.Document.Nodes.Count == fresh.Document.Nodes.Count,
            "An orphan layout targeting a private source identity cannot reveal or create that declaration.");
        Require(projector.Project(source, ProjectionFixtures.Profile(), ProjectionFixtures.Context(clock, new CancellationToken(true)), Sidecar(0))
            is CompositionProjectionResult.Failed, "A cancelled invocation must not publish Fresh output.");
        Require(projector.Project(source, ProjectionFixtures.Profile(), new("expired", clock.GetUtcNow(), CancellationToken.None), Sidecar(0))
            is CompositionProjectionResult.Failed, "An expired invocation must not publish Fresh output.");
        Require(originalBytes.AsSpan().SequenceEqual(ir.CanonicalBytes.Span) && changedPresentation.Layout.Single() == moved,
            "Projection and refresh must not mutate producer bytes or admitted sidecars.");
        Require(fresh.Document.Nodes is not DiagramNode[] && fresh.Elements is not CompositionProjectionElement[],
            "Fresh snapshots must not expose mutable collection storage.");
        Require(((ICollection<DiagramNode>)fresh.Document.Nodes).IsReadOnly &&
            ((ICollection<DiagramGroup>)fresh.Document.Groups).IsReadOnly &&
            fresh.Document.Nodes.All(item => ((ICollection<DiagramNodeProperty>)item.Properties).IsReadOnly),
            "Nested diagram collection storage must be read-only, not merely typed as IReadOnlyList.");
        Console.WriteLine("PASS composition projection: real public hierarchy, inert surfaces, typed metadata, refresh and failure containment");

        CompositionPresentationSidecar Sidecar(long revision, IEnumerable<CompositionLayoutEntry>? layout = null)
        {
            var admitted = contexts.Admit(ProjectionFixtures.Context(clock), CompositionLimitProfile.ConformanceV1);
            var result = CompositionPresentationSidecar.Admit(revision, layout ?? [], [], [], [], null,
                CompositionLimitProfile.ConformanceV1, admitted.Context!, clock);
            Require(result.IsAccepted, "Fixture sidecar must be admitted: " + result.Diagnostic?.Code);
            return result.Sidecar!;
        }
        void CheckFailure(CompositionCompilationResult failure, CompositionProjectionStatus expected)
        {
            Require(!failure.IsAccepted, "Failure fixture must be rejected by the real producer.");
            var result = projector.Project(failure, ProjectionFixtures.Profile(), ProjectionFixtures.Context(clock), Sidecar(0));
            Require(result.Status == expected && result is not CompositionProjectionResult.Fresh &&
                result.SourceDiagnostics.SequenceEqual(failure.Diagnostics.Diagnostics),
                "Producer diagnostics must survive unchanged in the appropriate closed failure result: " + expected);
        }
    }

    private static CompositionProjectionResult.Fresh Fresh(CompositionProjectionResult result) =>
        result as CompositionProjectionResult.Fresh ?? throw new InvalidOperationException("Expected Fresh: " +
            result.Status + " " + string.Join(",", result.Diagnostics.Select(item => item.Code + ":" + item.Message)));
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    { public override DateTimeOffset GetUtcNow() => now; }
}
