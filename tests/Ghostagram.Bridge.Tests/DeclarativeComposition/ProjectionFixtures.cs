using Ghostworx.System.Composition;
using Ghostworx.System.Composition.Compiler;
using Ghostworx.System.Composition.Definitions;
using Ghostworx.System.Composition.Requirements;
using Ghostworx.System.Composition.Surfaces;
using Ghostworx.System.Graph;
using Ghostworx.System.Primitives;
using Ghostworx.System.Variable;

namespace Ghostagram.Bridge.Tests.DeclarativeComposition;

/// <summary>Reusable inputs compiled by the actual System owner; no consumer result or envelope is constructed here.</summary>
public static class ProjectionFixtures
{
    public static CompositionLimitProfileDeclaration Profile() => CompositionLimitProfileDeclaration.ConformanceV1;
    public static CompositionOperationContextDeclaration Context(TimeProvider? timeProvider = null,
        CancellationToken cancellation = default, string correlationId = "ghostagram-projection-fixture") =>
        new(correlationId, (timeProvider ?? TimeProvider.System).GetUtcNow().AddSeconds(25), cancellation);

    public static CompositionCompilationResult CompileNested(long revision = 1, TimeProvider? timeProvider = null) =>
        Compile(new(NestedSource(revision)), timeProvider);

    public static CompositionCompilationResult CompileUnsupported(TimeProvider? timeProvider = null) =>
        Compile(new(NestedSource(), contractVersion: new("ghostagram.test.unsupported/1")), timeProvider);

    public static CompositionCompilationResult CompileSkewed(TimeProvider? timeProvider = null)
    {
        var source = NestedSource();
        return Compile(new(new DefinitionSourceSet(source.RootIdentity, source.Definitions,
            profile: new("ghostagram.test.source-skew/1"))), timeProvider);
    }

    public static CompositionCompilationResult CompileFailed(TimeProvider? timeProvider = null)
    {
        var source = NestedSource();
        // Keep the required root aggregate, but withhold its referenced child. Structural
        // construction succeeds; the real compiler owns the unresolved containment failure.
        var root = source.Definitions.Single(definition => definition.Identity == source.RootIdentity);
        return Compile(new(new DefinitionSourceSet(source.RootIdentity, [root])), timeProvider);
    }

    public static CompositionCompilationResult CompileCollision(TimeProvider? timeProvider = null) =>
        Compile(new(BuildSource(1, ambiguousExport: true)), timeProvider);

    public static CompositionCompilationResult CompileCancelled(TimeProvider? timeProvider = null) =>
        new CompositionCompiler(timeProvider: timeProvider).Compile(new CompositionCompilationRequest(NestedSource()),
            null, Profile(), Context(timeProvider, new CancellationToken(true)));

    public static DefinitionSourceSet NestedSource(long revision = 1) => BuildSource(revision, false);

    private static DefinitionSourceSet BuildSource(long revision, bool ambiguousExport)
    {
        // Definition identities are exact revision-pinned source identities. Stable
        // declaration and presentation identities below retain their own canonical revision.
        var rootId = Address(1, revision); var nestedId = Address(2, revision); var opaqueId = Address(3, revision);
        var nestedOccurrence = Address(4); var opaqueOccurrence = Address(5);
        var rootExports = new List<BoundaryExport> { new(Address(101), rootId, nestedOccurrence, Address(201), "nested") };
        if (ambiguousExport) rootExports.Add(new(Address(299), rootId, nestedOccurrence, Address(298), "ambiguous-nested"));
        var nestedExports = new List<BoundaryExport> { new(Address(102), nestedId, opaqueOccurrence, Address(202), "opaque") };
        rootExports.Add(new(Address(103), rootId, Address(202), Address(203), "opaque-through-root"));
        var shape = new SemanticShape(Address(6), "{\"type\":\"string\"}");
        DeclaredSurface[] surfaces =
        [
            new(Address(10), nestedId, DeclaredSurfaceKind.Capability, SurfaceVisibility.Exported, "Observe the declared capability"),
            new(Address(11), nestedId, DeclaredSurfaceKind.Operation, SurfaceVisibility.Exported, "Describe an operation", input: shape, result: shape),
            new(Address(12), nestedId, DeclaredSurfaceKind.IPort, SurfaceVisibility.Exported, "Describe an input port", input: shape, direction: PortDirection.Input),
            new(Address(13), nestedId, DeclaredSurfaceKind.IControl, SurfaceVisibility.Exported, "Describe a control", input: shape, result: shape,
                preconditions: [new("definition-ready", "The declaration is available")],
                effects: [new("definition-only", "Describes an effect without execution")],
                stateTransition: new("declared", "described", "Inert state transition description"),
                control: new("moderate", true, "Read-only control declaration"))
        ];
        for (var i = 0; i < surfaces.Length; i++)
        {
            nestedExports.Add(new(Address(110 + i), nestedId, surfaces[i].Identity, Address(210 + i), "surface" + i));
            rootExports.Add(new(Address(120 + i), rootId, Address(210 + i), Address(220 + i), "surface-through-root" + i));
        }

        var provenance = new DefinitionProvenance("ghostagram:projection-fixture", revision.ToString(global::System.Globalization.CultureInfo.InvariantCulture),
            metadata: [new(Address(300), GraphSemanticValue.Object([new("revision", GraphSemanticValue.Int64(revision)),
                new("bytes", GraphSemanticValue.Bytes([1, 2, 3]))]))]);
        var requirement = new VariableRequirement(Address(20), rootId,
            new VariableType(Address(21, revision: null), new SemanticVersion(1, 0)), [new VariableScope(Address(22, revision: null))],
            new VariableCardinality(1, 1), RequirementPresence.Required, SurfaceVisibility.Public, CompositionCompatibilitySet.Current);
        var assignment = new VariableAssignment(Address(23), requirement.Identity,
            new VariableReference(Address(24, revision: null), VariableReferenceMode.Live, requirement.ExpectedType, requirement.ScopeConstraints),
            CompositionCompatibilitySet.Current, owner: rootId, visibility: SurfaceVisibility.Public, provenance: provenance);
        var implementation = new ImplementationReference(Address(25), "artifact:projection-fixture", CompositionCompatibilitySet.Current,
            provenance, metadata: [new(Address(301), GraphSemanticValue.String("display-only"))], owner: rootId, visibility: SurfaceVisibility.Public);

        var root = Definition(rootId, ComponentDefinitionKind.Composite,
            [new(nestedOccurrence, rootId, new(nestedId, CompositionCompatibilitySet.Current), "nested", SurfaceVisibility.Public)],
            rootExports, [], SurfaceVisibility.Public, [requirement], [assignment], [implementation]);
        var nested = Definition(nestedId, ComponentDefinitionKind.Composite,
            [new(opaqueOccurrence, nestedId, new(opaqueId, CompositionCompatibilitySet.Current), "opaque", SurfaceVisibility.Public)],
            nestedExports, surfaces, SurfaceVisibility.Public);
        var opaque = Definition(opaqueId, ComponentDefinitionKind.Component, [], [],
            [new(Address(30), opaqueId, DeclaredSurfaceKind.Capability, SurfaceVisibility.Private, "PRIVATE-DO-NOT-DISPLAY")], SurfaceVisibility.Private);
        return new(rootId, [root, nested, opaque]);

        ComponentDefinition Definition(SemanticAddress id, ComponentDefinitionKind kind,
            IEnumerable<ConstituentOccurrence> constituents, IEnumerable<BoundaryExport> exports,
            IEnumerable<DeclaredSurface> declaredSurfaces, SurfaceVisibility visibility,
            IEnumerable<VariableRequirement>? requirements = null, IEnumerable<VariableAssignment>? assignments = null,
            IEnumerable<ImplementationReference>? implementations = null) =>
            new(id, revision, kind, [], new("fixture-scope", "Definition-only projection fixture", SurfaceVisibility.Component),
                [], [], constituents, [], exports, requirements ?? [], assignments ?? [], declaredSurfaces,
                implementations ?? [], new DefinitionProvenance("ghostagram:projection-fixture:" + id.LocalId.ToString("D"),
                    provenance.SourceRevision, metadata: provenance.Metadata), visibility: visibility);
    }

    public static SemanticAddress Address(int suffix, long? revision = 1) => SemanticAddress.ForNode(new SemanticAuthority("ghostagram.projection.tests"),
        new NodeId(Guid.Parse("31000000-0000-0000-0000-000000000001")),
        new NodeId(Guid.Parse($"32000000-0000-0000-0000-{suffix:000000000000}")), revision);

    private static CompositionCompilationResult Compile(CompositionCompilationRequest request, TimeProvider? timeProvider) =>
        new CompositionCompiler(timeProvider: timeProvider).Compile(request, null, Profile(), Context(timeProvider));
}
