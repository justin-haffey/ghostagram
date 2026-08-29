using Ghostworx.System.SemanticLinks.Conformance;

namespace Ghostagram.Graph.Conformance;

public sealed class Feature003CaseAdapter
{
    public const string ManifestDigest = "8da93fb0f50a7c201635b9cefc21e0de684882bf4bc64512b5773f0f4891916a";
    public const string ManifestSha256 = "afd4c03c0ed6d33be242a773712707f5e5b6c340745142d01412a4f57ad291a1";
    public const string ProfileSha256 = "2a90298f331d0735d5f36275b96e3d137d0809a50023e580654214e8aff9e5a9";
    public const string FixtureSchemaSha256 = "1a416eeb9641f8ac962af1acc76f9d1c7277a38f039f0e206ef19d2baa1b0c83";
    public const string DocumentSchemaSha256 = "4afb914440f035bc7cc5251b108b5d3d7724e44aeacf48532a451e796814497a";
    public const string ResultSchemaSha256 = "bb1716ee5e1f802efb29bc3019277cecf2cdaa6921d5a15075415a81c1c36bf7";

    public static readonly IReadOnlyDictionary<string, string> AssignedOperations =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["projection-freshness"] = "projection.freshness",
            ["security-boundary"] = "security.boundary",
            ["envelope-integrity"] = "envelope.integrity"
        };

    public Feature003CaseAdapter(SemanticLinkConformanceManifestDocument manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (!string.Equals(manifest.Record.ManifestDigest, ManifestDigest, StringComparison.Ordinal))
            throw new InvalidDataException("The Semantic Link corpus digest is not the reviewed Ghostagram FEATURE-003 digest.");
        var assigned = manifest.Record.Fixtures
            .Where(item => item.RequiredParticipants.Contains(
                SemanticLinkConformanceParticipants.Ghostagram, StringComparer.Ordinal))
            .ToDictionary(item => item.CaseId, item => item.Operation, StringComparer.Ordinal);
        if (assigned.Count != AssignedOperations.Count || AssignedOperations.Any(expected =>
                !assigned.TryGetValue(expected.Key, out var operation) ||
                !string.Equals(operation, expected.Value, StringComparison.Ordinal)))
            throw new InvalidDataException("The Ghostagram Semantic Link partition differs from the accepted Design allowlist.");
    }

    public SemanticLinkConformanceCaseResult Execute(
        SemanticLinkConformanceFixture fixture,
        IReadOnlyList<SemanticLinkConformanceVector> vectors)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(vectors);
        if (!fixture.RequiredParticipants.Contains(
                SemanticLinkConformanceParticipants.Ghostagram, StringComparer.Ordinal))
            return Unsupported(fixture);
        if (!AssignedOperations.TryGetValue(fixture.CaseId, out var operation) ||
            !string.Equals(operation, fixture.Operation, StringComparison.Ordinal))
            return Failed(fixture, "design-drift");

        try
        {
            var observation = Observe(fixture.CaseId, vectors);
            var passed = string.Equals(observation.PublicOutcome, fixture.ExpectedOutcome, StringComparison.Ordinal) &&
                         observation.ProjectionInvocations > 0 && observation.CanonicalStateUnchanged &&
                         observation.PresentationStateSeparate && observation.ProtectedDataExcluded &&
                         observation.ExecutedVectorIds.SequenceEqual(
                             vectors.Select(item => item.VectorId), StringComparer.Ordinal);
            return new SemanticLinkConformanceCaseResult
            {
                CaseId = fixture.CaseId,
                FixtureDigest = fixture.FixtureDigest,
                Status = passed ? SemanticLinkConformanceCaseStatus.Pass : SemanticLinkConformanceCaseStatus.Fail,
                PublicOutcome = observation.PublicOutcome,
                FailureCode = passed ? null : "observation-mismatch",
                ExecutedVectorIds = observation.ExecutedVectorIds
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Failed(fixture, "participant-execution-failed");
        }
    }

    private static Feature003BridgeObservation Observe(
        string caseId,
        IReadOnlyList<SemanticLinkConformanceVector> vectors) => caseId switch
    {
        "projection-freshness" => Feature003BridgeHarness.VerifyProjectionFreshness(vectors),
        "security-boundary" => Feature003BridgeHarness.VerifySecurityBoundary(vectors),
        "envelope-integrity" => Feature003BridgeHarness.VerifyEnvelopeIntegrity(vectors),
        _ => throw new InvalidOperationException("An assigned Semantic Link case has no Ghostagram handler.")
    };

    private static SemanticLinkConformanceCaseResult Unsupported(SemanticLinkConformanceFixture fixture) => new()
    {
        CaseId = fixture.CaseId,
        FixtureDigest = fixture.FixtureDigest,
        Status = SemanticLinkConformanceCaseStatus.Unsupported,
        PublicOutcome = "unsupported",
        SkipReason = "participant-not-required"
    };

    private static SemanticLinkConformanceCaseResult Failed(
        SemanticLinkConformanceFixture fixture,
        string code) => new()
    {
        CaseId = fixture.CaseId,
        FixtureDigest = fixture.FixtureDigest,
        Status = SemanticLinkConformanceCaseStatus.Fail,
        PublicOutcome = "failure",
        FailureCode = code
    };
}
