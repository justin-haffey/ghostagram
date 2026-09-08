using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ghostworx.System.Composition;
using Ghostworx.System.Composition.Compiler;
using Ghostworx.System.Composition.Conformance;
using Ghostworx.System.Composition.Conformance.Runner;
using Ghostworx.System.Composition.Conformance.Support;
using Ghostworx.System.Composition.Exchange;

namespace Ghostagram.Graph.Conformance.Composition;

public sealed record CompositionReplayInvocationReceipt(CompositionLimitProfileDeclaration? Profile, bool HasContext,
    string? CorrelationId, DateTimeOffset? AbsoluteDeadlineUtc, CompositionFixtureCancellation? Cancellation,
    DateTimeOffset? ClockStartUtc, IReadOnlyList<CompositionFixtureCheckpoint> ScheduledCheckpoints);

public sealed record CompositionReplayOperationReceipt(int Ordinal, CompositionFixtureAnchor Anchor, string Purpose,
    string OperationIdentity, CompositionResultStatus Status, IReadOnlyList<CompositionDiagnostic> Diagnostics,
    CompositionReplayInvocationReceipt Invocation, IReadOnlyList<string> ObservedCheckpoints, bool AllScheduledCheckpointsObserved,
    CompositionReplayObservedResult Observation);

public sealed record CompositionReplayObservedResult(string OperationIdentity, string ResultSchemaIdentity,
    CompositionResultStatus Status, IReadOnlyList<CompositionDiagnostic> Diagnostics, JsonElement ObservedFields,
    IReadOnlyList<string> RequiredFieldNames);

public sealed record CompositionReplayPrerequisiteReceipt(CompositionFixtureAnchor Consumer, CompositionFixtureAnchor Source,
    string Purpose, string DeclaredOutputKind, string DeclaredOutputDigest, string ActualOutputKind, string ActualOutputDigest,
    CompositionResultStatus ActualStatus, bool ExactMatch, bool RequiredOutcomeChecksMatch, bool OwnerObservedFieldsPending,
    CompositionReplayObservedResult Observation);

public sealed record CompositionReplayProducerReceipt(string CaseIdentity, CompositionResultStatus Status, string StableCode,
    string ResultBytesSha256, string BeforeStateDigest, string AfterStateDigest);

public sealed record CompositionOwnerReplayReceipt(CompositionFixtureAnchor Anchor, CompositionResultStatus OwnerStatus,
    IReadOnlyList<CompositionDiagnostic> OwnerDiagnostics, string ExpectedStatus, IReadOnlyList<string> ExpectedDiagnosticCodes,
    IReadOnlyDictionary<string, JsonElement> ExpectedObservedFields, CompositionReplayProducerReceipt OriginalProducerObservation,
    bool ExpectedStatusAndDiagnosticsMatch, bool OriginalStatusAndStableCodeMatch, bool AllScheduledCheckpointsObserved,
    bool PrerequisiteOutcomeChecksMatch, bool AdapterVerificationSucceeded, bool OwnerObservedFieldsPending,
    string ObservedFieldsStatus, IReadOnlyList<CompositionReplayOperationReceipt> Operations,
    IReadOnlyList<CompositionReplayPrerequisiteReceipt> Prerequisites, CompositionReplayObservedResult Observation,
    bool OwnerObservationChecksMatch, IReadOnlyDictionary<string, JsonElement> DeclaredExpectedObservedFields);

public sealed class CompositionOwnerReplayResult
{
    internal CompositionOwnerReplayResult(CompositionFixtureRequest originalRequest, CompositionFixtureRequest executedRequest,
        object ownerResult, CompositionResultStatus status, CompositionDiagnosticSet diagnostics,
        CompositionVerificationResult? adapterVerification, CompositionIr? ir, object projectionSource,
        bool adapterRequired, CompositionOwnerReplayReceipt receipt)
    {
        OriginalRequest = originalRequest; ExecutedRequest = executedRequest; OwnerResult = ownerResult;
        OwnerStatus = status; OwnerDiagnostics = diagnostics; AdapterVerification = adapterVerification;
        Ir = ir; ProjectionSource = projectionSource; Receipt = receipt;
        RequiredOutcomeChecksMatch = receipt.ExpectedStatusAndDiagnosticsMatch && receipt.OriginalStatusAndStableCodeMatch &&
            receipt.AllScheduledCheckpointsObserved && receipt.PrerequisiteOutcomeChecksMatch && receipt.OwnerObservationChecksMatch &&
            (!adapterRequired || receipt.AdapterVerificationSucceeded);
    }
    [JsonIgnore] public CompositionFixtureRequest OriginalRequest { get; }
    [JsonIgnore] public CompositionFixtureRequest ExecutedRequest { get; }
    public CompositionFixtureAnchor Anchor => OriginalRequest.Anchor;
    [JsonIgnore] public object OwnerResult { get; }
    public CompositionResultStatus OwnerStatus { get; }
    public CompositionDiagnosticSet OwnerDiagnostics { get; }
    [JsonIgnore] public CompositionVerificationResult? AdapterVerification { get; }
    [JsonIgnore] public CompositionIr? Ir { get; }
    [JsonIgnore] public CompositionPublicView? PublicView => Ir?.PublicView;
    public bool HasAcceptedPublicView => Ir is not null;
    [JsonIgnore] public object ProjectionSource { get; }
    public CompositionOwnerReplayReceipt Receipt { get; }
    public bool RequiredOutcomeChecksMatch { get; }
    public bool OwnerObservedFieldsPending => Receipt.OwnerObservedFieldsPending;
    public bool ReadyForProjection => RequiredOutcomeChecksMatch && !OwnerObservedFieldsPending;
    // This cannot classify a mismatch or a pending owner-field check as an exclusion.
    public bool SemanticNonProjectable => ReadyForProjection && OwnerStatus == CompositionResultStatus.Accepted && !HasAcceptedPublicView;
}

/// <summary>Explicit test-host owner replay before manifest freeze. Never invokes consumer Project/Refresh.</summary>
public static class CompositionOwnerReplay
{
    public static CompositionOwnerReplayResult Prepare(CompositionProducerInputs inputs, CompositionFixtureAnchor anchor)
    {
        ArgumentNullException.ThrowIfNull(inputs); ArgumentNullException.ThrowIfNull(anchor);
        var limits = CompositionLimitProfile.ConformanceV1;
        var cache = new Dictionary<string, Executed>(StringComparer.Ordinal);
        var active = new HashSet<string>(StringComparer.Ordinal);
        var operations = new List<CompositionReplayOperationReceipt>();
        var prerequisites = new List<CompositionReplayPrerequisiteReceipt>();
        var attempted = 0;
        var root = Replay(anchor, 0);
        var receipt = new CompositionOwnerReplayReceipt(anchor, root.Status, Copy(root.Diagnostics.Diagnostics),
            root.Original.Expectation!.Status, Copy(root.Original.Expectation.DiagnosticCodes),
            CopyFields(root.Original.Expectation.ObservedFields), Original(root.OriginalObservation),
            root.ExpectedMatches, root.OriginalMatches, operations.All(item => item.AllScheduledCheckpointsObserved),
            prerequisites.All(item => item.ExactMatch && item.RequiredOutcomeChecksMatch),
            !root.AdapterRequired || root.Adapter?.IsAccepted == true,
            false, root.ObservationMatches ? "VerifiedOwnerObservedFields" : "OwnerObservedFieldsMismatch",
            Copy(operations), Copy(prerequisites), Snapshot(root.Observation), root.ObservationMatches,
            CopyFields(root.Original.ExpectedObservedFields));
        return new(root.Original, root.Request, root.Result, root.Status, root.Diagnostics, root.Adapter,
            root.Ir, root.ProjectionSource, root.AdapterRequired, receipt);

        Executed Replay(CompositionFixtureAnchor expectedAnchor, int depth)
        {
            if (!inputs.Corpus.Fixtures.TryGetValue(expectedAnchor.FixtureIdentity, out var fixture) || fixture.Anchor != expectedAnchor)
                throw Unavailable("PREREQUISITE-ANCHOR", "The exact declared fixture anchor is absent from the admitted corpus.");
            if (cache.TryGetValue(expectedAnchor.FixtureIdentity, out var cached)) return cached;
            if (depth > limits.MaxRecursionDepth || ++attempted > limits.MaxVisitedIdentities)
                throw Unavailable("REPLAY-BOUND", "The finite replay prerequisite traversal bound was exceeded.");
            if (!active.Add(expectedAnchor.FixtureIdentity))
                throw Unavailable("PREREQUISITE-CYCLE", "Declared owner fixture prerequisites contain a cycle.");
            try
            {
                var original = Decode(expectedAnchor);
                var request = original;
                if (request.EncodePrerequisite is { } encode)
                {
                    var prior = RequiredPrior(encode, expectedAnchor, "encode-record", depth + 1);
                    request = prior.Result switch
                    {
                        CompositionCompilationResult compiled => request.BindEncodeRecord(prior.Original.Anchor, compiled),
                        CompositionDecodeResult decoded => request.BindEncodeRecord(prior.Original.Anchor, decoded),
                        _ => throw Unavailable("PREREQUISITE-KIND", "Encode requires its actual declared Compile or Decode prerequisite.")
                    };
                }
                var facts = new Dictionary<string, CompositionFixturePrerequisiteResult>(StringComparer.Ordinal);
                if (request.ReferenceResponses.Count != 0 || request.ReferencePrerequisite is not null)
                {
                    if (request.ReferenceResponses.Count > limits.MaxReferences)
                        throw Unavailable("REPLAY-BOUND", "Declared fixture reference responses exceed the replay bound.");
                    var referencePrerequisites = request.ReferenceResponses.Where(item => item.RecordSource is not null)
                        .Select(item => item.RecordSource!)
                        .Concat(request.ReferencePrerequisite is { } standalone ? [standalone] : []).Distinct();
                    foreach (var prerequisite in referencePrerequisites)
                    {
                        var prior = RequiredPrior(prerequisite, expectedAnchor, "exact-reference", depth + 1);
                        facts[prerequisite.Anchor.FixtureIdentity] = Fact(prior);
                    }
                }

                // Always use the actual owner driver. Raw null declarations remain null;
                // checkpoint schedules and their fixture clock are never replaced with defaults.
                using var driver = new CompositionFixtureExecutionDriver(request);
                IDefinitionReferenceSource? references = request.ReferenceResponses.Count != 0 ||
                    request.ReferencePrerequisite is not null || request.Kind == CompositionFixtureRequestKind.GetExact
                    ? driver.CreateReferenceSource(facts.Values) : null;
                object result = request.Kind switch
                {
                    CompositionFixtureRequestKind.Compile => driver.Compiler.Compile(request.CompileRequest!, references, driver.Profile!, driver.Context!),
                    CompositionFixtureRequestKind.GetExact => InvokeGetExact(request, driver, references),
                    CompositionFixtureRequestKind.Decode => driver.Exchange.Decode(request.DocumentBytes!.Value, driver.Profile!, driver.Context!),
                    CompositionFixtureRequestKind.Verify => driver.Exchange.Verify(request.DocumentBytes!.Value, driver.Profile!, driver.Context!),
                    CompositionFixtureRequestKind.Encode when request.EncodeRecord is not null => driver.Exchange.Encode(request.EncodeRecord, driver.Profile!, driver.Context!),
                    CompositionFixtureRequestKind.Inspect => driver.Migration.Inspect(request.CompatibilityRequest!, request.MigrationCatalog!, driver.Profile!, driver.Context!),
                    CompositionFixtureRequestKind.Negotiate => driver.Migration.Negotiate(request.CompatibilityRequest!, request.MigrationCatalog!, driver.Profile!, driver.Context!),
                    CompositionFixtureRequestKind.Migrate => driver.Migration.Migrate(request.MigrationRequest!, request.MigrationCatalog!, driver.Profile!, driver.Context!),
                    _ => throw Unavailable("REQUEST-VARIANT", "The declared owner operation has no bound executable request.")
                };
                var observed = Observe(result);
                var (status, diagnostics) = result is DefinitionReferenceResult referenceResult
                    ? (observed.Status, referenceResult.Diagnostics) : Outcome(result);
                var originalObservation = inputs.Producer.Candidate.Observations.Single(item => item.CaseIdentity == expectedAnchor.CaseIdentity);
                var expectedMatches = original.Expectation!.Status == status.ToString() &&
                    original.Expectation.DiagnosticCodes.SequenceEqual(diagnostics.Diagnostics.Select(item => item.Code), StringComparer.Ordinal);
                var originalMatches = originalObservation.Status == status &&
                    originalObservation.StableCode == (diagnostics.IsEmpty ? "ACCEPTED" : diagnostics[0].Code) &&
                    originalObservation.BeforeStateDigest == originalObservation.AfterStateDigest;
                var observationMatches = MatchesObservation(original, status, diagnostics, observed, originalObservation);
                operations.Add(new(operations.Count, expectedAnchor, "declared-fixture", expectedAnchor.DeclaredOperationIdentity,
                    status, Copy(diagnostics.Diagnostics), Invocation(original), Copy(driver.ObservedCheckpoints),
                    driver.AllScheduledCheckpointsObserved, Snapshot(observed)));

                CompositionVerificationResult? adapter = null;
                // Accepted wire outputs get an explicit new verification invocation.
                // Rejected Decode is projected directly from its actual owner result.
                ReadOnlyMemory<byte>? verifyBytes = result switch
                {
                    CompositionDecodeResult { IsAccepted: true } => original.DocumentBytes,
                    CompositionEncodeResult { IsAccepted: true } encoded => encoded.Document!.CanonicalBytes,
                    CompositionMigrationResult { IsAccepted: true } migrated => migrated.Successor!.CanonicalBytes,
                    _ => null
                };
                var adapterRequired = status == CompositionResultStatus.Accepted && verifyBytes.HasValue;
                if (verifyBytes is { } bytes)
                {
                    var clock = new ReplayClock();
                    var profile = CompositionLimitProfileDeclaration.ConformanceV1;
                    var context = new CompositionOperationContextDeclaration("ghostagram-owner-verify:" + expectedAnchor.RealizedRawByteDigest,
                        clock.GetUtcNow().AddSeconds(25), CancellationToken.None);
                    var exchange = new CompositionExchange(new CompositionProfileAdmission(), new CompositionOperationContextAdmission(clock), clock);
                    adapter = exchange.Verify(bytes, profile, context);
                    operations.Add(new(operations.Count, expectedAnchor, "projection-verification", "ICompositionExchange.Verify",
                        Status(adapter.Status), Copy(adapter.Diagnostics.Diagnostics),
                        new(profile, true, context.CorrelationId, context.AbsoluteDeadlineUtc, CompositionFixtureCancellation.Active,
                            DateTimeOffset.UnixEpoch, Array.Empty<CompositionFixtureCheckpoint>()), Array.Empty<string>(), true,
                        Snapshot(CompositionResultObservation.Project(adapter))));
                }
                var ir = status != CompositionResultStatus.Accepted ? null : result switch
                {
                    CompositionCompilationResult compiled => compiled.Ir,
                    CompositionVerificationResult verified when verified.IsAccepted => verified.Document?.Record as CompositionIr,
                    _ when adapter?.IsAccepted == true => adapter.Document?.Record as CompositionIr,
                    _ => null
                };
                object projectionSource = status == CompositionResultStatus.Accepted && adapter is not null ? adapter : result;
                var complete = new Executed(original, request, result, status, diagnostics, adapter, ir, projectionSource,
                    adapterRequired, originalObservation, expectedMatches, originalMatches, driver.AllScheduledCheckpointsObserved,
                    observed, observationMatches);
                cache.Add(expectedAnchor.FixtureIdentity, complete);
                return complete;
            }
            finally { active.Remove(expectedAnchor.FixtureIdentity); }
        }

        CompositionFixtureRequest Decode(CompositionFixtureAnchor expectedAnchor)
        {
            var operation = CompositionFixtureOperations.Require(expectedAnchor.DeclaredOperationIdentity);
            if (operation.Classification != "semantic-output" || expectedAnchor.Classification != CompositionFixtureClassification.SemanticOutput ||
                operation.ResultSchemaIdentity != expectedAnchor.ResultSchemaIdentity)
                throw Unavailable("FIXTURE-CLASSIFICATION", "The fixture does not match its declared semantic owner operation and result schema.");
            var clock = new ReplayClock();
            var profile = new CompositionProfileAdmission().Admit(CompositionLimitProfileDeclaration.ConformanceV1);
            var context = new CompositionOperationContextAdmission(clock).Admit(new("ghostagram-fixture-decode:" + expectedAnchor.RealizedRawByteDigest,
                clock.GetUtcNow().AddSeconds(25), CancellationToken.None), profile.Profile!);
            var decoded = new CompositionFixtureRequestDecoder(clock).Decode(inputs.Corpus, expectedAnchor.FixtureIdentity, profile.Profile!, context.Context!);
            if (!decoded.IsAccepted || decoded.Request!.Anchor != expectedAnchor)
                throw Unavailable("FIXTURE-DECODE", "The owner decoder did not return the exact admitted realized request.");
            if (!decoded.Request.HasInvocationDeclaration || decoded.Request.Expectation is null)
                throw Unavailable("INVOCATION-INCOMPLETE", "The owner fixture lacks its explicit raw invocation or expected result.");
            return decoded.Request;
        }

        Executed RequiredPrior(CompositionFixtureRecordPrerequisite prerequisite, CompositionFixtureAnchor consumer, string purpose, int depth)
        {
            if (prerequisite.Anchor.DeclaredOperationIdentity is not ("ICompositionCompiler.Compile" or "ICompositionExchange.Decode"))
                throw Unavailable("PREREQUISITE-KIND", "A record prerequisite must explicitly identify Compile or Decode.");
            var prior = Replay(prerequisite.Anchor, depth);
            if (prior.Status != CompositionResultStatus.Accepted)
                throw Unavailable("PREREQUISITE-REJECTED", "The declared record prerequisite returned an actual rejected owner result.");
            var fact = Fact(prior);
            var exact = fact.Anchor == prerequisite.Anchor && fact.OutputKind == prerequisite.OutputKind && fact.OutputDigest == prerequisite.OutputDigest;
            var matches = prior.ExpectedMatches && prior.OriginalMatches && prior.CheckpointsObserved && prior.ObservationMatches;
            prerequisites.Add(new(consumer, prerequisite.Anchor, purpose, prerequisite.OutputKind, prerequisite.OutputDigest,
                fact.OutputKind, fact.OutputDigest, prior.Status, exact, matches, false, Snapshot(prior.Observation)));
            if (!exact || !matches)
                throw Unavailable("PREREQUISITE-MISMATCH", "The actual prerequisite differs from its frozen digest, expected outcome, exact owner observation or checkpoint schedule.");
            return prior;
        }
    }

    private static CompositionFixturePrerequisiteResult Fact(Executed prior) => prior.Result switch
    {
        CompositionCompilationResult compiled => CompositionFixturePrerequisiteResult.FromCompile(prior.Original.Anchor, compiled),
        CompositionDecodeResult decoded => CompositionFixturePrerequisiteResult.FromDecode(prior.Original.Anchor, decoded),
        _ => throw Unavailable("PREREQUISITE-KIND", "The actual prerequisite is neither Compile nor Decode.")
    };
    private static DefinitionReferenceResult InvokeGetExact(CompositionFixtureRequest request,
        CompositionFixtureExecutionDriver driver, IDefinitionReferenceSource? source)
    {
        if (source is null || request.ReferenceRequest is null)
            throw Unavailable("REFERENCE-REQUEST", "The owner decoder did not supply the exact reference request and source.");
        CompositionOperationContext? context = null;
        // An explicitly absent context is forwarded as the actual GetExact input.
        // Failed preparation is availability evidence; it cannot become a reference result.
        if (driver.Context is not null)
        {
            var prepared = driver.AdmitReferenceContext();
            if (!prepared.IsAccepted)
                throw Unavailable("REFERENCE-CONTEXT-PREPARATION", "The owner rejected reference context preparation: " +
                    string.Join(",", prepared.Diagnostics.Diagnostics.Select(diagnostic => diagnostic.Code)));
            context = prepared.Context!;
        }
        return source.GetExact(request.ReferenceRequest, context!);
    }
    private static (CompositionResultStatus Status, CompositionDiagnosticSet Diagnostics) Outcome(object result) => result switch
    {
        CompositionCompilationResult value => (value.Status, value.Diagnostics),
        CompositionDecodeResult value => (Status(value.Status), value.Diagnostics),
        CompositionVerificationResult value => (Status(value.Status), value.Diagnostics),
        CompositionEncodeResult value => (Status(value.Status), value.Diagnostics),
        CompatibilityInspectionResult value => (Status(value.Status), value.Diagnostics),
        CompatibilitySelectionResult value => (Status(value.Status), value.Diagnostics),
        CompositionMigrationResult value => (Status(value.Status), value.Diagnostics),
        _ => throw Unavailable("OWNER-RESULT-KIND", "The owner returned an unsupported result type.")
    };
    private static CompositionResultObservation Observe(object result) => result switch
    {
        CompositionCompilationResult value => CompositionResultObservation.Project(value),
        DefinitionReferenceResult value => CompositionResultObservation.Project(value),
        CompositionDecodeResult value => CompositionResultObservation.Project(value),
        CompositionVerificationResult value => CompositionResultObservation.Project(value),
        CompositionEncodeResult value => CompositionResultObservation.Project(value),
        CompatibilityInspectionResult value => CompositionResultObservation.Project(value),
        CompatibilitySelectionResult value => CompositionResultObservation.Project(value),
        CompositionMigrationResult value => CompositionResultObservation.Project(value),
        _ => throw Unavailable("OWNER-RESULT-KIND", "The actual owner result has no shared observation contract.")
    };
    private static CompositionReplayObservedResult Snapshot(CompositionResultObservation observation) => new(
        observation.OperationIdentity, observation.ResultSchemaIdentity, observation.Status,
        Copy(observation.Diagnostics.Diagnostics), observation.ObservedFields.Clone(), Copy(observation.RequiredFieldNames));

    private static bool MatchesObservation(CompositionFixtureRequest request, CompositionResultStatus status,
        CompositionDiagnosticSet diagnostics, CompositionResultObservation observed, ProducerCaseObservation original)
    {
        var fields = observed.ObservedFields;
        if (observed.OperationIdentity != request.Anchor.DeclaredOperationIdentity ||
            observed.ResultSchemaIdentity != request.Anchor.ResultSchemaIdentity || observed.Status != status ||
            !observed.Diagnostics.Diagnostics.SequenceEqual(diagnostics.Diagnostics) ||
            !fields.EnumerateObject().Select(field => field.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(observed.RequiredFieldNames, StringComparer.Ordinal)) return false;
        // Preserve the owner's semantic expectation rule and exact JSON scalar/array comparison.
        if (status == CompositionResultStatus.Accepted && !request.Expectation!.ObservedFields.Keys.Any(key => key != "accepted")) return false;
        if (!MatchesSubset(request.Expectation!.ObservedFields, fields) || !MatchesSubset(request.ExpectedObservedFields, fields)) return false;
        try
        {
            using var document = JsonDocument.Parse(original.ResultBytes);
            var published = document.RootElement;
            var exactDiagnostics = JsonSerializer.SerializeToElement(observed.Diagnostics.Diagnostics.Select(
                diagnostic => new { diagnostic.Code, diagnostic.Category, diagnostic.Severity, diagnostic.Detail, diagnostic.Path }));
            return published.ValueKind == JsonValueKind.Object &&
                TextMatches(published, "operation", observed.OperationIdentity) &&
                TextMatches(published, "resultSchemaIdentity", observed.ResultSchemaIdentity) &&
                TextMatches(published, "fixtureIdentity", request.Anchor.FixtureIdentity) &&
                TextMatches(published, "realizedRawByteDigest", request.Anchor.RealizedRawByteDigest) &&
                TextMatches(published, "status", status.ToString()) &&
                published.TryGetProperty("diagnostics", out var publishedDiagnostics) && JsonEqual(exactDiagnostics, publishedDiagnostics) &&
                published.TryGetProperty("observedFields", out var publishedFields) && JsonEqual(fields, publishedFields);
        }
        catch (JsonException) { return false; }
    }
    private static bool MatchesSubset(IReadOnlyDictionary<string, JsonElement> expected, JsonElement fields) =>
        expected.All(field => fields.TryGetProperty(field.Key, out var actual) && JsonEqual(field.Value, actual));
    private static bool TextMatches(JsonElement value, string name, string expected) =>
        value.TryGetProperty(name, out var actual) && actual.ValueKind == JsonValueKind.String && actual.GetString() == expected;
    // Matches the owner runner's JsonEqual: object order is immaterial, array order and number text are exact.
    private static bool JsonEqual(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind) return false;
        return left.ValueKind switch
        {
            JsonValueKind.Object => left.EnumerateObject().Count() == right.EnumerateObject().Count() &&
                left.EnumerateObject().All(property => right.TryGetProperty(property.Name, out var other) && JsonEqual(property.Value, other)),
            JsonValueKind.Array => left.GetArrayLength() == right.GetArrayLength() &&
                left.EnumerateArray().Zip(right.EnumerateArray()).All(pair => JsonEqual(pair.First, pair.Second)),
            JsonValueKind.String => left.GetString() == right.GetString(),
            _ => left.GetRawText() == right.GetRawText()
        };
    }
    private static CompositionResultStatus Status(CompositionExchangeStatus status) => status switch
    {
        CompositionExchangeStatus.Accepted => CompositionResultStatus.Accepted,
        CompositionExchangeStatus.Rejected => CompositionResultStatus.Rejected,
        CompositionExchangeStatus.Cancelled => CompositionResultStatus.Cancelled,
        CompositionExchangeStatus.DeadlineExpired => CompositionResultStatus.DeadlineExpired,
        CompositionExchangeStatus.InternalFailure => CompositionResultStatus.InternalFailure,
        _ => throw Unavailable("OWNER-STATUS", "The owner returned an unknown exchange status.")
    };
    private static CompositionReplayInvocationReceipt Invocation(CompositionFixtureRequest request) => new(request.RawProfile,
        request.ContextDescriptor is not null, request.ContextDescriptor?.CorrelationId, request.ContextDescriptor?.AbsoluteDeadlineUtc,
        request.ContextDescriptor?.Cancellation, request.ContextDescriptor?.ClockStartUtc,
        Copy(request.ContextDescriptor?.Checkpoints ?? []));
    private static CompositionReplayProducerReceipt Original(ProducerCaseObservation observation) => new(observation.CaseIdentity,
        observation.Status, observation.StableCode, Convert.ToHexStringLower(SHA256.HashData(observation.ResultBytes.Span)),
        observation.BeforeStateDigest, observation.AfterStateDigest);
    private static IReadOnlyDictionary<string, JsonElement> CopyFields(IReadOnlyDictionary<string, JsonElement> fields) =>
        new ReadOnlyDictionary<string, JsonElement>(fields.ToDictionary(item => item.Key, item => item.Value.Clone(), StringComparer.Ordinal));
    private static IReadOnlyList<T> Copy<T>(IEnumerable<T> items) => Array.AsReadOnly(items.ToArray());
    private static CompositionCaseUnavailableException Unavailable(string code, string detail) => new("GRAM-COMP-" + code, detail);
    private sealed class ReplayClock : TimeProvider
    { public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch; }
    private sealed record Executed(CompositionFixtureRequest Original, CompositionFixtureRequest Request, object Result,
        CompositionResultStatus Status, CompositionDiagnosticSet Diagnostics, CompositionVerificationResult? Adapter, CompositionIr? Ir,
        object ProjectionSource, bool AdapterRequired, ProducerCaseObservation OriginalObservation,
        bool ExpectedMatches, bool OriginalMatches, bool CheckpointsObserved,
        CompositionResultObservation Observation, bool ObservationMatches);
}
