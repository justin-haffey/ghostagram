using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ghostagram.Bridge.DeclarativeCompositionProjection;
using Ghostworx.System.Composition;
using Ghostworx.System.Composition.Conformance;
using Ghostworx.System.Composition.Conformance.Support;

namespace Ghostagram.Graph.Conformance.Composition;

public sealed record CompositionConsumerCase(string CaseId, string OperationIdentity,
    IReadOnlyDictionary<string, string> FixtureDigests, IReadOnlyList<string> RequiredObservedFields,
    string Applicability = "required");
public sealed record CompositionConsumerDiagnostic(string Code, string Detail, string? PublicPath);
public sealed record CompositionConsumerObservation(string CaseId, string OperationIdentity,
    IReadOnlyDictionary<string, string> FixtureDigests, CompositionResultStatus Outcome, string Category,
    IReadOnlyList<CompositionConsumerDiagnostic> Diagnostics, IReadOnlyDictionary<string, object?> ObservedFields,
    bool Passed, bool Unsupported = false);

/// <summary>Writes the System-owned consumer format and preserves actual Project/Refresh outputs.</summary>
public static class CompositionConsumerProtocol
{
    public const string ConsumerIdentity = "ghostagram";
    public const string ManifestIdentity = "ghostagram.composition.projection.cases";
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static ConsumerCaseManifest MaterializeManifest(IEnumerable<CompositionConsumerCase> cases,
        IEnumerable<KeyValuePair<string, ReadOnlyMemory<byte>>> localArtifacts)
    {
        var entries = cases.ToArray();
        if (entries.Length == 0 || entries.Select(item => item.CaseId).Distinct(StringComparer.Ordinal).Count() != entries.Length)
            throw new ArgumentException("A concrete, nonempty, duplicate-free case inventory is required.");
        var bytes = Canonical(JsonSerializer.SerializeToElement(new
        {
            identity = ManifestIdentity,
            version = AdmittedConsumerEnvelopeAdmissionCore.ManifestVersion,
            consumerIdentity = ConsumerIdentity,
            cases = entries.Select(item => new
            {
                caseId = item.CaseId, operationIdentity = item.OperationIdentity,
                fixtureDigests = item.FixtureDigests, requiredObservedFields = item.RequiredObservedFields,
                applicability = item.Applicability
            }),
            localArtifacts = localArtifacts.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => new
            {
                identity = item.Key, bytes = Convert.ToBase64String(item.Value.Span),
                sha256 = Convert.ToHexStringLower(SHA256.HashData(item.Value.Span))
            })
        }, JsonOptions));
        return new ConsumerCaseManifest(ManifestIdentity, AdmittedConsumerEnvelopeAdmissionCore.ManifestVersion,
            bytes, DomainDigest(AdmittedConsumerEnvelopeAdmissionCore.ManifestVersion + "\0", bytes));
    }

    public static ConsumerCaseManifest ReadManifest(ReadOnlyMemory<byte> bytes)
    {
        using var parsed = JsonDocument.Parse(bytes);
        // Preserve supplied bytes. System admission owns schema/canonicality/coverage validation.
        return new ConsumerCaseManifest(parsed.RootElement.GetProperty("identity").GetString(),
            parsed.RootElement.GetProperty("version").GetString(), bytes.Span,
            DomainDigest(AdmittedConsumerEnvelopeAdmissionCore.ManifestVersion + "\0", bytes.Span));
    }

    public static ConsumerEnvelopeCandidate CreateCandidate(AdmittedCorpusSnapshot corpus,
        ProducerResultEnvelope producer, ConsumerCaseManifest manifest,
        IReadOnlyList<CompositionConsumerObservation> observations, IReadOnlyList<string> missing,
        DateTimeOffset startedUtc, DateTimeOffset endedUtc, string correlationId, string sourceRevision)
    {
        if (string.IsNullOrWhiteSpace(sourceRevision))
            throw new ArgumentException("An attributable consumer source revision is required.", nameof(sourceRevision));
        using var producerJson = JsonDocument.Parse(producer.CanonicalBytes);
        var candidate = new
        {
            envelopeKind = "consumer", version = AdmittedConsumerEnvelopeAdmissionCore.EnvelopeVersion,
            consumerIdentity = ConsumerIdentity, runner = new { identity = "ghostagram.composition.projection.runner", version = "1.0" },
            sourceRevision,
            environment = new
            {
                targetFramework = "net10.0", osFamily = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux",
                architecture = global::System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()
            },
            startedUtc, endedUtc, correlationId,
            producerAnchors = new
            {
                corpusIdentity = corpus.CorpusIdentity, manifestDigest = Convert.ToHexStringLower(corpus.ManifestDigest.Span),
                producerEnvelopeDigest = producer.EnvelopeDigest, profileId = producer.Candidate.Profile.Value,
                contractVersion = producer.Candidate.ContractVersion.Value, compilerVersion = producer.Candidate.CompilerVersion.Value,
                artifacts = producerJson.RootElement.GetProperty("artifacts").Clone()
            },
            consumerCaseManifest = new { identity = manifest.Identity, version = manifest.Version, digest = manifest.Digest },
            observations = observations.Select(item => new
            {
                caseId = item.CaseId, operationIdentity = item.OperationIdentity, fixtureDigests = item.FixtureDigests,
                outcome = item.Outcome.ToString(), category = item.Category,
                diagnostics = item.Diagnostics.Select(diagnostic => new { code = diagnostic.Code, detail = diagnostic.Detail, publicPath = diagnostic.PublicPath }),
                observedFields = item.ObservedFields
            }),
            partitions = new
            {
                passed = observations.Where(item => item.Passed && !item.Unsupported).Select(item => item.CaseId),
                failed = observations.Where(item => !item.Passed && !item.Unsupported).Select(item => item.CaseId),
                missing,
                unsupported = observations.Where(item => item.Unsupported).Select(item => item.CaseId)
            }
        };
        return new ConsumerEnvelopeCandidate(Canonical(JsonSerializer.SerializeToElement(candidate, JsonOptions)));
    }

    public static byte[] SerializeProjection(CompositionProjectionResult result) =>
        CompositionProjectionJson.SerializeToUtf8Bytes(result);

    public static byte[] Canonical(JsonElement value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) Write(writer, value);
        return stream.ToArray();
    }

    private static void Write(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in value.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
            { writer.WritePropertyName(property.Name); Write(writer, property.Value); }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        { writer.WriteStartArray(); foreach (var item in value.EnumerateArray()) Write(writer, item); writer.WriteEndArray(); }
        else value.WriteTo(writer);
    }

    private static string DomainDigest(string domain, ReadOnlySpan<byte> bytes)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        digest.AppendData(Encoding.UTF8.GetBytes(domain)); digest.AppendData(bytes);
        return Convert.ToHexStringLower(digest.GetHashAndReset());
    }
}
