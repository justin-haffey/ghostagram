using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Ghostworx.System.Variable.Conformance;

namespace Ghostagram.Graph.Conformance;

public sealed record Feature002RunnerOptions(string FixturesPath, string ResultPath, string ParticipantVersion)
{
    public static Feature002RunnerOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Count; index += 2)
        {
            if (index + 1 >= args.Count || !args[index].StartsWith("--", StringComparison.Ordinal) ||
                args[index] is not ("--feature" or "--fixtures" or "--result" or "--participant-version") ||
                !values.TryAdd(args[index], args[index + 1]))
                throw new ArgumentException("FEATURE-002 arguments must be unique --name value pairs.");
        }

        if (!string.Equals(Required(values, "--feature"), "feature-002", StringComparison.Ordinal))
            throw new ArgumentException("The FEATURE-002 runner requires '--feature feature-002'.");
        return new(
            Required(values, "--fixtures"),
            Required(values, "--result"),
            Required(values, "--participant-version"));
    }

    private static string Required(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Required argument '{key}' is missing.");
}

public static class Feature002ConformanceRunner
{
    public const string Participant = VariableConformanceParticipants.Ghostagram;
    public const string RunnerVersion = "2.0.0";

    public static int Run(Feature002RunnerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        try
        {
            var corpus = VariableConformanceCorpus.Load(options.FixturesPath);
            var manifestSha256 = Convert.ToHexString(SHA256.HashData(
                    File.ReadAllBytes(Path.Combine(corpus.RootPath, "manifest.json"))))
                .ToLowerInvariant();
            if (!string.Equals(
                    manifestSha256,
                    Feature002CaseAdapter.ManifestSha256,
                    StringComparison.Ordinal))
                throw new InvalidDataException(
                    "The Variable manifest bytes differ from the reviewed Ghostagram FEATURE-002 manifest.");
            var schemaDirectory = Path.GetFullPath(Path.Combine(
                corpus.RootPath, "..", "..", "..", "Schemas"));
            VariableConformanceWire.ValidateSchemaDirectory(schemaDirectory);
            var adapter = new Feature002CaseAdapter(corpus.Manifest);
            var started = DateTimeOffset.UtcNow;
            var stopwatch = Stopwatch.StartNew();
            var cases = new List<VariableConformanceCaseResult>(corpus.Manifest.Record.Fixtures.Count);
            foreach (var fixture in corpus.Manifest.Record.Fixtures)
            {
                using var document = corpus.ReadFixture(fixture);
                var fixtureBytes = VariableConformanceWire.SerializeCanonical(document.RootElement);
                cases.Add(adapter.Execute(fixture, fixtureBytes));
            }
            stopwatch.Stop();

            var record = new VariableConformanceResult
            {
                ManifestDigest = corpus.Manifest.Record.ManifestDigest,
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
                Summary = VariableConformanceSummary.FromCases(cases)
            };
            var result = VariableConformanceWire.WithResultDigest(new VariableConformanceResultDocument
            {
                ContractVersion = corpus.Manifest.ContractVersion,
                Profile = corpus.Manifest.Profile,
                Record = record
            });
            VariableConformanceWire.ThrowIfInvalid(result, corpus.Manifest);
            VariableConformanceResultWriter.WriteAtomically(options.ResultPath, result);

            Console.WriteLine(
                $"Ghostagram Variable conformance: digest={result.Record.ManifestDigest}; " +
                $"{record.Summary.Passed} passed, {record.Summary.Failed} failed, " +
                $"{record.Summary.Unsupported} unsupported, {record.Summary.Skipped} skipped.");
            return record.Summary.Passed == Feature002CaseAdapter.AssignedOperations.Count &&
                   record.Summary.Failed == 0 && record.Summary.Skipped == 0
                ? 0
                : 1;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Console.Error.WriteLine("Ghostagram Variable conformance rejected invalid input or result data.");
            return 2;
        }
    }
}
