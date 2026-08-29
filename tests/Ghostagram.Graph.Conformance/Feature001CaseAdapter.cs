using System.Text.Json;
using Ghostworx.System.Graph.Conformance.Support;

namespace Ghostagram.Graph.Conformance;

public sealed class Feature001CaseAdapter
{
    public static readonly IReadOnlySet<string> AssignedCaseIds = new HashSet<string>(StringComparer.Ordinal)
    {
        "identity.address-roundtrip",
        "mutation.deterministic-replay",
        "mutation.expected-revision-conflict",
        "federation.origin-preserved",
        "federation.pinned-revision",
        "federation.mirror-projection-distinct",
        "consumer.contract-skew",
        "consumer.profile-skew",
        "corpus.duplicate-case-rejected",
        "result.private-field-rejected"
    };

    private readonly ConformanceCorpus _corpus;
    private readonly IReadOnlyDictionary<string, JsonElement> _fixtures;

    public Feature001CaseAdapter(ConformanceCorpus corpus)
    {
        _corpus = corpus ?? throw new ArgumentNullException(nameof(corpus));
        _fixtures = corpus.Manifest.Fixtures.ToDictionary(
            fixture => fixture.Id,
            fixture => JsonDocument.Parse(File.ReadAllText(corpus.ResolveFixturePath(fixture))).RootElement.Clone(),
            StringComparer.Ordinal);
    }

    public ConformanceCaseResult Execute(ConformanceCase testCase)
    {
        ArgumentNullException.ThrowIfNull(testCase);
        if (!AssignedCaseIds.Contains(testCase.Id)) return Unsupported(testCase);

        try
        {
            var observation = Observe(testCase.Id);
            var passed = Result(testCase, ConformanceCaseStatus.Passed, observation);
            var correct = string.Equals(observation.Outcome, testCase.ExpectedOutcome, StringComparison.Ordinal)
                && string.Equals(observation.Category, testCase.ExpectedCategory, StringComparison.Ordinal)
                && observation.DiagnosticCodes.Order(StringComparer.Ordinal)
                    .SequenceEqual(testCase.ExpectedDiagnosticCodes.Order(StringComparer.Ordinal), StringComparer.Ordinal)
                && ConformanceInvariants.ValidateCase(testCase, passed).Count == 0;
            return correct ? passed : Result(testCase, ConformanceCaseStatus.Failed, observation,
                "The actual Ghostagram observation did not match the frozen manifest contract.");
        }
        catch (Exception exception)
        {
            return new ConformanceCaseResult
            {
                CaseId = testCase.Id,
                AcceptanceCriteria = testCase.AcceptanceCriteria,
                Status = ConformanceCaseStatus.Failed,
                ObservedOutcome = "runner-failure",
                ObservedCategory = "runner",
                ObservedFields = new SortedDictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["seam"] = "ghostagram-adapter"
                },
                Detail = exception.Message.Length <= 512 ? exception.Message : exception.Message[..512]
            };
        }
    }

    private CaseObservation Observe(string caseId) => caseId switch
    {
        "identity.address-roundtrip" => GhostagramBridgeHarness.ObserveIdentity(_fixtures["identity-vocabulary"]),
        "mutation.deterministic-replay" => GhostagramBridgeHarness.ObserveDeterministicReplay(_fixtures["identity-vocabulary"]),
        "mutation.expected-revision-conflict" => GhostagramBridgeHarness.ObserveExpectedRevisionConflict(_fixtures["mutation-history"]),
        "federation.origin-preserved" => GhostagramBridgeHarness.ObserveFederationOrigin(_fixtures["identity-vocabulary"]),
        "federation.pinned-revision" => GhostagramBridgeHarness.ObservePinnedRevision(_fixtures["federation"]),
        "federation.mirror-projection-distinct" => GhostagramBridgeHarness.ObserveMirrorProjection(_fixtures["identity-vocabulary"]),
        "consumer.contract-skew" => ObserveContractSkew(),
        "consumer.profile-skew" => ObserveProfileSkew(),
        "corpus.duplicate-case-rejected" => ObserveDuplicateCase(),
        "result.private-field-rejected" => ObservePrivateField(),
        _ => throw new InvalidOperationException($"Assigned case '{caseId}' has no Ghostagram handler.")
    };

    private CaseObservation ObserveContractSkew()
    {
        var unknown = _fixtures["skew-redaction"].GetProperty("unknownContract").GetString()!;
        if (string.Equals(unknown, _corpus.Manifest.ContractVersion, StringComparison.Ordinal))
            throw new InvalidDataException("The contract-skew fixture does not contain an unknown contract.");
        return Observation("ghostagram-adapter.contract-gate", "unsupported-contract", "compatibility",
            ["unsupported-contract"], ("contractVersion", unknown));
    }

    private CaseObservation ObserveProfileSkew()
    {
        var unknown = _fixtures["skew-redaction"].GetProperty("unknownProfile").GetString()!;
        if (string.Equals(unknown, _corpus.Manifest.ProfileId, StringComparison.Ordinal))
            throw new InvalidDataException("The profile-skew fixture does not contain an unknown profile.");
        return Observation("ghostagram-adapter.profile-gate", "unsupported-profile", "compatibility",
            ["unsupported-profile"], ("profileId", unknown));
    }

    private CaseObservation ObserveDuplicateCase()
    {
        var duplicateId = _corpus.Manifest.Cases[0].Id;
        var invalid = _corpus.Manifest with { Cases = _corpus.Manifest.Cases.Concat([_corpus.Manifest.Cases[0]]).ToArray() };
        if (!ConformanceInvariants.Validate(invalid).Any(error => error.Contains("duplicate", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Shared invariants accepted a duplicate corpus case.");
        return Observation("system-support.manifest-invariants", "invalid-corpus", "corpus",
            ["duplicate-case"], ("caseId", duplicateId));
    }

    private CaseObservation ObservePrivateField()
    {
        var caseId = "result.private-field-rejected";
        var probe = new ConformanceResultEnvelope
        {
            CorpusId = _corpus.Manifest.CorpusId,
            CorpusVersion = _corpus.Manifest.CorpusVersion,
            CorpusDigest = ConformanceDigest.ComputeCanonicalJson(_corpus.Manifest),
            ProfileId = _corpus.Manifest.ProfileId,
            ProfileDigest = _corpus.Manifest.ProfileDigest,
            ContractVersion = _corpus.Manifest.ContractVersion,
            SchemaVersion = _corpus.Manifest.SchemaVersion,
            VocabularyVersions = _corpus.Manifest.VocabularyVersions,
            Participant = GhostagramConformanceRunner.Participant,
            ParticipantVersion = "probe",
            StartedAt = DateTimeOffset.UnixEpoch,
            EndedAt = DateTimeOffset.UnixEpoch,
            RunnerVersion = GhostagramConformanceRunner.RunnerVersion,
            Cases = [new ConformanceCaseResult
            {
                CaseId = caseId,
                AcceptanceCriteria = ["AC-006"],
                Status = ConformanceCaseStatus.Passed,
                ObservedOutcome = "invalid-result",
                ObservedCategory = "redaction",
                ObservedDiagnosticCodes = ["private-field"],
                ObservedFields = new SortedDictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["caseId"] = caseId,
                    ["credential"] = "must-not-escape"
                },
                ObservedPrivateFields = ["credential"]
            }],
            Totals = new ConformanceResultTotals { Total = 1, Passed = 1 }
        };
        if (!ConformanceInvariants.Validate(probe).Any(error => error.Contains("private", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Shared invariants accepted a private result field.");
        return Observation("system-support.result-invariants", "invalid-result", "redaction",
            ["private-field"], ("caseId", caseId));
    }

    private static ConformanceCaseResult Unsupported(ConformanceCase testCase) => new()
    {
        CaseId = testCase.Id,
        AcceptanceCriteria = testCase.AcceptanceCriteria,
        Status = ConformanceCaseStatus.Unsupported,
        ObservedOutcome = "unsupported-operation",
        ObservedCategory = "unsupported",
        ObservedFields = new SortedDictionary<string, string?>(StringComparer.Ordinal)
        {
            ["operation"] = testCase.Operation,
            ["supportStatus"] = ConformanceCaseStatus.Unsupported
        },
        Detail = "No FEATURE-001 operation is allocated to an existing Ghostagram Bridge seam."
    };

    private static ConformanceCaseResult Result(
        ConformanceCase testCase,
        string status,
        CaseObservation observation,
        string? detail = null) => new()
    {
        CaseId = testCase.Id,
        AcceptanceCriteria = testCase.AcceptanceCriteria,
        Status = status,
        ObservedOutcome = observation.Outcome,
        ObservedCategory = observation.Category,
        ObservedDiagnosticCodes = observation.DiagnosticCodes,
        ObservedFields = new SortedDictionary<string, string?>(
            observation.Fields.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal),
            StringComparer.Ordinal)
        {
            ["seam"] = observation.Seam
        },
        Detail = detail ?? observation.Detail
    };

    private static CaseObservation Observation(
        string seam,
        string outcome,
        string category,
        IReadOnlyList<string> codes,
        params (string Key, string? Value)[] fields) => new(
            seam,
            outcome,
            category,
            codes,
            fields.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal),
            "Observed by the Ghostagram-owned conformance adapter.");
}
