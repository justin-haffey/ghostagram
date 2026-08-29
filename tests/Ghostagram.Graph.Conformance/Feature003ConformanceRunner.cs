using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Ghostworx.System.SemanticLinks.Conformance;

namespace Ghostagram.Graph.Conformance;

public sealed record Feature003RunnerOptions(
    string CorpusPath,
    string ProfilePath,
    string ResultPath,
    string ParticipantVersion)
{
    public static Feature003RunnerOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Count; index += 2)
        {
            if (index + 1 >= args.Count || !args[index].StartsWith("--", StringComparison.Ordinal) ||
                args[index] is not ("--feature" or "--corpus" or "--profile" or "--result" or "--participant-version") ||
                !values.TryAdd(args[index], args[index + 1]))
                throw new ArgumentException("FEATURE-003 arguments must be unique --name value pairs.");
        }

        if (!string.Equals(Required(values, "--feature"), "feature-003", StringComparison.Ordinal))
            throw new ArgumentException("The FEATURE-003 runner requires '--feature feature-003'.");
        return new(
            Required(values, "--corpus"),
            Required(values, "--profile"),
            Required(values, "--result"),
            Required(values, "--participant-version"));
    }

    private static string Required(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Required argument '{key}' is missing.");
}

public static class Feature003ConformanceRunner
{
    public const string Participant = SemanticLinkConformanceParticipants.Ghostagram;
    public const string RunnerVersion = "3.0.0";

    public static int Run(Feature003RunnerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        try
        {
            var schemaDirectory = ResolveSchemaDirectory(options.CorpusPath);
            var corpus = SemanticLinkConformanceCorpus.Load(
                options.CorpusPath,
                options.ProfilePath,
                schemaDirectory);
            VerifyFrozenFiles(corpus, options.ProfilePath, schemaDirectory);

            var adapter = new Feature003CaseAdapter(corpus.Manifest);
            var started = DateTimeOffset.UtcNow;
            var stopwatch = Stopwatch.StartNew();
            var cases = new List<SemanticLinkConformanceCaseResult>(corpus.Manifest.Record.Fixtures.Count);
            foreach (var fixture in corpus.Manifest.Record.Fixtures)
                cases.Add(adapter.Execute(fixture, corpus.ReadVectors(fixture)));
            stopwatch.Stop();

            var record = new SemanticLinkConformanceResultRecord
            {
                ManifestDigest = corpus.Manifest.Record.ManifestDigest,
                ProfileDigest = corpus.Manifest.Record.ProfileDigest,
                CorpusId = corpus.Manifest.Record.CorpusId,
                CorpusVersion = corpus.Manifest.Record.CorpusVersion,
                ParticipantId = Participant,
                ParticipantVersion = options.ParticipantVersion,
                RuntimeIdentifier = RuntimeInformation.RuntimeIdentifier,
                OsPlatform = Environment.OSVersion.Platform.ToString(),
                ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
                SourceRevision = "None",
                StartedAtUtc = started.UtcDateTime.ToString(
                    "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture),
                DurationMilliseconds = stopwatch.ElapsedMilliseconds,
                Cases = cases,
                Summary = SemanticLinkConformanceSummary.FromCases(cases)
            };
            var result = SemanticLinkConformanceWire.WithResultDigest(new SemanticLinkConformanceResultDocument
            {
                ContractVersion = corpus.Manifest.ContractVersion,
                Profile = corpus.Manifest.Profile,
                Record = record
            });
            var canonical = SemanticLinkConformanceWire.SerializeCanonical(result);
            _ = SemanticLinkConformanceWire.ReadResult(canonical, corpus.Manifest);
            if (!canonical.AsSpan().SequenceEqual(SemanticLinkConformanceWire.SerializeCanonical(result)))
                throw new InvalidDataException("Repeated result serialization was not deterministic.");
            SemanticLinkConformanceResultWriter.WriteAtomically(options.ResultPath, result);

            Console.WriteLine(
                $"Ghostagram Semantic Link conformance: digest={record.ManifestDigest}; " +
                $"{record.Summary.Passed} passed, {record.Summary.Failed} failed, " +
                $"{record.Summary.Unsupported} unsupported, {record.Summary.Skipped} skipped; " +
                $"result={Path.GetFileName(options.ResultPath)}");
            return record.Summary.Passed == Feature003CaseAdapter.AssignedOperations.Count &&
                   record.Summary.Failed == 0 && record.Summary.Skipped == 0
                ? 0
                : 1;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Console.Error.WriteLine("Ghostagram Semantic Link conformance rejected invalid input, execution, or result data.");
            return 2;
        }
    }

    private static string ResolveSchemaDirectory(string corpusPath) => Path.GetFullPath(Path.Combine(
        corpusPath, "..", "..", "Schemas"));

    private static void VerifyFrozenFiles(
        SemanticLinkConformanceCorpus corpus,
        string profilePath,
        string schemaDirectory)
    {
        if (!string.Equals(corpus.Manifest.Record.ManifestDigest, Feature003CaseAdapter.ManifestDigest,
                StringComparison.Ordinal))
            throw new InvalidDataException("The Semantic Link manifest digest is not the reviewed Ghostagram digest.");
        VerifySha(Path.Combine(corpus.RootPath, "manifest.json"), Feature003CaseAdapter.ManifestSha256, "manifest");
        VerifySha(profilePath, Feature003CaseAdapter.ProfileSha256, "profile");
        VerifySha(Path.Combine(schemaDirectory, "semantic-link-conformance-fixture-v1.schema.json"),
            Feature003CaseAdapter.FixtureSchemaSha256, "fixture schema");
        VerifySha(Path.Combine(schemaDirectory, "semantic-link-document-v1.schema.json"),
            Feature003CaseAdapter.DocumentSchemaSha256, "document schema");
        VerifySha(Path.Combine(schemaDirectory, "semantic-link-result-envelope-v1.schema.json"),
            Feature003CaseAdapter.ResultSchemaSha256, "result schema");
    }

    private static void VerifySha(string path, string expected, string label)
    {
        var actual = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.GetFullPath(path))));
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidDataException($"The frozen Semantic Link {label} bytes changed.");
    }
}
