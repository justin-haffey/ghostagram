using System.Reflection;
using Ghostagram.Bridge.DeclarativeCompositionProjection;
using Ghostworx.System.Composition;
using Ghostworx.System.Composition.Compiler;

namespace Ghostagram.Bridge.Tests.DeclarativeComposition;

/// <summary>Actual test observations for parent conformance tooling; this is not a consumer envelope.</summary>
public sealed record ProjectionBoundaryObservation(string CaseId, string OperationIdentity, CompositionResultStatus Outcome,
    bool Passed, string Expected, string Observed,
    CompositionProjectionResult? Result = null);

public static class ProjectionBoundaryCases
{
    public static IReadOnlyList<string> CaseIds { get; } = Inventory();

    public static ProjectionBoundaryObservation ExecuteCase(string caseId, CompositionCompilationResult source, TimeProvider? timeProvider = null)
    {
        if (!CaseIds.Contains(caseId, StringComparer.Ordinal)) throw new ArgumentException("Unknown concrete boundary case.", nameof(caseId));
        return ExecuteSelected(source, timeProvider, caseId).Single();
    }

    public static IReadOnlyList<ProjectionBoundaryObservation> Execute(CompositionCompilationResult source, TimeProvider? timeProvider = null)
        => ExecuteSelected(source, timeProvider, null);

    private static IReadOnlyList<ProjectionBoundaryObservation> ExecuteSelected(CompositionCompilationResult source, TimeProvider? timeProvider, string? selectedCase)
    {
        if (!source.IsAccepted) throw new ArgumentException("Boundary execution requires actual accepted producer input.", nameof(source));
        var clock = timeProvider ?? new FixedClock(DateTimeOffset.UtcNow);
        var now = clock.GetUtcNow();
        var profiles = new CompositionProfileAdmission();
        var contexts = new CompositionOperationContextAdmission(clock);
        var profile = CompositionLimitProfile.ConformanceV1;
        var context = new CompositionOperationContext("boundary", now.AddSeconds(25), CancellationToken.None);
        var projector = new DeclarativeCompositionProjector(profiles, contexts, clock);
        var observations = new List<ProjectionBoundaryObservation>();
        var empty = Sidecar().Sidecar!;
        var maxEntries = CompositionPresentationProfile.MaximumRawSidecarEntries;
        var maxCoordinate = CompositionPresentationProfile.MaximumCoordinateMagnitude;
        var maxBytes = CompositionPresentationProfile.MaximumProjectionUtf8Bytes;
        var maxKey = checked(10 + 4 * (((profile.MaxRecursionDepth + 2) * (profile.MaxIdentifierUtf8Bytes + 4) + 2) / 3));

        Observe("GRAM-LOCAL-LAYOUT-ENTRIES-EXACT", () => Sidecar(layout: Layouts(maxEntries)), true);
        Observe("GRAM-LOCAL-LAYOUT-ENTRIES-PLUS-ONE", () => Sidecar(layout: Layouts(maxEntries + 1)), false, "BOUND");
        Observe("GRAM-LOCAL-WAYPOINT-ENTRIES-EXACT", () => Sidecar(waypoints: Points(maxEntries)), true);
        Observe("GRAM-LOCAL-WAYPOINT-ENTRIES-PLUS-ONE", () => Sidecar(waypoints: Points(maxEntries + 1)), false, "BOUND");
        Observe("GRAM-LOCAL-DISPLAY-ENTRIES-EXACT", () => Sidecar(display: Displays(maxEntries)), true);
        Observe("GRAM-LOCAL-DISPLAY-ENTRIES-PLUS-ONE", () => Sidecar(display: Displays(maxEntries + 1)), false, "BOUND");
        Observe("GRAM-LOCAL-SELECTION-ENTRIES-EXACT", () => Sidecar(selection: Keys(maxEntries)), true);
        Observe("GRAM-LOCAL-SELECTION-ENTRIES-PLUS-ONE", () => Sidecar(selection: Keys(maxEntries + 1)), false, "BOUND");
        Observe("GRAM-LOCAL-MIXED-ENTRIES-EXACT", () => Sidecar(layout: Layouts(512), waypoints: Points(512), display: Displays(512),
            selection: Keys(511), viewport: new()), true);
        Observe("GRAM-LOCAL-MIXED-ENTRIES-PLUS-ONE", () => Sidecar(layout: Layouts(512), waypoints: Points(512), display: Displays(512),
            selection: Keys(512), viewport: new()), false, "BOUND");
        Observe("GRAM-LOCAL-KEY-BYTES-EXACT", () => Sidecar(selection: [new string('x', maxKey)]), true);
        Observe("GRAM-LOCAL-KEY-BYTES-PLUS-ONE", () => Sidecar(selection: [new string('x', maxKey + 1)]), false, "BOUND");
        Observe("GRAM-LOCAL-KEY-BYTES-UTF8", () => Sidecar(selection: [new string('\u00e9', maxKey / 2 + 1)]), false, "BOUND");
        Observe("GRAM-LOCAL-KEY-INVALID", () => Sidecar(selection: [" "]), false, "IDENTITY");
        Observe("GRAM-LOCAL-AGGREGATE-BYTES-EXACT", () => Sidecar(selection: SizedKeys(maxBytes)), true);
        Observe("GRAM-LOCAL-AGGREGATE-BYTES-PLUS-ONE", () => Sidecar(selection: SizedKeys(maxBytes + 1)), false, "BOUND");
        Observe("GRAM-LOCAL-REVISION-EXACT", () => Sidecar(revision: long.MaxValue), true);
        Observe("GRAM-LOCAL-REVISION-INVALID", () => Sidecar(revision: -1), false, "PROFILE");
        Observe("GRAM-LOCAL-PROFILE-UNKNOWN", () => Sidecar(presentationProfile: "unknown/1"), false, "PROFILE");
        Observe("GRAM-LOCAL-LAYOUT-COLLISION", () => Sidecar(layout: [new("duplicate", 0, 0, 1, 1), new("duplicate", 2, 2, 1, 1)]), false, "COLLISION");
        Observe("GRAM-LOCAL-WAYPOINT-COLLISION", () => Sidecar(waypoints: [new("duplicate", 0, 0, 0), new("duplicate", 0, 1, 1)]), false, "COLLISION");
        Observe("GRAM-LOCAL-DISPLAY-COLLISION", () => Sidecar(display: [new("duplicate", true), new("duplicate", false)]), false, "COLLISION");
        Observe("GRAM-LOCAL-SELECTION-COLLISION", () => Sidecar(selection: ["duplicate", "duplicate"]), false, "COLLISION");
        Observe("GRAM-LOCAL-WAYPOINT-ORDER-EXACT", () => Sidecar(waypoints: [new("point", int.MaxValue, 0, 0)]), true);
        Observe("GRAM-LOCAL-WAYPOINT-ORDER-INVALID", () => Sidecar(waypoints: [new("point", -1, 0, 0)]), false, "GEOMETRY");
        Observe("GRAM-LOCAL-ENUMERATOR-FAILURE", () => Sidecar(layout: Broken()), false, "MALFORMED");
        Observe("GRAM-LOCAL-NULL-ENTRY", () => Sidecar(layout: [null!]), false, "MALFORMED");

        foreach (var value in new[] { maxCoordinate, -maxCoordinate })
        {
            var sign = value > 0 ? "POSITIVE" : "NEGATIVE";
            Observe("GRAM-LOCAL-COORDINATE-" + sign + "-EXACT", () => Sidecar(layout: [new("position", value, value, 1, 1)]), true);
            Observe("GRAM-LOCAL-COORDINATE-" + sign + "-PLUS-ONE", () => Sidecar(layout: [new("position", value + Math.Sign(value), value, 1, 1)]), false, "GEOMETRY");
        }
        Observe("GRAM-LOCAL-SIZE-EXACT", () => Sidecar(layout: [new("size", 0, 0, maxCoordinate, maxCoordinate)]), true);
        Observe("GRAM-LOCAL-SIZE-PLUS-ONE", () => Sidecar(layout: [new("size", 0, 0, maxCoordinate + 1, maxCoordinate)]), false, "GEOMETRY");
        Observe("GRAM-LOCAL-SIZE-ZERO", () => Sidecar(layout: [new("size", 0, 0, 0, 1)]), false, "GEOMETRY");
        Observe("GRAM-LOCAL-VIEWPORT-EXACT", () => Sidecar(viewport: new(maxCoordinate, -maxCoordinate, maxCoordinate)), true);
        Observe("GRAM-LOCAL-VIEWPORT-PLUS-ONE", () => Sidecar(viewport: new(maxCoordinate + 1, 0, 1)), false, "GEOMETRY");
        Observe("GRAM-LOCAL-ZOOM-PLUS-ONE", () => Sidecar(viewport: new(0, 0, maxCoordinate + 1)), false, "GEOMETRY");
        Observe("GRAM-LOCAL-ZOOM-ZERO", () => Sidecar(viewport: new(0, 0, 0)), false, "GEOMETRY");
        foreach (var (name, value) in new[] { ("NAN", double.NaN), ("POSITIVE-INFINITY", double.PositiveInfinity), ("NEGATIVE-INFINITY", double.NegativeInfinity) })
        {
            Observe("GRAM-LOCAL-COORDINATE-" + name, () => Sidecar(layout: [new("position", value, 0, 1, 1)]), false, "GEOMETRY");
            Observe("GRAM-LOCAL-SIZE-" + name, () => Sidecar(layout: [new("size", 0, 0, value, 1)]), false, "GEOMETRY");
            Observe("GRAM-LOCAL-ZOOM-" + name, () => Sidecar(viewport: new(0, 0, value)), false, "GEOMETRY");
        }

        // Inspect the actual declaration properties. Every observed numeric dimension is
        // materialized as its own named executed exact/+1/missing case, not a wildcard pass.
        foreach (var property in typeof(CompositionLimitProfileDeclaration).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.PropertyType == typeof(int?) || property.PropertyType == typeof(long?)).OrderBy(property => property.Name, StringComparer.Ordinal))
        {
            var exact = ProjectionFixtures.Profile() with { };
            var plus = ProjectionFixtures.Profile() with { };
            object next = property.PropertyType == typeof(int?) ? (object)checked((int)property.GetValue(exact)! + 1) : checked((long)property.GetValue(exact)! + 1);
            property.SetValue(plus, next);
            var missing = ProjectionFixtures.Profile() with { };
            property.SetValue(missing, null);
            Project("GRAM-PROFILE-" + property.Name + "-EXACT", exact, ProjectionFixtures.Context(clock), true);
            Project("GRAM-PROFILE-" + property.Name + "-PLUS-ONE", plus, ProjectionFixtures.Context(clock), false);
            Project("GRAM-PROFILE-" + property.Name + "-INVALID", missing, ProjectionFixtures.Context(clock), false);
        }
        Project("GRAM-PROFILE-IDENTITY-INVALID", ProjectionFixtures.Profile() with { Identity = null }, ProjectionFixtures.Context(clock), false);
        Project("GRAM-PROFILE-UNLIMITED-INVALID", ProjectionFixtures.Profile() with { IsUnlimited = true }, ProjectionFixtures.Context(clock), false);
        Project("GRAM-PROFILE-CANCELLATION-INVALID", ProjectionFixtures.Profile() with { CancellationPropagationRequired = false }, ProjectionFixtures.Context(clock), false);
        Project("GRAM-CONTEXT-CORRELATION-EXACT", ProjectionFixtures.Profile(), new(new string('x', 512), now.AddSeconds(25), CancellationToken.None), true);
        Project("GRAM-CONTEXT-CORRELATION-PLUS-ONE", ProjectionFixtures.Profile(), new(new string('x', 513), now.AddSeconds(25), CancellationToken.None), false);
        Project("GRAM-CONTEXT-CORRELATION-INVALID", ProjectionFixtures.Profile(), new(null, now.AddSeconds(25), CancellationToken.None), false);
        Project("GRAM-CONTEXT-HORIZON-EXACT", ProjectionFixtures.Profile(), new("horizon", now.AddMilliseconds(30000), CancellationToken.None), true);
        Project("GRAM-CONTEXT-HORIZON-PLUS-ONE", ProjectionFixtures.Profile(), new("horizon", now.AddMilliseconds(30001), CancellationToken.None), false);
        Project("GRAM-CONTEXT-DEADLINE-INVALID", ProjectionFixtures.Profile(), new("deadline", null, CancellationToken.None), false);
        Project("GRAM-CONTEXT-DEADLINE-EXPIRED", ProjectionFixtures.Profile(), new("deadline", now, CancellationToken.None), false);
        Project("GRAM-CONTEXT-DEADLINE-NONUTC", ProjectionFixtures.Profile(), new("deadline", now.AddSeconds(25).ToOffset(TimeSpan.FromHours(1)), CancellationToken.None), false);
        Project("GRAM-CONTEXT-CANCELLATION-MISSING", ProjectionFixtures.Profile(), new("cancel", now.AddSeconds(25), null), false);
        Project("GRAM-CONTEXT-CANCELLATION-REQUESTED", ProjectionFixtures.Profile(), new("cancel", now.AddSeconds(25), new CancellationToken(true)), false);
        if (selectedCase is null || selectedCase is "GRAM-DEADLINE-TRAVERSAL" or "GRAM-DEADLINE-TERMINAL" or "GRAM-CANCEL-TRAVERSAL" or "GRAM-CANCEL-TERMINAL")
        {
        var probeClock = new ProbeClock(now);
        var probe = new DeclarativeCompositionProjector(profiles, new CompositionOperationContextAdmission(probeClock), probeClock);
        var probeContext = new CompositionOperationContextDeclaration("terminal-probe", now.AddSeconds(25), CancellationToken.None);
        var probeFresh = probe.Project(source, ProjectionFixtures.Profile(), probeContext, empty);
        if (probeFresh is not CompositionProjectionResult.Fresh) throw new InvalidOperationException("A valid probe must complete before terminal-boundary cases can run.");
        var publicationRead = probeClock.Reads;
        foreach (var (phase, read) in new[] { ("TRAVERSAL", Math.Max(2, publicationRead / 2)), ("TERMINAL", publicationRead) })
        {
            if (selectedCase is null || selectedCase == "GRAM-DEADLINE-" + phase)
            {
            probeClock.Reset(read, null);
            var expired = probe.Project(source, ProjectionFixtures.Profile(), probeContext, empty);
            observations.Add(new("GRAM-DEADLINE-" + phase, "DeclarativeCompositionProjection.Project", Outcome(expired), expired is CompositionProjectionResult.Failed &&
                expired.Diagnostics.Any(item => item.Code == "GRAM-COMP-DEADLINE"), "Failed without diagram: GRAM-COMP-DEADLINE", expired.Status.ToString(), expired));
            }
            if (selectedCase is null || selectedCase == "GRAM-CANCEL-" + phase)
            {
            using var cancellation = new CancellationTokenSource();
            probeClock.Reset(int.MaxValue, () => { if (probeClock.Reads == read) cancellation.Cancel(); });
            var cancelled = probe.Project(source, ProjectionFixtures.Profile(), probeContext with { Cancellation = cancellation.Token }, empty);
            observations.Add(new("GRAM-CANCEL-" + phase, "DeclarativeCompositionProjection.Project", Outcome(cancelled), cancelled is CompositionProjectionResult.Failed &&
                cancelled.Diagnostics.Any(item => item.Code == "GRAM-COMP-CANCELLED"), "Failed without diagram: GRAM-COMP-CANCELLED", cancelled.Status.ToString(), cancelled));
            }
        }
        }
        return observations.AsReadOnly();

        CompositionSidecarAdmission Sidecar(long revision = 0, IEnumerable<CompositionLayoutEntry>? layout = null,
            IEnumerable<CompositionWaypointEntry>? waypoints = null, IEnumerable<CompositionDisplayEntry>? display = null,
            IEnumerable<string>? selection = null, CompositionViewport? viewport = null,
            string presentationProfile = CompositionPresentationProfile.Identity) =>
            CompositionPresentationSidecar.Admit(revision, layout ?? [], waypoints ?? [], display ?? [], selection ?? [], viewport,
                profile, context, clock, presentationProfile);
        void Observe(string id, Func<CompositionSidecarAdmission> execute, bool accepted, string? code = null)
        {
            if (selectedCase is not null && selectedCase != id) return;
            var result = execute();
            observations.Add(new(id, "DeclarativeCompositionProjection.Sidecar.Admit",
                result.IsAccepted ? CompositionResultStatus.Accepted : CompositionResultStatus.Rejected,
                result.IsAccepted == accepted && (accepted || result.Sidecar is null && result.Diagnostic?.Code == "GRAM-COMP-" + code),
                accepted ? "Accepted immutable sidecar" : "Rejected without sidecar: GRAM-COMP-" + code,
                result.IsAccepted ? "Accepted immutable sidecar" : result.Diagnostic?.Code ?? "No diagnostic"));
        }
        void Project(string id, CompositionLimitProfileDeclaration declaration, CompositionOperationContextDeclaration operation, bool accepted)
        {
            if (selectedCase is not null && selectedCase != id) return;
            var result = projector.Project(source, declaration, operation, empty);
            observations.Add(new(id, "DeclarativeCompositionProjection.Project", Outcome(result), accepted ? result is CompositionProjectionResult.Fresh : result is CompositionProjectionResult.Failed,
                accepted ? "Fresh" : "Failed without diagram", result.Status.ToString(), result));
        }
        static IEnumerable<string> Keys(int count) => Enumerable.Range(0, count).Select(index => "orphan-" + index);
        static IEnumerable<CompositionLayoutEntry> Layouts(int count) => Keys(count).Select(key => new CompositionLayoutEntry(key, 0, 0, 1, 1));
        static IEnumerable<CompositionWaypointEntry> Points(int count) => Keys(count).Select(key => new CompositionWaypointEntry(key, 0, 0, 0));
        static IEnumerable<CompositionDisplayEntry> Displays(int count) => Keys(count).Select(key => new CompositionDisplayEntry(key, false));
        IEnumerable<string> SizedKeys(int bytes)
        {
            var index = 0;
            while (bytes > 0)
            {
                var size = Math.Min(bytes, maxKey);
                yield return (index++).ToString("D6") + new string('x', size - 6);
                bytes -= size;
            }
        }
        static IEnumerable<CompositionLayoutEntry> Broken()
        {
            yield return new("first", 0, 0, 1, 1);
            throw new InvalidOperationException("PROTECTED-ENUMERATOR-DETAIL");
        }
    }

    public static void Run()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero));
        var results = Execute(ProjectionFixtures.CompileNested(timeProvider: clock), clock);
        if (!results.Select(result => result.CaseId).Order(StringComparer.Ordinal).SequenceEqual(CaseIds.Order(StringComparer.Ordinal)))
            throw new InvalidOperationException("Executed concrete boundary cases differ from the pre-execution inventory.");
        var failed = results.Where(result => !result.Passed).ToArray();
        if (failed.Length > 0) throw new InvalidOperationException(string.Join(Environment.NewLine,
            failed.Select(result => result.CaseId + ": expected " + result.Expected + "; observed " + result.Observed)));
        Console.WriteLine("PASS composition boundaries: " + results.Count + " actual admission and no-partial-output cases");
    }
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    { public override DateTimeOffset GetUtcNow() => now; }

    private static CompositionResultStatus Outcome(CompositionProjectionResult result)
    {
        if (result.Status == CompositionProjectionStatus.Fresh) return CompositionResultStatus.Accepted;
        if (result.Status == CompositionProjectionStatus.Unsupported) return CompositionResultStatus.Unsupported;
        var codes = result.Diagnostics.Select(item => item.Code).Concat(result.SourceDiagnostics.Select(item => item.Code));
        if (codes.Any(code => code is "GRAM-COMP-CANCELLED" or "CMP-CANCELLED")) return CompositionResultStatus.Cancelled;
        if (codes.Any(code => code is "GRAM-COMP-DEADLINE" or "CMP-DEADLINE-EXPIRED")) return CompositionResultStatus.DeadlineExpired;
        return CompositionResultStatus.Rejected;
    }

    private static IReadOnlyList<string> Inventory()
    {
        var ids = new List<string>
        {
            "GRAM-CONTEXT-CANCELLATION-MISSING", "GRAM-CONTEXT-CANCELLATION-REQUESTED",
            "GRAM-CONTEXT-CORRELATION-EXACT", "GRAM-CONTEXT-CORRELATION-INVALID", "GRAM-CONTEXT-CORRELATION-PLUS-ONE",
            "GRAM-CONTEXT-DEADLINE-EXPIRED", "GRAM-CONTEXT-DEADLINE-INVALID", "GRAM-CONTEXT-DEADLINE-NONUTC",
            "GRAM-CONTEXT-HORIZON-EXACT", "GRAM-CONTEXT-HORIZON-PLUS-ONE",
            "GRAM-LOCAL-AGGREGATE-BYTES-EXACT", "GRAM-LOCAL-AGGREGATE-BYTES-PLUS-ONE",
            "GRAM-LOCAL-ENUMERATOR-FAILURE", "GRAM-LOCAL-KEY-BYTES-EXACT", "GRAM-LOCAL-KEY-BYTES-PLUS-ONE",
            "GRAM-LOCAL-KEY-BYTES-UTF8", "GRAM-LOCAL-KEY-INVALID", "GRAM-LOCAL-MIXED-ENTRIES-EXACT",
            "GRAM-LOCAL-MIXED-ENTRIES-PLUS-ONE", "GRAM-LOCAL-NULL-ENTRY", "GRAM-LOCAL-PROFILE-UNKNOWN",
            "GRAM-LOCAL-REVISION-EXACT", "GRAM-LOCAL-REVISION-INVALID", "GRAM-LOCAL-SIZE-EXACT",
            "GRAM-LOCAL-SIZE-PLUS-ONE", "GRAM-LOCAL-SIZE-ZERO", "GRAM-LOCAL-VIEWPORT-EXACT",
            "GRAM-LOCAL-VIEWPORT-PLUS-ONE", "GRAM-LOCAL-WAYPOINT-ORDER-EXACT", "GRAM-LOCAL-WAYPOINT-ORDER-INVALID",
            "GRAM-LOCAL-ZOOM-PLUS-ONE", "GRAM-LOCAL-ZOOM-ZERO", "GRAM-PROFILE-CANCELLATION-INVALID",
            "GRAM-PROFILE-IDENTITY-INVALID", "GRAM-PROFILE-UNLIMITED-INVALID"
        };
        foreach (var dimension in new[] { "LAYOUT", "WAYPOINT", "DISPLAY", "SELECTION" })
        { ids.Add("GRAM-LOCAL-" + dimension + "-ENTRIES-EXACT"); ids.Add("GRAM-LOCAL-" + dimension + "-ENTRIES-PLUS-ONE"); ids.Add("GRAM-LOCAL-" + dimension + "-COLLISION"); }
        foreach (var sign in new[] { "POSITIVE", "NEGATIVE" })
        { ids.Add("GRAM-LOCAL-COORDINATE-" + sign + "-EXACT"); ids.Add("GRAM-LOCAL-COORDINATE-" + sign + "-PLUS-ONE"); }
        foreach (var dimension in new[] { "COORDINATE", "SIZE", "ZOOM" })
        foreach (var invalid in new[] { "NAN", "POSITIVE-INFINITY", "NEGATIVE-INFINITY" }) ids.Add("GRAM-LOCAL-" + dimension + "-" + invalid);
        foreach (var property in typeof(CompositionLimitProfileDeclaration).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.PropertyType == typeof(int?) || property.PropertyType == typeof(long?)))
        foreach (var variant in new[] { "EXACT", "PLUS-ONE", "INVALID" }) ids.Add("GRAM-PROFILE-" + property.Name + "-" + variant);
        foreach (var phase in new[] { "TRAVERSAL", "TERMINAL" })
        { ids.Add("GRAM-CANCEL-" + phase); ids.Add("GRAM-DEADLINE-" + phase); }
        return Array.AsReadOnly(ids.Order(StringComparer.Ordinal).ToArray());
    }
    private sealed class ProbeClock(DateTimeOffset now) : TimeProvider
    {
        private int deadlineRead = int.MaxValue;
        private Action? onRead;
        public int Reads { get; private set; }
        public void Reset(int expiresAt, Action? action) { Reads = 0; deadlineRead = expiresAt; onRead = action; }
        public override DateTimeOffset GetUtcNow()
        {
            Reads++; onRead?.Invoke();
            return Reads >= deadlineRead ? now.AddMinutes(1) : now;
        }
    }
}
