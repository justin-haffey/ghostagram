using Ghostworx.System.Variable;
using Ghostworx.System.Variable.Conformance;
using Ghostworx.System.Variable.Serialization;

namespace Ghostagram.Graph.Conformance;

public sealed class Feature002CaseAdapter
{
    public const string ManifestDigest = "32510da1219131fa7f1b634065b8d5b01df04d77fa9ca77a5d6e168ece1dadbe";
    public const string ManifestSha256 = "86b1eea07aaea0dc5c3cade857562b18382d096461aef5ebcd208f29ae4864d6";

    public static readonly IReadOnlyDictionary<string, string> AssignedOperations =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["definition-reference-contract-matrix"] = "public-definition-reference-contract-matrix",
            ["serialization-migration-extension-matrix"] = "public-serialization-migration-extension-matrix",
            ["public-security-leakage-matrix"] = "public-security-redaction-leakage-matrix",
            ["conformance-wire-digest-bound-matrix"] = "conformance-schema-digest-bound-matrix",
            ["composition-reference-only"] = "composition-reference-only",
            ["composition-binding-exclusion"] = "composition-binding-exclusion",
            ["composition-no-resolution"] = "composition-no-resolution",
            ["consumer-skew-redaction-matrix"] = "consumer-contract-profile-skew-redaction-matrix"
        };

    public Feature002CaseAdapter(VariableConformanceManifestDocument manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (!string.Equals(manifest.Record.ManifestDigest, ManifestDigest, StringComparison.Ordinal))
            throw new InvalidDataException("The Variable corpus digest is not the reviewed Ghostagram FEATURE-002 digest.");
        var assigned = manifest.Record.Fixtures
            .Where(item => item.RequiredParticipantKinds.Contains(
                VariableConformanceParticipants.Ghostagram, StringComparer.Ordinal))
            .ToDictionary(item => item.CaseId, item => item.Operation, StringComparer.Ordinal);
        if (assigned.Count != AssignedOperations.Count || AssignedOperations.Any(expected =>
                !assigned.TryGetValue(expected.Key, out var operation) ||
                !string.Equals(operation, expected.Value, StringComparison.Ordinal)))
            throw new InvalidDataException("The Ghostagram Variable case partition differs from the accepted Design allowlist.");
    }

    public VariableConformanceCaseResult Execute(
        VariableConformanceFixture fixture,
        ReadOnlyMemory<byte> fixtureBytes)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        if (!fixture.RequiredParticipantKinds.Contains(
                VariableConformanceParticipants.Ghostagram, StringComparer.Ordinal))
            return Unsupported(fixture);
        if (!AssignedOperations.TryGetValue(fixture.CaseId, out var operation) ||
            !string.Equals(operation, fixture.Operation, StringComparison.Ordinal))
            return Failed(fixture, "design-drift");

        try
        {
            using var verified = VariableConformanceWire.ReadFixture(fixtureBytes.Span, fixture.FixtureDigest);
            var reference = new VariableJsonSerializer().DeserializeReference(fixtureBytes.Span);
            var observation = Observe(fixture.CaseId, reference, fixtureBytes.Span);
            var passed = string.Equals(observation.PublicOutcome, fixture.ExpectedPublicOutcome, StringComparison.Ordinal) &&
                         observation.ResolutionInvocations == 0 && observation.ProtectedDataExcluded &&
                         observation.PresentationDataExcluded;
            return new VariableConformanceCaseResult
            {
                CaseId = fixture.CaseId,
                FixtureDigest = fixture.FixtureDigest,
                Status = passed ? VariableConformanceCaseStatus.Pass : VariableConformanceCaseStatus.Fail,
                PublicOutcome = observation.PublicOutcome,
                FailureCode = passed ? null : "observation-mismatch"
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Failed(fixture, "participant-execution-failed");
        }
    }

    private static Feature002BridgeObservation Observe(
        string caseId,
        VariableReference reference,
        ReadOnlySpan<byte> fixtureBytes) => caseId switch
    {
        "definition-reference-contract-matrix" =>
            Feature002BridgeHarness.ObserveReference(reference, fixtureBytes),
        "serialization-migration-extension-matrix" =>
            Feature002BridgeHarness.ObserveReference(reference, fixtureBytes, requireCanonicalRoundTrip: true),
        "public-security-leakage-matrix" =>
            Feature002BridgeHarness.ObservePublicSecurity(reference, fixtureBytes),
        "conformance-wire-digest-bound-matrix" =>
            Feature002BridgeHarness.ObserveReference(reference, fixtureBytes),
        "composition-reference-only" =>
            Feature002BridgeHarness.ObserveReference(reference, fixtureBytes),
        "composition-binding-exclusion" =>
            Feature002BridgeHarness.ObserveReference(reference, fixtureBytes, requireBindingExclusion: true),
        "composition-no-resolution" =>
            Feature002BridgeHarness.ObserveReference(reference, fixtureBytes),
        "consumer-skew-redaction-matrix" =>
            Feature002BridgeHarness.ObserveCompatibilitySkew(reference),
        _ => throw new InvalidOperationException("An assigned Variable case has no Ghostagram handler.")
    };

    private static VariableConformanceCaseResult Unsupported(VariableConformanceFixture fixture) => new()
    {
        CaseId = fixture.CaseId,
        FixtureDigest = fixture.FixtureDigest,
        Status = VariableConformanceCaseStatus.Unsupported,
        ProtectedOutcome = "unsupported",
        PublicOutcome = "unsupported",
        SkipReason = "participant-not-required"
    };

    private static VariableConformanceCaseResult Failed(VariableConformanceFixture fixture, string code) => new()
    {
        CaseId = fixture.CaseId,
        FixtureDigest = fixture.FixtureDigest,
        Status = VariableConformanceCaseStatus.Fail,
        ProtectedOutcome = "internal-provider-failure",
        PublicOutcome = VariableOutcomeCode.ResolutionUnavailable.Value,
        FailureCode = code
    };
}
