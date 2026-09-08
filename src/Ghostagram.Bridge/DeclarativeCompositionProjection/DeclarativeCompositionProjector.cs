using System.Text;
using System.Text.Json;
using Ghostagram.Core;
using Ghostworx.System.Composition;
using Ghostworx.System.Composition.Definitions;
using Ghostworx.System.Composition.Exchange;
using Ghostworx.System.Composition.Surfaces;

namespace Ghostagram.Bridge.DeclarativeCompositionProjection;

/// <summary>Pure consumer of the compiler-owned public view. It never compiles, resolves, or visits private definitions.</summary>
public sealed class DeclarativeCompositionProjector
{
    private readonly ICompositionProfileAdmission profiles;
    private readonly ICompositionOperationContextAdmission contexts;
    private readonly TimeProvider clock;
    internal static readonly JsonSerializerOptions Json = CompositionPresentationJson.CreateOptions();

    public DeclarativeCompositionProjector(ICompositionProfileAdmission profiles,
        ICompositionOperationContextAdmission contexts, TimeProvider? timeProvider = null)
    {
        this.profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        this.contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
        clock = timeProvider ?? TimeProvider.System;
    }

    public CompositionProjectionResult Project(CompositionCompilationResult source,
        CompositionLimitProfileDeclaration profile, CompositionOperationContextDeclaration context,
        CompositionPresentationSidecar sidecar) => Execute(null, () => Read(source), profile, context, sidecar);

    public CompositionProjectionResult Project(CompositionVerificationResult source,
        CompositionLimitProfileDeclaration profile, CompositionOperationContextDeclaration context,
        CompositionPresentationSidecar sidecar, MigrationLineage? lineage = null) =>
        Execute(null, () => Read(source, lineage), profile, context, sidecar);

    // These owner outcomes carry diagnostics, not a verified projection source. Accepted
    // Encode/Migrate callers must explicitly Verify; Inspect/Negotiate/GetExact successes
    // remain semantic successes without a projectable public view.
    public CompositionProjectionResult Project(CompositionDecodeResult source,
        CompositionLimitProfileDeclaration profile, CompositionOperationContextDeclaration context,
        CompositionPresentationSidecar sidecar) => Execute(null, () =>
        {
            ArgumentNullException.ThrowIfNull(source);
            return DiagnosticOnly(source.IsAccepted, source.Diagnostics);
        }, profile, context, sidecar);

    public CompositionProjectionResult Project(CompositionEncodeResult source,
        CompositionLimitProfileDeclaration profile, CompositionOperationContextDeclaration context,
        CompositionPresentationSidecar sidecar) => Execute(null, () =>
        {
            ArgumentNullException.ThrowIfNull(source);
            return DiagnosticOnly(source.IsAccepted, source.Diagnostics);
        }, profile, context, sidecar);

    public CompositionProjectionResult Project(CompositionMigrationResult source,
        CompositionLimitProfileDeclaration profile, CompositionOperationContextDeclaration context,
        CompositionPresentationSidecar sidecar) => Execute(null, () =>
        {
            ArgumentNullException.ThrowIfNull(source);
            return DiagnosticOnly(source.IsAccepted, source.Diagnostics);
        }, profile, context, sidecar);

    public CompositionProjectionResult Project(CompatibilityInspectionResult source,
        CompositionLimitProfileDeclaration profile, CompositionOperationContextDeclaration context,
        CompositionPresentationSidecar sidecar) => Execute(null, () =>
        {
            ArgumentNullException.ThrowIfNull(source);
            return DiagnosticOnly(source.IsAccepted, source.Diagnostics);
        }, profile, context, sidecar);

    public CompositionProjectionResult Project(CompatibilitySelectionResult source,
        CompositionLimitProfileDeclaration profile, CompositionOperationContextDeclaration context,
        CompositionPresentationSidecar sidecar) => Execute(null, () =>
        {
            ArgumentNullException.ThrowIfNull(source);
            return DiagnosticOnly(source.IsAccepted, source.Diagnostics);
        }, profile, context, sidecar);

    public CompositionProjectionResult Project(DefinitionReferenceResult source,
        CompositionLimitProfileDeclaration profile, CompositionOperationContextDeclaration context,
        CompositionPresentationSidecar sidecar) => Execute(null, () =>
        {
            ArgumentNullException.ThrowIfNull(source);
            return DiagnosticOnly(source.IsAccepted, source.Diagnostics);
        }, profile, context, sidecar);

    public CompositionProjectionResult Refresh(CompositionProjectionResult previous, CompositionCompilationResult source,
        CompositionLimitProfileDeclaration profile, CompositionOperationContextDeclaration context,
        CompositionPresentationSidecar sidecar) =>
        Execute(previous ?? throw new ArgumentNullException(nameof(previous)), () => Read(source), profile, context, sidecar);

    public CompositionProjectionResult Refresh(CompositionProjectionResult previous, CompositionVerificationResult source,
        CompositionLimitProfileDeclaration profile, CompositionOperationContextDeclaration context,
        CompositionPresentationSidecar sidecar, MigrationLineage? lineage = null) =>
        Execute(previous ?? throw new ArgumentNullException(nameof(previous)), () => Read(source, lineage), profile, context, sidecar);

    private CompositionProjectionResult Execute(CompositionProjectionResult? previous, Func<Source> read,
        CompositionLimitProfileDeclaration profileDeclaration, CompositionOperationContextDeclaration contextDeclaration,
        CompositionPresentationSidecar sidecar)
    {
        CompositionProjectionSourceAnchor? anchor = null;
        IReadOnlyList<CompositionDiagnostic> producer = [];
        var revision = sidecar?.Revision ?? 0;
        try
        {
            var profileResult = profiles.Admit(profileDeclaration);
            if (!profileResult.IsAccepted) return ProducerFailure(profileResult.Diagnostics.Diagnostics, revision);
            var profile = profileResult.Profile!;
            var contextResult = contexts.Admit(contextDeclaration, profile);
            if (!contextResult.IsAccepted) return ProducerFailure(contextResult.Diagnostics.Diagnostics, revision);
            var budget = new ProjectionBudget(profile, contextResult.Context!, clock);
            budget.Check();
            if (sidecar is null) throw new ProjectionFailure("SIDECAR", "An admitted presentation sidecar is required.");
            // Recheck the immutable sidecar under this invocation's admitted context. Admission
            // ports are not called again; no previous deadline or cancellation token is reused.
            budget.Sidecar(sidecar);
            var source = read();
            budget.Diagnostics(source.Diagnostics.Diagnostics);
            producer = source.Diagnostics.Diagnostics;
            if (!source.Accepted) return ProducerFailure(producer, revision, source.Unsupported);
            if (source.Ir is not { } ir)
                return new CompositionProjectionResult.Unsupported(null, revision, producer,
                    [Local("SOURCE-KIND", "The accepted owner result has no verified compiler public view for diagram projection.")]);
            // This property is supplied and identity-bound by System, never supplied independently by a caller.
            var view = ir.PublicView;
            if (view is null) throw new ProjectionFailure("PUBLIC-VIEW", "The accepted IR has no producer public view.");
            if (view.RootDefinitionIdentity != ir.RootIdentity)
                throw new ProjectionFailure("ANCHOR-COLLISION", "The public root and enclosing IR identity differ.");
            if (ir.Profile.Value != profile.Identity.Value)
                return new CompositionProjectionResult.Skewed(null, revision, producer,
                    [Local("PROFILE-SKEW", "The source profile differs from the admitted projection invocation.")]);
            budget.Count(ir.Provenance.Count, profile.MaxDefinitions);
            foreach (var provenance in ir.Provenance) budget.Value(provenance, profile.MaxResultBytes);
            budget.Count(ir.ReferenceProvenance.Count, profile.MaxVisitedIdentities);
            budget.Value(ir.ReferenceProvenance, profile.MaxResultBytes);
            if (source.Lineage is { } lineage)
            {
                budget.Count(lineage.Entries.Count, profile.MaxMigrationSteps);
                budget.Value(lineage, profile.MaxResultBytes);
            }
            anchor = new(view.RootDefinitionIdentity, view.RootDefinitionRevision, ir.ContentIdentity,
                source.Tuple!, ir.Provenance, source.Lineage, ir.ReferenceProvenance);
            var prior = previous switch
            {
                CompositionProjectionResult.Fresh fresh => fresh,
                CompositionProjectionResult.Stale stale => stale.LastKnown,
                _ => null
            };
            if (previous is not null)
            {
                if (previous is not CompositionProjectionResult.Fresh || prior is null ||
                    !prior.SourceAnchor!.Equals(anchor))
                {
                    var staleResult = new CompositionProjectionResult.Stale(anchor, revision, prior, producer,
                        [Local("SOURCE-STALE", "The source changed or the previous result was not fresh; call Project for a full projection.")]);
                    budget.Value(staleResult, Math.Min(profile.MaxResultBytes, CompositionPresentationProfile.MaximumProjectionUtf8Bytes));
                    budget.Check();
                    return staleResult;
                }
                if (CompositionProjectionFreshness.Compare(prior.SourceAnchor!, prior.PresentationRevision, anchor, revision)
                    == CompositionRefreshDisposition.InvalidPresentationRevision)
                    throw new ProjectionFailure("PRESENTATION-REVISION", "Presentation revision cannot move backwards.");
            }

            var projection = new PublicViewMapping(budget, sidecar).Map(view, anchor);
            var semanticComparison = budget.Value(new
            {
                projection.Elements,
                projection.PublicFacts,
                Nodes = projection.Document.Nodes.Select(node => new { node.Id, node.Label, node.TypeId, node.GroupId, node.Properties }),
                Groups = projection.Document.Groups.Select(group => new { group.Id, group.Label, group.ParentGroupId })
            }, profile.MaxResultBytes).GetRawText();
            var presentationComparison = budget.Value(new { sidecar.Layout, sidecar.Waypoints, sidecar.Display,
                sidecar.Selection, sidecar.Viewport }, profile.MaxResultBytes).GetRawText();
            if (prior is not null && previous is CompositionProjectionResult.Fresh)
            {
                if (!StringComparer.Ordinal.Equals(prior.SemanticComparison, semanticComparison))
                    throw new ProjectionFailure("ANCHOR-COLLISION", "One source anchor produced different public semantic facts.");
                if (revision == prior.PresentationRevision && prior.PresentationComparison != presentationComparison)
                    throw new ProjectionFailure("PRESENTATION-REVISION", "Different presentation state requires a new presentation revision.");
            }
            var delta = prior is null ? null : Difference(prior, projection.Document, revision);
            var result = new CompositionProjectionResult.Fresh(anchor, revision, projection.Document, projection.Elements,
                projection.PublicFacts, semanticComparison, presentationComparison, delta, producer, projection.Diagnostics);
            // L32 belongs to consumer envelope observations. It is not a layout allowance.
            budget.Value(result, Math.Min(profile.MaxResultBytes, CompositionPresentationProfile.MaximumProjectionUtf8Bytes));
            budget.Check();
            return result;
        }
        catch (ProjectionFailure failure)
        { return new CompositionProjectionResult.Failed(anchor, revision, producer, [Local(failure.Code, failure.Message)]); }
        catch (OperationCanceledException)
        { return new CompositionProjectionResult.Failed(anchor, revision, producer, [Local("CANCELLED", "Projection was cancelled.")]); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException or OverflowException or NotSupportedException)
        { return new CompositionProjectionResult.Failed(anchor, revision, producer, [Local("INVALID", "The source could not be projected completely.")]); }
    }

    private static CompositionProjectionDiagnostic Local(string code, string message) => new("GRAM-COMP-" + code, "projection", message);

    private static CompositionProjectionResult ProducerFailure(IReadOnlyList<CompositionDiagnostic> diagnostics,
        long revision, bool unsupported = false)
    {
        if (unsupported || diagnostics.Any(item => item.Code == CompositionDiagnosticCodes.Unsupported ||
            item.Code is "CMP-CONTRACT-UNSUPPORTED" or "CMP-COMPILER-UNSUPPORTED" or "CMP-PROFILE-UNSUPPORTED"))
            return new CompositionProjectionResult.Unsupported(null, revision, diagnostics, []);
        if (diagnostics.Any(item => item.Code is "CMP-PROFILE-SKEW" or "CMP-REFERENCE-SKEW" or
            "CMP-VARIABLE-INCOMPATIBLE" or "CMP-IMPLEMENTATION-INCOMPATIBLE" or "CMP-EXTENSION-INCOMPATIBLE"))
            return new CompositionProjectionResult.Skewed(null, revision, diagnostics, []);
        return new CompositionProjectionResult.Failed(null, revision, diagnostics, []);
    }

    private static Source Read(CompositionCompilationResult source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var ir = source.Ir;
        var tuple = ir is null ? null : CompatibilityTuple.Current(CompositionExchangeRecordKind.Ir) with
        {
            ContractVersion = ir.ContractVersion.Value, CompilerVersion = ir.ContentIdentity.CompilerVersion.Value,
            Profile = ir.Profile.Value, IdentityProfile = ir.ContentIdentity.Algorithm
        };
        return new(source.IsAccepted, source.Status == CompositionResultStatus.Unsupported, ir, tuple, null, source.Diagnostics);
    }
    private static Source Read(CompositionVerificationResult source, MigrationLineage? lineage)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new(source.IsAccepted, false, source.Document?.Record as CompositionIr, source.Document?.Compatibility,
            lineage, source.Diagnostics);
    }
    private sealed record Source(bool Accepted, bool Unsupported, CompositionIr? Ir, CompatibilityTuple? Tuple,
        MigrationLineage? Lineage, CompositionDiagnosticSet Diagnostics);

    private static Source DiagnosticOnly(bool accepted, CompositionDiagnosticSet diagnostics) =>
        new(accepted, false, null, null, null, diagnostics);

    private static CompositionPresentationDelta Difference(CompositionProjectionResult.Fresh prior,
        DiagramDocument document, long revision)
    {
        var nodes = prior.Document.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var groups = prior.Document.Groups.ToDictionary(group => group.Id, StringComparer.Ordinal);
        return new(prior.PresentationRevision, revision,
            document.Nodes.Where(node => nodes[node.Id].X != node.X || nodes[node.Id].Y != node.Y ||
                nodes[node.Id].Width != node.Width || nodes[node.Id].Height != node.Height ||
                nodes[node.Id].Presentation?.DisplayMode != node.Presentation?.DisplayMode)
                .Select(node => new CompositionNodePresentationChange(node.Id, node.X, node.Y, node.Width, node.Height,
                    node.Presentation?.DisplayMode ?? DiagramNodeDisplayModes.Expanded)),
            document.Groups.Where(group => groups[group.Id].X != group.X || groups[group.Id].Y != group.Y ||
                groups[group.Id].Width != group.Width || groups[group.Id].Height != group.Height || groups[group.Id].Collapsed != group.Collapsed)
                .Select(group => new CompositionGroupPresentationChange(group.Id, group.X, group.Y, group.Width, group.Height, group.Collapsed)),
            document.Selection, new(document.Viewport.X, document.Viewport.Y, document.Viewport.Zoom));
    }
}

internal sealed class ProjectionFailure(string code, string message) : Exception(message)
{
    internal string Code { get; } = code;
}

internal sealed class ProjectionBudget(CompositionLimitProfile profile, CompositionOperationContext context, TimeProvider clock)
{
    internal CompositionLimitProfile Profile { get; } = profile;
    internal void Check()
    {
        var now = clock.GetUtcNow();
        context.Cancellation.ThrowIfCancellationRequested();
        if (now >= context.AbsoluteDeadlineUtc)
            throw new ProjectionFailure("DEADLINE", "Projection deadline expired.");
    }
    internal void Count(int count, int maximum)
    { Check(); if (count < 0 || count > maximum) throw new ProjectionFailure("BOUND", "A public projection dimension exceeds its admitted bound."); }
    internal void Text(string? value, int? maximum = null)
    {
        Check();
        if (value is not null && Encoding.UTF8.GetByteCount(value) > (maximum ?? Profile.MaxStringUtf8Bytes))
            throw new ProjectionFailure("BOUND", "A public string exceeds its admitted bound.");
    }
    internal JsonElement Value(object value, long maximum)
    {
        Check();
        using var stream = new LimitedJsonStream(maximum);
        JsonSerializer.Serialize(stream, value, value.GetType(), DeclarativeCompositionProjector.Json);
        Check();
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }
    internal void Diagnostics(IReadOnlyList<CompositionDiagnostic> diagnostics)
    {
        Count(diagnostics.Count, Profile.MaxDiagnostics);
        foreach (var diagnostic in diagnostics)
        {
            Check(); Text(diagnostic.Detail, Profile.MaxDiagnosticDetailUtf8Bytes);
            Text(diagnostic.Category); Text(diagnostic.Code); Text(diagnostic.Path); Text(diagnostic.CorrelationId);
            Text(diagnostic.Identity); Text(diagnostic.Revision);
        }
        Value(diagnostics, Profile.MaxDiagnosticBytes);
    }
    internal void Sidecar(CompositionPresentationSidecar sidecar)
    {
        Count(sidecar.RawEntryCount, CompositionPresentationProfile.MaximumRawSidecarEntries);
        foreach (var id in sidecar.Layout.Select(item => item.SourceIdentity).Concat(sidecar.Waypoints.Select(item => item.SourceIdentity))
            .Concat(sidecar.Display.Select(item => item.SourceIdentity)).Concat(sidecar.Selection))
            Text(id, CompositionDiagramIdentity.MaximumSourceKeyUtf8Bytes(Profile));
        Check();
    }
    private sealed class LimitedJsonStream(long maximum) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        { Require(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer)
        { Require(buffer.Length); base.Write(buffer); }
        public override void WriteByte(byte value)
        { Require(1); base.WriteByte(value); }
        private void Require(int count)
        {
            if (maximum <= 0 || Position + count > maximum)
                throw new ProjectionFailure("BOUND", "Serialized public projection exceeds its finite byte bound.");
        }
    }
}
