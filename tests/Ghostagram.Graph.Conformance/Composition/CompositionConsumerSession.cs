using System.Security.Cryptography;
using System.Text.Json;
using Ghostagram.Bridge.DeclarativeCompositionProjection;
using Ghostagram.Bridge.Tests.DeclarativeComposition;
using Ghostworx.System.Composition;
using Ghostworx.System.Composition.Compiler;
using Ghostworx.System.Composition.Conformance;
using Ghostworx.System.Composition.Conformance.Support;

namespace Ghostagram.Graph.Conformance.Composition;

public sealed record CompositionExecutableCase(CompositionConsumerCase Manifest, Func<CompositionScenarioResult> Execute);
public sealed record CompositionProducerInputs(CorpusArtifactCatalog Catalog, AdmittedCorpusSnapshot Corpus, ProducerResultEnvelope Producer);

/// <summary>Coordinates actual consumer cases, externally frozen manifests and owner-only envelope admission.</summary>
public static class CompositionConsumerSession
{
    public static IReadOnlyList<string> ProjectionFields { get; } = Array.AsReadOnly(new[]
    { "assertion", "assertionSatisfied", "localObservation", "projectionStatus", "projectionSha256", "sourceDigest", "diagnosticCodes" });

    public static CompositionProducerInputs LoadProducer(string corpusRoot, string producerEnvelopePath,
        ICorpusPublishedResultSource? publishedResultSource = null)
    {
        var profiles = new CompositionProfileAdmission();
        var contexts = new CompositionOperationContextAdmission();
        publishedResultSource?.RequireAvailable();
        var catalog = publishedResultSource is null
            ? CorpusArtifactCatalog.Load(corpusRoot)
            : CorpusArtifactCatalog.Load(corpusRoot, publishedResultSource);
        var request = catalog.CreateAdmissionRequest();
        var admitted = new CorpusAdmission(profiles, contexts, new AdmittedCorpusAdmissionCore())
            .Admit(request, ProjectionFixtures.Profile(), ProjectionFixtures.Context(correlationId: "ghostagram-corpus-import"));
        if (!admitted.IsAccepted) throw new InvalidDataException("System rejected the producer corpus: " + Codes(admitted.Diagnostics));
        publishedResultSource?.RequireAvailable();
        ConformancePurposeRegistry.RequireNormative(admitted.Snapshot!.Purpose);
        const long normativeEnvelopeBytes = 4_194_304;
        var limit = normativeEnvelopeBytes;
        var bytes = ReadBounded(producerEnvelopePath, limit);
        var producer = new ProducerEnvelopeReader(profiles, contexts, new AdmittedProducerEnvelopeAdmissionCore())
            .Admit(bytes, admitted.Snapshot!, ProjectionFixtures.Profile(), ProjectionFixtures.Context(correlationId: "ghostagram-producer-import"));
        if (!producer.IsAccepted) throw new InvalidDataException("System rejected the persisted producer envelope: " + Codes(producer.Diagnostics));
        publishedResultSource?.RequireAvailable();
        ConformancePurposeRegistry.RequireNormative(producer.Envelope!.Purpose);
        return new(catalog, admitted.Snapshot!, producer.Envelope!);
    }

    public static IReadOnlyList<CompositionExecutableCase> LocalCases(IReadOnlyDictionary<string, string> fixtureDigests)
    {
        var source = new Lazy<CompositionCompilationResult>(() => ProjectionFixtures.CompileNested());
        var outputSize = new Lazy<IReadOnlyList<ProjectionBoundaryObservation>>(() => ProjectionOutputSizeCases.Observe(ProjectionOutputSizeCases.Find()));
        return CompositionProjectionScenarios.CaseIds.Select(caseId => new CompositionExecutableCase(
            new(caseId, CompositionProjectionScenarios.Operation(caseId), fixtureDigests, ProjectionFields),
            () => CompositionProjectionScenarios.Execute(caseId)))
            .Concat(ProjectionBoundaryCases.CaseIds.Select(caseId => new CompositionExecutableCase(
                new(caseId, caseId.StartsWith("GRAM-LOCAL-", StringComparison.Ordinal)
                    ? "DeclarativeCompositionProjection.Sidecar.Admit" : "DeclarativeCompositionProjection.Project", fixtureDigests, ProjectionFields),
                () =>
                {
                    var actual = ProjectionBoundaryCases.ExecuteCase(caseId, source.Value);
                    return new(actual.Result, actual.Passed, actual.Expected, actual.Observed, actual.Outcome);
                })))
            .Concat(ProjectionOutputSizeCases.CaseIds.Select(caseId => new CompositionExecutableCase(
                new(caseId, "DeclarativeCompositionProjection.Project", fixtureDigests, ProjectionFields),
                () =>
                {
                    var actual = outputSize.Value.Single(item => item.CaseId == caseId);
                    return new(actual.Result, actual.Passed, actual.Expected, actual.Observed, actual.Outcome);
                }))).ToArray();
    }

    public static ConsumerCaseManifest Materialize(IReadOnlyList<CompositionExecutableCase> cases,
        IReadOnlyList<KeyValuePair<string, ReadOnlyMemory<byte>>> artifacts, string outputPath)
    {
        var manifest = CompositionConsumerProtocol.MaterializeManifest(cases.Select(item => item.Manifest), artifacts);
        if (manifest.CanonicalBytes.Length > CompositionLimitProfile.ConformanceV1.MaxDocumentBytes)
            throw new InvalidDataException("The concrete manifest exceeds the owner document bound.");
        WriteNew(outputPath, manifest.CanonicalBytes.Span);
        // This digest is a handoff value. A subsequent run must receive the externally frozen value explicitly.
        Console.WriteLine("Materialized consumer manifest; digest=" + manifest.Digest);
        return manifest;
    }

    public static int Run(CompositionProducerInputs inputs, IReadOnlyList<CompositionExecutableCase> cases,
        IReadOnlyList<KeyValuePair<string, ReadOnlyMemory<byte>>> artifacts, string manifestPath,
        string expectedManifestDigest, string outputDirectory, string sourceRevision)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ConformancePurposeRegistry.RequireNormative(inputs.Corpus.Purpose);
        ConformancePurposeRegistry.RequireNormative(inputs.Producer.Purpose);
        if (string.IsNullOrWhiteSpace(sourceRevision))
            throw new ArgumentException("An attributable consumer source revision is required.", nameof(sourceRevision));
        var manifest = CompositionConsumerProtocol.ReadManifest(ReadBounded(manifestPath, CompositionLimitProfile.ConformanceV1.MaxDocumentBytes));
        if (string.IsNullOrWhiteSpace(expectedManifestDigest) || manifest.Digest != expectedManifestDigest)
            throw new InvalidDataException("Consumer manifest differs from the externally supplied frozen digest.");
        var current = CompositionConsumerProtocol.MaterializeManifest(cases.Select(item => item.Manifest), artifacts);
        if (!current.CanonicalBytes.Span.SequenceEqual(manifest.CanonicalBytes.Span))
            throw new InvalidDataException("Concrete cases or fixture bytes changed since manifest materialization.");
        if (Directory.Exists(outputDirectory) && Directory.EnumerateFileSystemEntries(outputDirectory).Any())
            throw new IOException("Consumer output directory must be new or empty.");
        Directory.CreateDirectory(outputDirectory);
        var started = DateTimeOffset.UtcNow;
        var observations = new List<CompositionConsumerObservation>(cases.Count);
        var outputs = new List<object>(cases.Count);
        for (var index = 0; index < cases.Count; index++)
        {
            var test = cases[index];
            try
            {
                var actual = test.Execute();
                var bytes = actual.Result is null ? null : CompositionConsumerProtocol.SerializeProjection(actual.Result);
                var digest = bytes is null ? null : Convert.ToHexStringLower(SHA256.HashData(bytes));
                var path = bytes is null ? null : $"projection-{index + 1:0000}.json";
                if (bytes is not null) WriteNew(Path.Combine(outputDirectory, path!), bytes);
                var result = actual.Result;
                var diagnostics = result is null ? [] : result.SourceDiagnostics.Select(item => new CompositionConsumerDiagnostic(item.Code, item.Detail, item.Path))
                    .Concat(result.Diagnostics.Select(item => new CompositionConsumerDiagnostic(item.Code, item.Message, item.Path))).ToArray();
                var fields = new Dictionary<string, object?>
                    {
                        ["assertion"] = actual.Assertion, ["assertionSatisfied"] = actual.Passed,
                        ["localObservation"] = actual.LocalObservation,
                        ["projectionStatus"] = result?.Status.ToString(), ["projectionSha256"] = digest,
                        ["sourceDigest"] = result?.SourceAnchor?.ContentDigest,
                        ["diagnosticCodes"] = diagnostics.Select(item => item.Code).ToArray()
                    };
                foreach (var field in actual.AdditionalFields ?? new Dictionary<string, object?>()) fields.Add(field.Key, field.Value);
                if (!fields.Keys.Order(StringComparer.Ordinal).SequenceEqual(test.Manifest.RequiredObservedFields.Order(StringComparer.Ordinal)))
                    throw new InvalidOperationException("Actual observation fields differ from the frozen concrete case.");
                observations.Add(new(test.Manifest.CaseId, test.Manifest.OperationIdentity, test.Manifest.FixtureDigests,
                    result is null ? actual.AdmissionOutcome ?? throw new InvalidOperationException("An admission-only case must retain its actual outcome.") : Outcome(result),
                    result is null ? "PresentationAdmission" : "Projection", diagnostics, fields, actual.Passed));
                outputs.Add(new { caseId = test.Manifest.CaseId, path, sha256 = digest, status = result?.Status.ToString(), actual.Passed });
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
            {
                // The runner records failure, never exception internals or an invented projection.
                var unavailable = exception as CompositionCaseUnavailableException;
                var code = unavailable?.Code ?? "GRAM-COMP-CASE-FAILED";
                var detail = unavailable?.PublicDetail ?? "The concrete consumer case did not produce a complete observation.";
                var fields = test.Manifest.RequiredObservedFields.ToDictionary(name => name, _ => (object?)null, StringComparer.Ordinal);
                fields["assertion"] = "Concrete consumer execution completed.";
                fields["assertionSatisfied"] = false;
                fields["diagnosticCodes"] = new[] { code };
                observations.Add(new(test.Manifest.CaseId, test.Manifest.OperationIdentity, test.Manifest.FixtureDigests,
                    unavailable is null ? CompositionResultStatus.InternalFailure : CompositionResultStatus.Unsupported,
                    unavailable is null ? "ConsumerRunner" : "ConsumerRunnerAvailability", [new(code, detail, null)],
                    fields, false, unavailable is not null));
                outputs.Add(new { caseId = test.Manifest.CaseId, path = (string?)null, passed = false });
            }
        }
        WriteNew(Path.Combine(outputDirectory, "projection-index.json"), JsonSerializer.SerializeToUtf8Bytes(outputs, CompositionConsumerProtocol.JsonOptions));
        var ended = DateTimeOffset.UtcNow;
        var correlation = "ghostagram-consumer-" + Guid.NewGuid().ToString("N");
        var candidate = CompositionConsumerProtocol.CreateCandidate(inputs.Corpus, inputs.Producer, manifest, observations, [],
            started, ended, correlation, sourceRevision);
        var admitted = new ConsumerEnvelopeAdmission(new CompositionProfileAdmission(), new CompositionOperationContextAdmission(),
            new AdmittedConsumerEnvelopeAdmissionCore()).Admit(new(candidate, inputs.Corpus, inputs.Producer, manifest),
                ProjectionFixtures.Profile(), ProjectionFixtures.Context(correlationId: correlation));
        if (!admitted.IsAccepted)
        {
            WriteNew(Path.Combine(outputDirectory, "unadmitted-consumer-candidate.json"), candidate.Bytes.Span);
            WriteNew(Path.Combine(outputDirectory, "admission-diagnostics.json"),
                JsonSerializer.SerializeToUtf8Bytes(admitted.Diagnostics.Diagnostics, CompositionConsumerProtocol.JsonOptions));
            Console.Error.WriteLine("System rejected consumer envelope admission: " + Codes(admitted.Diagnostics));
            return 1;
        }
        ConformancePurposeRegistry.RequireNormative(admitted.Envelope!.Purpose);
        WriteNew(Path.Combine(outputDirectory, "consumer-envelope.json"), admitted.Envelope!.CanonicalBytes.Span);
        var passed = observations.Count(item => item.Passed && !item.Unsupported);
        Console.WriteLine($"Actual composition consumer cases: {passed}/{observations.Count} passed; admitted envelope {admitted.Envelope.EnvelopeDigest}.");
        return passed == observations.Count ? 0 : 1;
    }

    public static byte[] ReadBounded(string path, long maximum)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > maximum || file.Length > int.MaxValue) throw new InvalidDataException("Input exceeds its admitted byte bound.");
        var bytes = new byte[(int)file.Length]; file.ReadExactly(bytes);
        if (file.ReadByte() != -1) throw new InvalidDataException("Input changed while reading.");
        return bytes;
    }
    private static void WriteNew(string path, ReadOnlySpan<byte> bytes)
    {
        var full = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        using var file = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None); file.Write(bytes);
    }
    private static string Codes(CompositionDiagnosticSet diagnostics) => string.Join(",", diagnostics.Diagnostics.Select(item => item.Code));
    private static CompositionResultStatus Outcome(CompositionProjectionResult result) => result.Status switch
    {
        CompositionProjectionStatus.Fresh => CompositionResultStatus.Accepted,
        CompositionProjectionStatus.Unsupported => CompositionResultStatus.Unsupported,
        _ => CompositionResultStatus.Rejected
    };
}
