using System.Globalization;
using System.Text.Json;
using Ghostagram.Bridge.DeclarativeCompositionProjection;
using Ghostworx.System.Composition;
using Ghostworx.System.Composition.Compiler;
using Ghostworx.System.Composition.Definitions;
using Ghostworx.System.Composition.Surfaces;
using Ghostworx.System.Graph;

namespace Ghostagram.Bridge.Tests.DeclarativeComposition;

public sealed record ProjectionOutputSizeSearch(CompositionCompilationResult Producer,
    CompositionProjectionResult.Fresh Exact, CompositionProjectionResult Overflow,
    CompositionPresentationSidecar ExactSidecar, CompositionPresentationSidecar PlusOneSidecar,
    int PaddingBytes, int MeasuredExactUtf8Bytes, int CalculatedPlusOneCandidateUtf8Bytes, int CompilerInvocations);

/// <summary>Searches real compiler/projector outputs; never constructs an IR, public view or Fresh result.</summary>
public static class ProjectionOutputSizeCases
{
    public static IReadOnlyList<string> CaseIds { get; } = Array.AsReadOnly(new[]
    { "GRAM-LOCAL-RESULT-BYTES-EXACT", "GRAM-LOCAL-RESULT-BYTES-PLUS-ONE" });

    public static CompositionCompilationResult CompileLarge(int paddingBytes, TimeProvider? timeProvider = null)
    {
        if (paddingBytes < 0 || paddingBytes > 24000) throw new ArgumentOutOfRangeException(nameof(paddingBytes));
        var root = ProjectionFixtures.Address(800);
        // Fixed entry count makes every added ASCII byte linear. Each semantic value stays
        // below 4096 bytes; fixed public schemas/descriptions remain below their owner limits.
        var metadata = Enumerable.Range(0, 8).Select(index => new KeyValuePair<SemanticAddress, GraphSemanticValue>(
            ProjectionFixtures.Address(810 + index), GraphSemanticValue.String(new string('x', paddingBytes / 8 + (index < paddingBytes % 8 ? 1 : 0)))));
        var provenance = new DefinitionProvenance("ghostagram:large-public-schema-fixture", "1", metadata: metadata);
        var schema = new SemanticShape(ProjectionFixtures.Address(880), "{\"description\":\"" + new string('s', 1000) + "\",\"type\":\"string\"}");
        var surfaces = Enumerable.Range(0, 24).Select(index => new DeclaredSurface(ProjectionFixtures.Address(850 + index), root,
            DeclaredSurfaceKind.Operation, SurfaceVisibility.Public, "Public operation description " + new string('d', 128),
            input: schema, result: schema));
        var definition = new ComponentDefinition(root, 1, ComponentDefinitionKind.Component, [],
            new("large-fixture-scope", "Large public schema and description projection", SurfaceVisibility.Public),
            [], [], [], [], [], [], [], surfaces, [], provenance);
        return new CompositionCompiler(timeProvider: timeProvider).Compile(new CompositionCompilationRequest(new(root, [definition])),
            null, ProjectionFixtures.Profile(), ProjectionFixtures.Context(timeProvider));
    }

    public static ProjectionOutputSizeSearch Find(TimeProvider? timeProvider = null)
    {
        var clock = timeProvider ?? new FixedClock(DateTimeOffset.UtcNow);
        var profiles = new CompositionProfileAdmission();
        var contexts = new CompositionOperationContextAdmission(clock);
        var projector = new DeclarativeCompositionProjector(profiles, contexts, clock);
        var candidates = new Dictionary<int, Candidate>();
        var limit = CompositionPresentationProfile.MaximumProjectionUtf8Bytes;
        var baseSidecar = Sidecar(new());
        var lower = 0; var upper = 24000;
        if (Evaluate(lower).Result is not CompositionProjectionResult.Fresh)
            throw Gap("The smallest real producer fixture does not project completely.", Evaluate(lower));
        if (Evaluate(upper).Result is CompositionProjectionResult.Fresh)
            throw Gap("The largest admitted payload remains below the projection ceiling; the source fixture needs review.", Evaluate(upper));
        // At most fifteen bisection steps narrow this finite 24,001-value input space.
        while (upper - lower > 1)
        {
            var middle = lower + (upper - lower) / 2;
            if (Evaluate(middle).Result is CompositionProjectionResult.Fresh) lower = middle;
            else upper = middle;
        }
        var below = Evaluate(lower);
        var above = Evaluate(upper);
        if (!above.Producer.IsAccepted || above.Result is not CompositionProjectionResult.Failed ||
            !above.Result.Diagnostics.Any(item => item.Code == "GRAM-COMP-BOUND"))
            throw Gap("The owner or another projector guard stopped growth before the local serialized-result boundary.", above);
        var gap = limit - below.Bytes;
        // Viewport values occur once in a full Project result. Change only finite numeric
        // spelling lengths to bridge the semantic payload's repetition stride without changing source authority.
        var exactView = ViewportAdding(gap);
        var plusOneView = ViewportAdding(gap + 1);
        if (exactView is null || plusOneView is null)
        {
            var stride = lower > 0 ? below.Bytes - Evaluate(lower - 1).Bytes : 0;
            throw Gap($"Exact bytes are unreachable with this fixture: measured {below.Bytes}, gap {gap}, observed source stride {stride}; finite viewport spellings add at most 53 bytes.", below);
        }
        var exactSidecar = Sidecar(exactView);
        var plusOneSidecar = Sidecar(plusOneView);
        var sourceBytes = below.Producer.Ir!.CanonicalBytes.ToArray();
        var exact = Project(below.Producer, exactSidecar);
        if (exact is not CompositionProjectionResult.Fresh fresh)
            throw Gap("The measured exact-byte candidate was not published Fresh.", new(below.Producer, exact, 0));
        var actualBytes = CompositionProjectionJson.SerializeToUtf8Bytes(fresh).Length;
        if (actualBytes != limit) throw Gap($"Exact-byte calibration differed from the actual serialized result: {actualBytes}.", new(below.Producer, fresh, actualBytes));
        var viewportDifference = ViewportBytes(plusOneView) - ViewportBytes(exactView);
        if (viewportDifference != 1) throw new InvalidOperationException("The plus-one case must change serialized viewport bytes by exactly one.");
        var overflow = Project(below.Producer, plusOneSidecar);
        if (overflow is not CompositionProjectionResult.Failed || !overflow.Diagnostics.Any(item => item.Code == "GRAM-COMP-BOUND"))
            throw Gap("The exact-plus-one candidate did not fail without a Fresh diagram.", new(below.Producer, overflow, 0));
        if (!sourceBytes.AsSpan().SequenceEqual(below.Producer.Ir.CanonicalBytes.Span))
            throw new InvalidOperationException("Output-size probing mutated the admitted producer.");
        return new(below.Producer, fresh, overflow, exactSidecar, plusOneSidecar, lower, actualBytes,
            checked(actualBytes + viewportDifference), candidates.Count);

        Candidate Evaluate(int padding)
        {
            if (candidates.TryGetValue(padding, out var cached)) return cached;
            var producer = CompileLarge(padding, clock);
            var result = producer.IsAccepted ? Project(producer, baseSidecar) : null;
            var candidate = new Candidate(producer, result, result is CompositionProjectionResult.Fresh accepted
                ? CompositionProjectionJson.SerializeToUtf8Bytes(accepted).Length : 0);
            candidates.Add(padding, candidate);
            return candidate;
        }
        CompositionProjectionResult Project(CompositionCompilationResult producer, CompositionPresentationSidecar sidecar) =>
            projector.Project(producer, ProjectionFixtures.Profile(), ProjectionFixtures.Context(clock), sidecar);
        CompositionPresentationSidecar Sidecar(CompositionViewport viewport)
        {
            var context = contexts.Admit(ProjectionFixtures.Context(clock), CompositionLimitProfile.ConformanceV1);
            var sidecar = CompositionPresentationSidecar.Admit(0, [], [], [], [], viewport, CompositionLimitProfile.ConformanceV1, context.Context!, clock);
            return sidecar.Sidecar ?? throw new InvalidOperationException("Finite output-size fixture viewport failed admission.");
        }
    }

    public static IReadOnlyList<ProjectionBoundaryObservation> Observe(ProjectionOutputSizeSearch search) => Array.AsReadOnly(new[]
    {
        new ProjectionBoundaryObservation(CaseIds[0], "DeclarativeCompositionProjection.Project", CompositionResultStatus.Accepted,
            search.MeasuredExactUtf8Bytes == CompositionPresentationProfile.MaximumProjectionUtf8Bytes,
            "Fresh at exactly 1048576 UTF-8 bytes", "Fresh measured at " + search.MeasuredExactUtf8Bytes + " UTF-8 bytes", search.Exact),
        new ProjectionBoundaryObservation(CaseIds[1], "DeclarativeCompositionProjection.Project", CompositionResultStatus.Rejected,
            search.CalculatedPlusOneCandidateUtf8Bytes == CompositionPresentationProfile.MaximumProjectionUtf8Bytes + 1 &&
                search.Overflow is CompositionProjectionResult.Failed && search.Overflow.Diagnostics.Any(item => item.Code == "GRAM-COMP-BOUND"),
            "Failed without diagram for the one-byte-larger candidate",
            "Actual " + search.Overflow.Status + "; unchanged source and exactly +1 serialized viewport byte from measured exact result", search.Overflow)
    });

    public static void Run()
    {
        var found = Find(new FixedClock(new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero)));
        if (Observe(found).Any(item => !item.Passed)) throw new InvalidOperationException("Actual output-size boundary observations failed.");
        Console.WriteLine($"PASS projection serialized bytes: exact {found.MeasuredExactUtf8Bytes}; +1 rejected; {found.CompilerInvocations} real compiler invocations");
    }

    private static CompositionViewport? ViewportAdding(int bytes)
    {
        if (bytes < 0 || bytes > 53) return null;
        var signed = Numbers(allowNegative: true);
        var positive = Numbers(allowNegative: false).Where(item => item.Value > 0).ToArray();
        foreach (var x in signed)
        foreach (var y in signed)
        foreach (var zoom in positive)
        {
            var candidate = new CompositionViewport(x.Value, y.Value, zoom.Value);
            if (x.Bytes + y.Bytes + zoom.Bytes - 3 == bytes && ViewportBytes(candidate) - ViewportBytes(new()) == bytes) return candidate;
        }
        return null;
    }
    private static (double Value, int Bytes)[] Numbers(bool allowNegative)
    {
        var values = new List<double> { 0, 1 };
        for (var digits = 1; digits <= 16; digits++)
            values.Add(double.Parse("0." + new string('1', digits), CultureInfo.InvariantCulture));
        if (allowNegative) values.AddRange(values.ToArray().Where(value => value > 0).Select(value => -value));
        return values.Where(value => allowNegative || value > 0)
            .Select(value => (Value: value, Bytes: JsonSerializer.SerializeToUtf8Bytes(value).Length))
            .GroupBy(item => item.Bytes).Select(group => group.First()).OrderBy(item => item.Bytes).ToArray();
    }
    private static int ViewportBytes(CompositionViewport viewport) => JsonSerializer.SerializeToUtf8Bytes(viewport,
        new JsonSerializerOptions(JsonSerializerDefaults.Web)).Length;
    private static InvalidOperationException Gap(string reason, Candidate candidate) => new("Output byte boundary remains unverified. " + reason +
        " Producer=" + candidate.Producer.Status + "; projection=" + candidate.Result?.Status + "; codes=" +
        string.Join(",", candidate.Producer.Diagnostics.Diagnostics.Select(item => item.Code).Concat(candidate.Result?.Diagnostics.Select(item => item.Code) ?? [])));
    private sealed record Candidate(CompositionCompilationResult Producer, CompositionProjectionResult? Result, int Bytes);
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    { public override DateTimeOffset GetUtcNow() => now; }
}
