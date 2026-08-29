using System.Text;
using System.Security.Cryptography;
using Ghostworx.System.Variable;
using Ghostworx.System.Variable.Conformance;
using Ghostworx.System.Variable.Serialization;

namespace Ghostagram.Graph.Conformance.Tests;

public static class Feature002Tests
{
    public static int Run(IReadOnlyList<string> args)
    {
        var fixturesPath = RequiredOption(args, "--fixtures");
        Assert(string.Equals(RequiredOption(args, "--feature"), "feature-002", StringComparison.Ordinal),
            "The FEATURE-002 tests require '--feature feature-002'.");
        var corpus = VariableConformanceCorpus.Load(fixturesPath);
        Assert(corpus.Manifest.Record.ManifestDigest == Feature002CaseAdapter.ManifestDigest,
            "The test corpus digest differs from the reviewed Ghostagram allowlist.");
        Assert(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(
                   Path.Combine(corpus.RootPath, "manifest.json")))).Equals(
                   Feature002CaseAdapter.ManifestSha256, StringComparison.OrdinalIgnoreCase),
            "The test manifest bytes differ from the reviewed Ghostagram manifest.");
        Assert(corpus.Manifest.Record.Fixtures.Count == 12,
            "The frozen Variable corpus must contain 12 cases.");
        Assert(Feature002CaseAdapter.AssignedOperations.Count == 8,
            "The Ghostagram Variable allowlist must contain exactly eight cases.");
        AssertThrows<ArgumentException>(() => Feature002RunnerOptions.Parse(
            ["--feature", "feature-002", "--fixtures", fixturesPath, "--result", "result.json",
             "--participant-version", "tests", "--participant", "ghostworx-system-variable-producer"]),
            "The runner must reject participant identity overrides.");

        var adapter = new Feature002CaseAdapter(corpus.Manifest);
        var direct = corpus.Manifest.Record.Fixtures.Select(fixture =>
        {
            using var document = corpus.ReadFixture(fixture);
            return adapter.Execute(fixture, VariableConformanceWire.SerializeCanonical(document.RootElement));
        }).ToArray();
        Assert(direct.Count(item => item.Status == VariableConformanceCaseStatus.Pass) == 8 &&
               direct.Count(item => item.Status == VariableConformanceCaseStatus.Unsupported) == 4 &&
               direct.All(item => item.Status is VariableConformanceCaseStatus.Pass or VariableConformanceCaseStatus.Unsupported),
            "Direct adapter execution must produce eight passes and four explicit unsupported cases.");

        var first = corpus.GetFixture("definition-reference-contract-matrix");
        using var firstDocument = corpus.ReadFixture(first);
        var firstBytes = VariableConformanceWire.SerializeCanonical(firstDocument.RootElement);
        var reference = new VariableJsonSerializer().DeserializeReference(firstBytes);
        var definition = Feature002BridgeHarness.VerifyDefinitionProjection(reference);
        Assert(definition.ProjectionInvocations == 1 && definition.ResolutionInvocations == 0 &&
               definition.ProtectedDataExcluded && definition.PresentationDataExcluded,
            "A public Variable Definition must project without resolution or leakage.");
        var provenance = Feature002BridgeHarness.VerifyPublicProvenanceProjection(reference);
        Assert(provenance.ProjectionInvocations == 1 && provenance.ResolutionInvocations == 0 &&
               provenance.ProtectedDataExcluded && provenance.PresentationDataExcluded,
            "Public provenance must project without a resolved payload, protected data, or resolution.");
        var skewFixture = corpus.GetFixture("consumer-skew-redaction-matrix");
        using var skewDocument = corpus.ReadFixture(skewFixture);
        var skewReference = new VariableJsonSerializer().DeserializeReference(
            VariableConformanceWire.SerializeCanonical(skewDocument.RootElement));
        var compatiblePinned = VariableReference.Pinned(
            new VariableRevision(skewReference.Identity, 1),
            skewReference.ExpectedType,
            skewReference.ScopeConstraints,
            VariableContractVersion.Current,
            VariableCompatibilityProfile.ConformanceSmall.CanonicalId);
        var pinned = Feature002BridgeHarness.ObserveReference(compatiblePinned, ReadOnlySpan<byte>.Empty);
        Assert(pinned.ProjectionInvocations == 1 && pinned.ResolutionInvocations == 0,
            "A compatible pinned Variable Reference must preserve its exact revision without resolution.");
        var invalidation = Feature002BridgeHarness.VerifyInvalidationProjection(reference);
        Assert(invalidation.DeltaObserved && invalidation.ProjectionInvocations == 2 &&
               invalidation.ResolutionInvocations == 0 && invalidation.ProtectedDataExcluded &&
               invalidation.PresentationDataExcluded,
            "Public invalidation must traverse the Bridge delta seam without resolution or leakage.");

        var testRoot = Path.Combine(Path.GetTempPath(), "ghostagram-variable-conformance-tests", Guid.NewGuid().ToString("N"));
        var firstResultPath = Path.Combine(testRoot, "first.json");
        var secondResultPath = Path.Combine(testRoot, "second.json");
        try
        {
            Assert(Feature002ConformanceRunner.Run(new(fixturesPath, firstResultPath, "tests")) == 0,
                "The first valid Ghostagram Variable run failed.");
            Assert(Feature002ConformanceRunner.Run(new(fixturesPath, secondResultPath, "tests")) == 0,
                "The repeated valid Ghostagram Variable run failed.");
            var firstResult = VariableConformanceWire.ReadResult(File.ReadAllBytes(firstResultPath), corpus.Manifest);
            var secondResult = VariableConformanceWire.ReadResult(File.ReadAllBytes(secondResultPath), corpus.Manifest);
            Assert(firstResult.Record.ParticipantId == VariableConformanceParticipants.Ghostagram &&
                   firstResult.Record.Summary.Total == 12 && firstResult.Record.Summary.Passed == 8 &&
                   firstResult.Record.Summary.Unsupported == 4 && firstResult.Record.Summary.Failed == 0 &&
                   firstResult.Record.Summary.Skipped == 0,
                "The persisted envelope has incorrect attribution or totals.");
            Assert(EquivalentCases(firstResult, secondResult),
                "Repeated runs changed case status, outcome, diagnostic, or fixture identity.");

            var serialized = Encoding.UTF8.GetString(File.ReadAllBytes(firstResultPath));
            string[] forbidden =
            [
                "credential", "secret", "bindingIdentity", "providerCapability", "privateLocator",
                "resolvedPayload", "protectedTenant", "stackTrace"
            ];
            Assert(forbidden.All(marker => !serialized.Contains(marker, StringComparison.OrdinalIgnoreCase)),
                "Protected Variable data entered the public result envelope.");

            var preserved = File.ReadAllBytes(firstResultPath);
            Assert(Feature002ConformanceRunner.Run(new(
                       Path.Combine(testRoot, "missing-corpus"), firstResultPath, "tests")) == 2,
                "An invalid corpus path must fail closed.");
            Assert(File.ReadAllBytes(firstResultPath).SequenceEqual(preserved),
                "A failed run replaced the prior valid result.");

            var alteredCorpus = Path.Combine(testRoot, "altered-corpus");
            CopyDirectory(corpus.RootPath, alteredCorpus);
            File.AppendAllText(Path.Combine(alteredCorpus, "manifest.json"), Environment.NewLine);
            Assert(Feature002ConformanceRunner.Run(new(
                       alteredCorpus, firstResultPath, "tests")) == 2,
                "A byte-altered manifest must fail the reviewed-manifest gate.");
            Assert(File.ReadAllBytes(firstResultPath).SequenceEqual(preserved),
                "A rejected manifest replaced the prior valid result.");
        }
        finally
        {
            if (Directory.Exists(testRoot)) Directory.Delete(testRoot, recursive: true);
        }

        Console.WriteLine("Ghostagram FEATURE-002 conformance tests passed (12 cases: 8 executed, 4 unsupported).");
        return 0;
    }

    private static bool EquivalentCases(
        VariableConformanceResultDocument left,
        VariableConformanceResultDocument right) =>
        left.Record.Cases.Zip(right.Record.Cases).All(pair =>
            pair.First.CaseId == pair.Second.CaseId &&
            pair.First.FixtureDigest == pair.Second.FixtureDigest &&
            pair.First.Status == pair.Second.Status &&
            pair.First.ProtectedOutcome == pair.Second.ProtectedOutcome &&
            pair.First.PublicOutcome == pair.Second.PublicOutcome &&
            pair.First.FailureCode == pair.Second.FailureCode &&
            pair.First.SkipReason == pair.Second.SkipReason);

    private static string RequiredOption(IReadOnlyList<string> args, string option)
    {
        var index = args.IndexOf(option);
        if (index < 0 || index + 1 >= args.Count || string.IsNullOrWhiteSpace(args[index + 1]))
            throw new ArgumentException($"Required option '{option}' is missing.");
        return args[index + 1];
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void AssertThrows<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)));
    }

    private static int IndexOf(this IReadOnlyList<string> values, string value)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (string.Equals(values[index], value, StringComparison.Ordinal)) return index;
        }
        return -1;
    }
}
