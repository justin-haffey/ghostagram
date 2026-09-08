using System.Text.Json;
using Ghostagram.Core;
using Ghostworx.System.Composition;

namespace Ghostagram.Bridge.DeclarativeCompositionProjection;

/// <summary>A public source locator kept separately from its deterministic diagram identifier.</summary>
public sealed record CompositionProjectionElement(string DiagramId, string SourceKey, string Kind,
    string SourceIdentity, string DefinitionIdentity, long DefinitionRevision, string OwningBoundaryIdentity,
    string? ParentBoundaryIdentity, bool IsOpaque);

public sealed record CompositionNodePresentationChange(string Id, double X, double Y, double Width, double Height,
    string DisplayMode);
public sealed record CompositionGroupPresentationChange(string Id, double X, double Y, double Width, double Height,
    bool Collapsed);

/// <summary>No semantic labels, properties, hierarchy, routes, or executable operations can occur in this delta.</summary>
public sealed class CompositionPresentationDelta
{
    internal CompositionPresentationDelta(long baseRevision, long revision,
        IEnumerable<CompositionNodePresentationChange> nodes, IEnumerable<CompositionGroupPresentationChange> groups,
        IEnumerable<string> selection, CompositionViewport viewport)
    {
        BaseRevision = baseRevision; Revision = revision;
        Nodes = Array.AsReadOnly(nodes.ToArray()); Groups = Array.AsReadOnly(groups.ToArray());
        Selection = Array.AsReadOnly(selection.ToArray()); Viewport = viewport;
    }
    public long BaseRevision { get; }
    public long Revision { get; }
    public IReadOnlyList<CompositionNodePresentationChange> Nodes { get; }
    public IReadOnlyList<CompositionGroupPresentationChange> Groups { get; }
    public IReadOnlyList<string> Selection { get; }
    public CompositionViewport Viewport { get; }
}

/// <summary>
/// Closed result union. Only the projector can create an accepted snapshot; failed and
/// stale outcomes cannot carry a newly accepted diagram. Producer diagnostics retain their order and fields.
/// </summary>
public abstract class CompositionProjectionResult
{
    private CompositionProjectionResult(CompositionProjectionStatus status, CompositionProjectionSourceAnchor? anchor,
        long presentationRevision, IReadOnlyList<CompositionDiagnostic> sourceDiagnostics,
        IReadOnlyList<CompositionProjectionDiagnostic> diagnostics)
    {
        Status = status; SourceAnchor = anchor; PresentationRevision = presentationRevision;
        SourceDiagnostics = Array.AsReadOnly(sourceDiagnostics.ToArray());
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }
    public CompositionProjectionStatus Status { get; }
    // An unaccepted producer result may have no IR and therefore no honest content anchor.
    public CompositionProjectionSourceAnchor? SourceAnchor { get; }
    public long PresentationRevision { get; }
    public IReadOnlyList<CompositionDiagnostic> SourceDiagnostics { get; }
    public IReadOnlyList<CompositionProjectionDiagnostic> Diagnostics { get; }

    public sealed class Fresh : CompositionProjectionResult
    {
        internal Fresh(CompositionProjectionSourceAnchor anchor, long revision, DiagramDocument document,
            IEnumerable<CompositionProjectionElement> elements, JsonElement publicFacts,
            string semanticComparison, string presentationComparison, CompositionPresentationDelta? delta,
            IReadOnlyList<CompositionDiagnostic> sourceDiagnostics, IReadOnlyList<CompositionProjectionDiagnostic> diagnostics)
            : base(CompositionProjectionStatus.Fresh, anchor, revision, sourceDiagnostics, diagnostics)
        {
            Document = document; Elements = Array.AsReadOnly(elements.ToArray()); PublicFacts = publicFacts.Clone();
            SemanticComparison = semanticComparison; PresentationComparison = presentationComparison; Delta = delta;
        }
        public DiagramDocument Document { get; }
        public IReadOnlyList<CompositionProjectionElement> Elements { get; }
        public JsonElement PublicFacts { get; }
        public CompositionPresentationDelta? Delta { get; }
        internal string SemanticComparison { get; }
        internal string PresentationComparison { get; }
    }

    public sealed class Stale : CompositionProjectionResult
    {
        internal Stale(CompositionProjectionSourceAnchor anchor, long revision, Fresh? lastKnown,
            IReadOnlyList<CompositionDiagnostic> sourceDiagnostics, IReadOnlyList<CompositionProjectionDiagnostic> diagnostics)
            : base(CompositionProjectionStatus.Stale, anchor, revision, sourceDiagnostics, diagnostics) => LastKnown = lastKnown;
        public bool FullReprojectionRequired => true;
        // This is explicitly last-known output; Status on this result remains Stale.
        public Fresh? LastKnown { get; }
    }

    public sealed class Unsupported : CompositionProjectionResult
    {
        internal Unsupported(CompositionProjectionSourceAnchor? anchor, long revision,
            IReadOnlyList<CompositionDiagnostic> sourceDiagnostics, IReadOnlyList<CompositionProjectionDiagnostic> diagnostics)
            : base(CompositionProjectionStatus.Unsupported, anchor, revision, sourceDiagnostics, diagnostics) { }
    }
    public sealed class Skewed : CompositionProjectionResult
    {
        internal Skewed(CompositionProjectionSourceAnchor? anchor, long revision,
            IReadOnlyList<CompositionDiagnostic> sourceDiagnostics, IReadOnlyList<CompositionProjectionDiagnostic> diagnostics)
            : base(CompositionProjectionStatus.Skewed, anchor, revision, sourceDiagnostics, diagnostics) { }
    }
    public sealed class Failed : CompositionProjectionResult
    {
        internal Failed(CompositionProjectionSourceAnchor? anchor, long revision,
            IReadOnlyList<CompositionDiagnostic> sourceDiagnostics, IReadOnlyList<CompositionProjectionDiagnostic> diagnostics)
            : base(CompositionProjectionStatus.Failed, anchor, revision, sourceDiagnostics, diagnostics) { }
    }
}
