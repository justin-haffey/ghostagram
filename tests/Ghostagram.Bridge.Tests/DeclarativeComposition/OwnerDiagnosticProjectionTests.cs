using Ghostagram.Bridge.DeclarativeCompositionProjection;
using Ghostworx.System.Composition;
using Ghostworx.System.Composition.Compiler;
using Ghostworx.System.Composition.Definitions;
using Ghostworx.System.Composition.Exchange;

namespace Ghostagram.Bridge.Tests.DeclarativeComposition;

public static class OwnerDiagnosticProjectionTests
{
    public static void Run()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero));
        var profiles = new CompositionProfileAdmission();
        var contexts = new CompositionOperationContextAdmission(clock);
        var exchange = new CompositionExchange(profiles, contexts, clock);
        var migration = new CompositionMigration(profiles, contexts, exchange);
        var projector = new DeclarativeCompositionProjector(profiles, contexts, clock);
        var profile = ProjectionFixtures.Profile();
        var context = ProjectionFixtures.Context(clock);
        var sidecar = CompositionPresentationSidecar.Admit(0, [], [], [], [], null, CompositionLimitProfile.ConformanceV1,
            contexts.Admit(context, CompositionLimitProfile.ConformanceV1).Context!, clock).Sidecar!;
        var compiled = ProjectionFixtures.CompileNested(timeProvider: clock);
        Require(compiled.IsAccepted, "Actual diagnostic test source must compile.");

        var failedEncode = exchange.Encode(compiled.Ir!, profile with { MaxRoots = 0 }, context);
        Require(!failedEncode.IsAccepted, "Actual Encode must reject the invalid profile.");
        Rejected(projector.Project(failedEncode, profile, context, sidecar), failedEncode.Diagnostics);
        var failedDecode = exchange.Decode(ReadOnlyMemory<byte>.Empty, profile, context);
        Require(!failedDecode.IsAccepted, "Actual Decode must reject an empty document.");
        Rejected(projector.Project(failedDecode, profile, context, sidecar), failedDecode.Diagnostics);
        var failedMigration = migration.Migrate(new(ReadOnlyMemory<byte>.Empty, CompatibilityTuple.Current(CompositionExchangeRecordKind.Ir)),
            CompositionMigrationCatalog.SupportedV1, profile, context);
        Require(!failedMigration.IsAccepted, "Actual Migrate must reject the empty predecessor.");
        Rejected(projector.Project(failedMigration, profile, context, sidecar), failedMigration.Diagnostics);
        var failedInspection = migration.Inspect(null!, CompositionMigrationCatalog.SupportedV1, profile, context);
        Require(!failedInspection.IsAccepted, "Actual Inspect must reject a missing request.");
        Rejected(projector.Project(failedInspection, profile, context, sidecar), failedInspection.Diagnostics);
        var tuple = CompatibilityTuple.Current(CompositionExchangeRecordKind.Ir);
        var failedSelection = migration.Negotiate(new(tuple, [tuple with { SchemaVersion = "unknown-schema" }]),
            CompositionMigrationCatalog.SupportedV1, profile, context);
        Require(!failedSelection.IsAccepted, "Actual Negotiate must reject an unavailable target.");
        Rejected(projector.Project(failedSelection, profile, context, sidecar), failedSelection.Diagnostics);

        var encoded = exchange.Encode(compiled.Ir!, profile, context);
        Require(encoded.IsAccepted, "Actual Encode of accepted IR must succeed.");
        NoView(projector.Project(encoded, profile, context, sidecar), encoded.Diagnostics);
        var verified = exchange.Verify(encoded.Document!.CanonicalBytes, profile, context);
        Require(verified.IsAccepted && projector.Project(verified, profile, context, sidecar) is CompositionProjectionResult.Fresh,
            "Only an explicit actual Verify supplies the encoded document's projectable public view.");
        var inspected = migration.Inspect(new(tuple, [tuple]), CompositionMigrationCatalog.SupportedV1, profile, context);
        Require(inspected.IsAccepted, "Actual inspection of an exact target must succeed.");
        NoView(projector.Project(inspected, profile, context, sidecar), inspected.Diagnostics);
        var selected = migration.Negotiate(new(tuple, [tuple]), CompositionMigrationCatalog.SupportedV1, profile, context);
        Require(selected.IsAccepted, "Actual negotiation of an exact target must succeed.");
        NoView(projector.Project(selected, profile, context, sidecar), selected.Diagnostics);
        Console.WriteLine("PASS actual Encode/Migrate/Inspect/Negotiate diagnostic projection and explicit verified Encode path");
    }

    /// <summary>For an actual GetExact outcome supplied by the owner replay/provider; this helper never constructs one.</summary>
    public static CompositionProjectionResult CheckReference(DefinitionReferenceResult actualResult,
        DeclarativeCompositionProjector projector, CompositionLimitProfileDeclaration profile,
        CompositionOperationContextDeclaration context, CompositionPresentationSidecar sidecar)
    {
        var result = projector.Project(actualResult, profile, context, sidecar);
        if (actualResult.IsAccepted) NoView(result, actualResult.Diagnostics);
        else Rejected(result, actualResult.Diagnostics);
        return result;
    }

    private static void Rejected(CompositionProjectionResult result, CompositionDiagnosticSet diagnostics) =>
        Require(result is CompositionProjectionResult.Failed or CompositionProjectionResult.Skewed or CompositionProjectionResult.Unsupported &&
            result.SourceAnchor is null && result.SourceDiagnostics.SequenceEqual(diagnostics.Diagnostics),
            "Rejected owner output must preserve all source diagnostics without a fabricated diagram or IR anchor.");
    private static void NoView(CompositionProjectionResult result, CompositionDiagnosticSet diagnostics) =>
        Require(result is CompositionProjectionResult.Unsupported && result.SourceAnchor is null &&
            result.Diagnostics.Any(item => item.Code == "GRAM-COMP-SOURCE-KIND") && result.SourceDiagnostics.SequenceEqual(diagnostics.Diagnostics),
            "Accepted semantic outcomes without a verified public view must remain non-projectable without invented source failure.");
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    { public override DateTimeOffset GetUtcNow() => now; }
}
