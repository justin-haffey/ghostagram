using System.Text.Json;
using Ghostagram.Core;
using Ghostworx.System.Composition;
using Ghostworx.System.Composition.Definitions;
using Ghostworx.System.Composition.Surfaces;

namespace Ghostagram.Bridge.DeclarativeCompositionProjection;

internal sealed record MappedComposition(DiagramDocument Document,
    IReadOnlyList<CompositionProjectionElement> Elements, JsonElement PublicFacts,
    IReadOnlyList<CompositionProjectionDiagnostic> Diagnostics);

/// <summary>Only compiler-filtered public entries cross this mapper's boundary.</summary>
internal sealed class PublicViewMapping(ProjectionBudget budget, CompositionPresentationSidecar sidecar)
{
    private const double Padding = 28;
    private readonly List<DiagramNode> nodes = [];
    private readonly List<DiagramGroup> groups = [];
    private readonly List<CompositionProjectionElement> elements = [];
    private readonly List<JsonElement> facts = [];
    private readonly HashSet<string> ids = new(StringComparer.Ordinal);
    private readonly HashSet<string> sources = new(StringComparer.Ordinal);
    private readonly HashSet<string> definitions = new(StringComparer.Ordinal);
    private readonly HashSet<string> visited = new(StringComparer.Ordinal);
    private readonly HashSet<string> exports = new(StringComparer.Ordinal);
    private readonly Dictionary<DeclaredSurfaceKind, int> surfaceCounts = [];
    private readonly Dictionary<string, CompositionLayoutEntry> layout = sidecar.Layout.ToDictionary(item => item.SourceIdentity, StringComparer.Ordinal);
    private readonly Dictionary<string, CompositionDisplayEntry> display = sidecar.Display.ToDictionary(item => item.SourceIdentity, StringComparer.Ordinal);
    private int occurrences;
    private int requirements;
    private int assignments;
    private int implementations;

    internal MappedComposition Map(CompositionPublicView view, CompositionProjectionSourceAnchor anchor)
    {
        Visit(view.Root, null, 0, 40, 40);
        var selectable = elements.Where(element => element.Kind is "component" or "boundary")
            .ToDictionary(element => element.SourceKey, element => element.DiagramId, StringComparer.Ordinal);
        var selected = sidecar.Selection.Where(selectable.ContainsKey).Select(key => selectable[key]).ToArray();
        var orphanCount = sidecar.Layout.Count(item => !selectable.ContainsKey(item.SourceIdentity)) +
            sidecar.Display.Count(item => !selectable.ContainsKey(item.SourceIdentity)) +
            sidecar.Selection.Count(item => !selectable.ContainsKey(item)) + sidecar.Waypoints.Count;
        // F003 has no connector edges. Even known surface waypoints are presentation orphans,
        // not a request to synthesize a connection or executable route.
        CompositionProjectionDiagnostic[] diagnostics = orphanCount == 0 ? Array.Empty<CompositionProjectionDiagnostic>() :
            [new CompositionProjectionDiagnostic("GRAM-COMP-PRESENTATION-ORPHANS", "sidecar",
                $"Ignored {orphanCount} presentation entries without a compatible displayed element.")];
        var document = new DiagramDocument(CompositionDiagramIdentity.Encode("composition", anchor.RootIdentity.CanonicalText),
            Array.AsReadOnly(nodes.ToArray()), Array.Empty<DiagramPort>(), Array.Empty<DiagramEdge>(),
            Array.AsReadOnly(groups.ToArray()), new(sidecar.Viewport.X, sidecar.Viewport.Y, sidecar.Viewport.Zoom),
            Selection: Array.AsReadOnly(selected));
        var publicFacts = budget.Value(new { SourceAnchor = anchor, Components = facts }, budget.Profile.MaxResultBytes);
        budget.Check();
        return new(document, Array.AsReadOnly(elements.ToArray()), publicFacts, Array.AsReadOnly(diagnostics));
    }

    private Bounds Visit(CompositionPublicComponentView component, string? parentGroup, int depth, double x, double y)
    {
        budget.Check(); budget.Count(depth, budget.Profile.MaxRecursionDepth);
        budget.Count(++occurrences, checked(budget.Profile.MaxConstituents + budget.Profile.MaxRoots));
        definitions.Add(component.SourceDefinitionIdentity.CanonicalText);
        budget.Count(definitions.Count, budget.Profile.MaxDefinitions);
        Address(component.SourceDefinitionIdentity.CanonicalText); Address(component.OwningBoundaryIdentity.CanonicalText);
        if (component.SourceOccurrenceIdentity is { } occurrence) Address(occurrence.CanonicalText);
        if (component.ParentBoundaryIdentity is { } parent) Address(parent.CanonicalText);
        budget.Count(component.PublicOwnerPath.Count, checked(budget.Profile.MaxRecursionDepth + 1));
        foreach (var owner in component.PublicOwnerPath) Address(owner.CanonicalText);
        if (component.IsOpaque && (component.Children.Count != 0 || component.Surfaces.Count != 0 ||
            component.PublicRequirements.Count != 0 || component.PublicAssignments.Count != 0 || component.PublicImplementationReferences.Count != 0))
            throw new ProjectionFailure("PRIVATE-LEAK", "An opaque producer boundary disclosed internal facts.");
        if (!component.IsOpaque && component.Visibility == SurfaceVisibility.Private)
            throw new ProjectionFailure("PRIVATE-LEAK", "A private producer boundary was presented as visible.");
        var tuple = component.PublicOwnerPath.Select(owner => owner.CanonicalText)
            .Append((component.SourceOccurrenceIdentity ?? component.SourceDefinitionIdentity).CanonicalText).ToArray();
        var nodeId = CompositionDiagramIdentity.Encode("component", tuple);
        var nodeKey = nodeId;
        AddElement(nodeId, nodeKey, "component", component);
        var isGroup = component.Kind == ComponentDefinitionKind.Composite && !component.IsOpaque;
        var groupId = isGroup ? CompositionDiagramIdentity.Encode("boundary", tuple) : null;
        if (groupId is not null) AddElement(groupId, groupId, "boundary", component);

        var properties = new List<DiagramNodeProperty>();
        Row("definition", "Definition", component.SourceDefinitionIdentity.CanonicalText);
        Row("revision", "Definition revision", component.SourceDefinitionRevision);
        Row("boundary", "Owning boundary", component.OwningBoundaryIdentity.CanonicalText);
        if (component.SourceOccurrenceIdentity is { } occurrenceId) Row("occurrence", "Occurrence", occurrenceId.CanonicalText);
        if (component.ParentBoundaryIdentity is { } parentId) Row("parent", "Parent boundary", parentId.CanonicalText);
        Row("visibility", "Visibility", component.Visibility.ToString());
        Row("opaque", "Opaque boundary", component.IsOpaque);
        Row("provenance", "Provenance", component.Provenance);
        foreach (var export in component.ExportChain)
        {
            budget.Check(); Address(export.Identity.CanonicalText);
            exports.Add(export.Identity.CanonicalText); budget.Count(exports.Count, budget.Profile.MaxExports);
            Row("export-" + export.Identity.CanonicalText, "Approved export", export);
        }
        requirements = checked(requirements + component.PublicRequirements.Count);
        assignments = checked(assignments + component.PublicAssignments.Count);
        implementations = checked(implementations + component.PublicImplementationReferences.Count);
        budget.Count(requirements, budget.Profile.MaxRequirements); budget.Count(assignments, budget.Profile.MaxAssignments);
        budget.Count(implementations, budget.Profile.MaxImplementationReferences);
        foreach (var requirement in component.PublicRequirements)
        {
            if (requirement.Visibility == SurfaceVisibility.Private) throw new ProjectionFailure("PRIVATE-LEAK", "A private requirement reached the public view.");
            Address(requirement.Identity.CanonicalText);
            Row("requirement-" + requirement.Identity.CanonicalText, "Variable requirement", requirement);
        }
        foreach (var assignment in component.PublicAssignments)
        {
            Address(assignment.Identity.CanonicalText);
            Row("assignment-" + assignment.Identity.CanonicalText, "Variable assignment", assignment);
        }
        foreach (var implementation in component.PublicImplementationReferences)
        {
            Address(implementation.CandidateIdentity.CanonicalText);
            Row("implementation-" + implementation.CandidateIdentity.CanonicalText, "Implementation reference", implementation);
        }
        foreach (var surface in component.Surfaces)
        {
            budget.Check();
            var declaration = surface.Declaration;
            if (declaration.Visibility == SurfaceVisibility.Private)
                throw new ProjectionFailure("PRIVATE-LEAK", "A private surface reached the public view.");
            Address(declaration.Identity.CanonicalText); Address(declaration.Owner.CanonicalText);
            var count = surfaceCounts.GetValueOrDefault(declaration.Kind) + 1;
            surfaceCounts[declaration.Kind] = count;
            budget.Count(count, declaration.Kind switch
            {
                DeclaredSurfaceKind.Capability => budget.Profile.MaxCapabilities,
                DeclaredSurfaceKind.Operation => budget.Profile.MaxOperations,
                DeclaredSurfaceKind.IPort => budget.Profile.MaxPorts,
                DeclaredSurfaceKind.IControl => budget.Profile.MaxControls,
                _ => throw new ProjectionFailure("SURFACE", "Unknown public surface kind.")
            });
            budget.Text(declaration.Meaning);
            if (declaration.Input is { } input) budget.Text(input.CanonicalSchema, budget.Profile.MaxSchemaUtf8Bytes);
            if (declaration.Result is { } result) budget.Text(result.CanonicalSchema, budget.Profile.MaxSchemaUtf8Bytes);
            var surfaceId = CompositionDiagramIdentity.Encode("surface", tuple.Append(declaration.Identity.CanonicalText).ToArray());
            if (!ids.Add(surfaceId)) throw new ProjectionFailure("COLLISION", "A public surface identity is duplicated.");
            elements.Add(new(surfaceId, surfaceId, "surface", declaration.Identity.CanonicalText,
                component.SourceDefinitionIdentity.CanonicalText, surface.SourceDefinitionRevision,
                component.OwningBoundaryIdentity.CanonicalText, component.ParentBoundaryIdentity?.CanonicalText, false));
            properties.Add(Property(surfaceId, declaration.Kind + " declaration", new
            {
                Identity = declaration.Identity.CanonicalText, Owner = declaration.Owner.CanonicalText,
                Kind = declaration.Kind.ToString(), Visibility = declaration.Visibility.ToString(), declaration.Meaning,
                declaration.Input, declaration.Result, Direction = declaration.Direction?.ToString(),
                declaration.Preconditions, declaration.Effects, declaration.StateTransition, declaration.Control,
                declaration.Compatibility, surface.SourceDefinitionRevision, surface.PublicOwnerPath,
                surface.Provenance, surface.ExportChain
            }, budget.Profile.MaxResultBytes));
        }
        // Public facts contain only this already-filtered entry, never its raw definition.
        facts.Add(budget.Value(new
        {
            component.SourceDefinitionIdentity, component.SourceDefinitionRevision, component.SourceOccurrenceIdentity,
            component.OwningBoundaryIdentity, component.ParentBoundaryIdentity, component.PublicOwnerPath,
            Kind = component.Kind.ToString(), Visibility = component.Visibility.ToString(), component.IsOpaque,
            component.Provenance, component.ExportChain, component.PublicRequirements, component.PublicAssignments,
            component.PublicImplementationReferences, component.Surfaces
        }, budget.Profile.MaxResultBytes));

        var intrinsicHeight = checked(68 + 30 * properties.Count);
        var proposed = layout.GetValueOrDefault(nodeKey);
        var nodeBounds = proposed is null ? new Bounds(x + (isGroup ? Padding : 0), y + (isGroup ? Padding + 24 : 0), 900, intrinsicHeight) :
            new Bounds(proposed.X, proposed.Y, proposed.Width, proposed.Height);
        if (nodeBounds.Height < intrinsicHeight)
            throw new ProjectionFailure("LAYOUT-CLIPPING", "Presentation geometry is too short for the declared public rows.");
        ValidateBounds(nodeBounds);
        var collapsed = display.GetValueOrDefault(nodeKey)?.Collapsed ?? false;
        nodes.Add(new(nodeId, nodeBounds.X, nodeBounds.Y, nodeBounds.Width, nodeBounds.Height,
            (component.IsOpaque ? "Opaque " : "") + component.Kind + " " + component.SourceDefinitionIdentity.LocalId.ToString("D"),
            groupId ?? parentGroup, Resizable: false, Rotatable: false, LabelEditable: false,
            TypeId: "ghostagram.composition." + component.Kind.ToString().ToLowerInvariant(),
            Properties: Array.AsReadOnly(properties.ToArray()),
            Presentation: new(DisplayMode: collapsed ? DiagramNodeDisplayModes.Compact : DiagramNodeDisplayModes.Expanded)));
        var bounds = nodeBounds;
        var childY = nodeBounds.Bottom + Padding;
        foreach (var child in component.Children.OrderBy(item => (item.SourceOccurrenceIdentity ?? item.SourceDefinitionIdentity).CanonicalText, StringComparer.Ordinal))
        {
            var childBounds = Visit(child, groupId ?? parentGroup, depth + 1, nodeBounds.X + Padding, childY);
            bounds = bounds.Union(childBounds); childY = Math.Max(childY, childBounds.Bottom + Padding);
        }
        if (groupId is null) return bounds;
        var groupBounds = bounds.Padded(Padding);
        if (layout.TryGetValue(groupId, out var specified))
        {
            groupBounds = new(specified.X, specified.Y, specified.Width, specified.Height);
            if (!groupBounds.Contains(bounds)) throw new ProjectionFailure("LAYOUT-CLIPPING", "An explicit Composite boundary must contain its displayed children.");
        }
        ValidateBounds(groupBounds);
        groups.Add(new(groupId, groupBounds.X, groupBounds.Y, groupBounds.Width, groupBounds.Height,
            "Composite boundary", parentGroup, display.GetValueOrDefault(groupId)?.Collapsed ?? false,
            Resizable: false, LabelEditable: false));
        return groupBounds;

        void Row(string key, string name, object value) => properties.Add(Property(
            CompositionDiagramIdentity.Encode("fact", nodeId, key), name, value, budget.Profile.MaxResultBytes));
    }

    private DiagramNodeProperty Property(string id, string name, object value, long limit)
    {
        var json = budget.Value(value, limit);
        return new(id, name, json.ValueKind switch { JsonValueKind.String => DiagramPropertyTypes.String,
            JsonValueKind.Number => DiagramPropertyTypes.Decimal, JsonValueKind.True or JsonValueKind.False => DiagramPropertyTypes.Boolean,
            _ => DiagramPropertyTypes.Json }, json, Mode: DiagramPropertyModes.Display, Connectable: false, Editor: null);
    }
    private void AddElement(string id, string sourceKey, string kind, CompositionPublicComponentView component)
    {
        if (!ids.Add(id) || !sources.Add(sourceKey)) throw new ProjectionFailure("COLLISION", "A public source occurrence identity is duplicated.");
        elements.Add(new(id, sourceKey, kind, (component.SourceOccurrenceIdentity ?? component.SourceDefinitionIdentity).CanonicalText,
            component.SourceDefinitionIdentity.CanonicalText, component.SourceDefinitionRevision,
            component.OwningBoundaryIdentity.CanonicalText, component.ParentBoundaryIdentity?.CanonicalText, component.IsOpaque));
    }
    private void Address(string value)
    {
        budget.Text(value, budget.Profile.MaxIdentifierUtf8Bytes);
        visited.Add(value); budget.Count(visited.Count, budget.Profile.MaxVisitedIdentities);
    }
    private static void ValidateBounds(Bounds bounds)
    {
        if (!double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y) || !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height) ||
            bounds.Width <= 0 || bounds.Height <= 0 || new[] { bounds.X, bounds.Y, bounds.Right, bounds.Bottom }
                .Any(value => Math.Abs(value) > CompositionPresentationProfile.MaximumCoordinateMagnitude))
            throw new ProjectionFailure("GEOMETRY", "Projection geometry exceeds its finite presentation bounds.");
    }
    private readonly record struct Bounds(double X, double Y, double Width, double Height)
    {
        internal double Right => X + Width;
        internal double Bottom => Y + Height;
        internal Bounds Union(Bounds other)
        {
            var x = Math.Min(X, other.X); var y = Math.Min(Y, other.Y);
            return new(x, y, Math.Max(Right, other.Right) - x, Math.Max(Bottom, other.Bottom) - y);
        }
        internal Bounds Padded(double padding) => new(X - padding, Y - padding, Width + 2 * padding, Height + 2 * padding);
        internal bool Contains(Bounds other) => X <= other.X && Y <= other.Y && Right >= other.Right && Bottom >= other.Bottom;
    }
}
