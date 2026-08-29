using System.Text;
using Ghostworx.System.SemanticLinks.Conformance;

namespace Ghostagram.Graph.Conformance.Tests;

public static class Feature003Tests
{
    public static int Run(IReadOnlyList<string> args)
    {
        Assert(string.Equals(RequiredOption(args, "--feature"), "feature-003", StringComparison.Ordinal),
            "The FEATURE-003 tests require '--feature feature-003'.");
        var corpusPath = RequiredOption(args, "--corpus");
        var profilePath = RequiredOption(args, "--profile");
        var schemaDirectory = Path.GetFullPath(Path.Combine(corpusPath, "..", "..", "Schemas"));
        var corpus = SemanticLinkConformanceCorpus.Load(corpusPath, profilePath, schemaDirectory);
        Assert(corpus.Manifest.Record.ManifestDigest == Feature003CaseAdapter.ManifestDigest,
            "The test corpus digest differs from the reviewed Ghostagram allowlist.");
        Assert(corpus.Manifest.Record.Fixtures.Count == 13,
            "The frozen Semantic Link corpus must contain 13 cases.");
        Assert(Feature003CaseAdapter.AssignedOperations.Count == 3,
            "The Ghostagram Semantic Link allowlist must contain exactly three cases.");
        AssertThrows<ArgumentException>(() => Feature003RunnerOptions.Parse(
            ["--feature", "feature-003", "--corpus", corpusPath, "--profile", profilePath,
             "--result", "result.json", "--participant-version", "tests",
             "--participant", SemanticLinkConformanceParticipants.Producer]),
            "The runner must reject participant identity overrides.");

        var projectionFixture = corpus.Manifest.Record.Fixtures.Single(item => item.CaseId == "projection-freshness");
        var projection = Feature003BridgeHarness.VerifyProjectionFreshness(corpus.ReadVectors(projectionFixture));

        var adapter = new Feature003CaseAdapter(corpus.Manifest);
        var direct = corpus.Manifest.Record.Fixtures.Select(fixture =>
            adapter.Execute(fixture, corpus.ReadVectors(fixture))).ToArray();
        Assert(direct.Count(item => item.Status == SemanticLinkConformanceCaseStatus.Pass) == 3 &&
               direct.Count(item => item.Status == SemanticLinkConformanceCaseStatus.Unsupported) == 10 &&
               direct.All(item => item.Status is SemanticLinkConformanceCaseStatus.Pass or
                   SemanticLinkConformanceCaseStatus.Unsupported),
            "Direct adapter execution must produce three passes and ten explicit unsupported cases: " +
            string.Join(", ", direct.Select(item => $"{item.CaseId}={item.Status}/{item.FailureCode}")));
        Assert(direct.Where(item => item.Status == SemanticLinkConformanceCaseStatus.Pass)
                .All(item => item.ExecutedVectorIds.Count > 0),
            "Every assigned pass must report the vector executed through the Bridge seam.");

        Assert(projection.ProjectionInvocations == 3 && projection.DeltaInvocations == 1 &&
               projection.CanonicalStateUnchanged && projection.PresentationStateSeparate &&
               projection.ProtectedDataExcluded,
            "Full, delta, invalidation, and rebuild projection checks did not all pass.");
        var securityFixture = corpus.Manifest.Record.Fixtures.Single(item => item.CaseId == "security-boundary");
        var security = Feature003BridgeHarness.VerifySecurityBoundary(corpus.ReadVectors(securityFixture));
        Assert(security.ProjectionInvocations == 1 && security.CanonicalStateUnchanged &&
               security.PresentationStateSeparate && security.ProtectedDataExcluded,
            "The real Bridge security observation did not preserve the public boundary.");
        var envelopeFixture = corpus.Manifest.Record.Fixtures.Single(item => item.CaseId == "envelope-integrity");
        var envelopeObservation = Feature003BridgeHarness.VerifyEnvelopeIntegrity(corpus.ReadVectors(envelopeFixture));
        Assert(envelopeObservation.ProjectionInvocations == 1 && envelopeObservation.CanonicalStateUnchanged &&
               envelopeObservation.PresentationStateSeparate && envelopeObservation.ProtectedDataExcluded,
            "The fixed-participant envelope observation did not traverse the real Bridge output.");

        var testRoot = Path.Combine(Path.GetTempPath(), "ghostagram-semantic-link-conformance-tests",
            Guid.NewGuid().ToString("N"));
        var firstResultPath = Path.Combine(testRoot, "first.json");
        var secondResultPath = Path.Combine(testRoot, "second.json");
        try
        {
            Assert(Feature003ConformanceRunner.Run(new(
                       corpusPath, profilePath, firstResultPath, "tests")) == 0,
                "The first valid Ghostagram Semantic Link run failed.");
            Assert(Feature003ConformanceRunner.Run(new(
                       corpusPath, profilePath, secondResultPath, "tests")) == 0,
                "The repeated valid Ghostagram Semantic Link run failed.");
            var first = SemanticLinkConformanceWire.ReadResult(
                File.ReadAllBytes(firstResultPath), corpus.Manifest);
            var second = SemanticLinkConformanceWire.ReadResult(
                File.ReadAllBytes(secondResultPath), corpus.Manifest);
            Assert(first.Record.ParticipantId == SemanticLinkConformanceParticipants.Ghostagram &&
                   first.Record.Summary.Total == 13 && first.Record.Summary.Passed == 3 &&
                   first.Record.Summary.Unsupported == 10 && first.Record.Summary.Failed == 0 &&
                   first.Record.Summary.Skipped == 0,
                "The persisted envelope has incorrect attribution or totals.");
            Assert(EquivalentCases(first, second),
                "Repeated runs changed case status, outcome, vector, or fixture identity.");

            var serialized = Encoding.UTF8.GetString(File.ReadAllBytes(firstResultPath));
            string[] forbidden =
            [
                "layout", "bounds", "viewport", "selection", "waypoint", "presentationRevision",
                "credential", "secret", "privateLocator", "physicalEndpoint", "runtimeBinding",
                "authorizationGrant", "payload", "providerDetail", "stackTrace"
            ];
            Assert(forbidden.All(marker => !serialized.Contains(marker, StringComparison.OrdinalIgnoreCase)),
                "Presentation, protected, or runtime semantic data entered the public result envelope.");

            var preserved = File.ReadAllBytes(firstResultPath);
            Assert(Feature003ConformanceRunner.Run(new(
                       Path.Combine(testRoot, "missing-corpus"), profilePath, firstResultPath, "tests")) == 2,
                "An invalid corpus path must fail closed.");
            Assert(File.ReadAllBytes(firstResultPath).SequenceEqual(preserved),
                "A failed run replaced the prior valid result.");

            var alteredProject = Path.Combine(testRoot, "altered-project");
            var alteredCorpus = Path.Combine(alteredProject, "Corpus", "V1");
            var alteredSchemas = Path.Combine(alteredProject, "Schemas");
            CopyDirectory(corpus.RootPath, alteredCorpus);
            CopyDirectory(schemaDirectory, alteredSchemas);
            File.AppendAllText(Path.Combine(alteredCorpus, "manifest.json"), Environment.NewLine);
            Assert(Feature003ConformanceRunner.Run(new(
                       alteredCorpus, profilePath, firstResultPath, "tests")) == 2,
                "A byte-altered manifest must fail the frozen-manifest gate.");
            Assert(File.ReadAllBytes(firstResultPath).SequenceEqual(preserved),
                "A rejected manifest replaced the prior valid result.");
        }
        finally
        {
            if (Directory.Exists(testRoot)) Directory.Delete(testRoot, recursive: true);
        }

        Console.WriteLine("Ghostagram FEATURE-003 conformance tests passed (13 cases: 3 executed, 10 unsupported).");
        return 0;
    }

    private static bool EquivalentCases(
        SemanticLinkConformanceResultDocument left,
        SemanticLinkConformanceResultDocument right) =>
        left.Record.Cases.Zip(right.Record.Cases).All(pair =>
            pair.First.CaseId == pair.Second.CaseId &&
            pair.First.FixtureDigest == pair.Second.FixtureDigest &&
            pair.First.Status == pair.Second.Status &&
            pair.First.PublicOutcome == pair.Second.PublicOutcome &&
            pair.First.ProtectedOutcome == pair.Second.ProtectedOutcome &&
            pair.First.FailureCode == pair.Second.FailureCode &&
            pair.First.SkipReason == pair.Second.SkipReason &&
            pair.First.ExecutedVectorIds.SequenceEqual(pair.Second.ExecutedVectorIds, StringComparer.Ordinal));

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
