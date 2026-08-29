using System.Runtime.InteropServices;
using System.Text.Json;
using Ghostworx.System.Graph.Conformance.Support;

namespace Ghostagram.Graph.Conformance;

public sealed record RunnerOptions(string CorpusPath, string SchemaPath, string OutputPath, string ParticipantVersion)
{
    public static RunnerOptions Parse(IReadOnlyList<string> args)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "--corpus", "--schema", "--output", "--participant-version"
        };
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Count; index += 2)
        {
            if (index + 1 >= args.Count || !args[index].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("Arguments must be supplied as --name value pairs.");
            if (!allowed.Contains(args[index]))
                throw new ArgumentException($"Unknown argument '{args[index]}'.");
            if (!values.TryAdd(args[index], args[index + 1]))
                throw new ArgumentException($"Argument '{args[index]}' was supplied more than once.");
        }

        return new(
            Required(values, "--corpus"),
            Required(values, "--schema"),
            Required(values, "--output"),
            Required(values, "--participant-version"));
    }

    private static string Required(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Required argument '{key}' is missing.");
}

public static class GhostagramConformanceRunner
{
    public const string Participant = "ghostagram";
    public const string RunnerVersion = "1.0.0";

    public static int Run(RunnerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        try
        {
            var startedAt = DateTimeOffset.UtcNow;
            var corpus = ConformanceCorpus.Load(options.CorpusPath);
            VerifyProfileDigest(corpus);
            var adapter = new Feature001CaseAdapter(corpus);
            var results = corpus.Manifest.Cases.Select(adapter.Execute).ToArray();
            var endedAt = DateTimeOffset.UtcNow;
            var envelope = new ConformanceResultEnvelope
            {
                CorpusId = corpus.Manifest.CorpusId,
                CorpusVersion = corpus.Manifest.CorpusVersion,
                CorpusDigest = ConformanceDigest.ComputeCanonicalJson(corpus.Manifest),
                ProfileId = corpus.Manifest.ProfileId,
                ProfileDigest = corpus.Manifest.ProfileDigest,
                ContractVersion = corpus.Manifest.ContractVersion,
                SchemaVersion = corpus.Manifest.SchemaVersion,
                VocabularyVersions = corpus.Manifest.VocabularyVersions,
                Participant = Participant,
                ParticipantVersion = options.ParticipantVersion,
                Environment = new ConformanceEnvironment
                {
                    Framework = RuntimeInformation.FrameworkDescription,
                    OperatingSystem = RuntimeInformation.OSDescription,
                    Architecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()
                },
                StartedAt = startedAt,
                EndedAt = endedAt,
                RunnerVersion = RunnerVersion,
                Cases = results,
                Totals = ConformanceResultTotals.FromCases(results),
                MissingCases = results.Where(item => item.Status == ConformanceCaseStatus.Missing)
                    .Select(item => item.CaseId).Order(StringComparer.Ordinal).ToArray(),
                UnsupportedCases = results.Where(item => item.Status == ConformanceCaseStatus.Unsupported)
                    .Select(item => item.CaseId).Order(StringComparer.Ordinal).ToArray()
            };

            var serialized = ConformanceResultWriter.Serialize(corpus.Manifest, envelope);
            ConformanceResultSchemaValidator.Validate(serialized, Path.GetFullPath(options.SchemaPath));
            if (!StringComparer.Ordinal.Equals(serialized, ConformanceResultWriter.Serialize(corpus.Manifest, envelope)))
                throw new InvalidDataException("Serializing the Ghostagram envelope twice did not produce identical bytes.");

            WriteValidated(options.OutputPath, serialized);
            var assignedIncomplete = results.Any(result =>
                Feature001CaseAdapter.AssignedCaseIds.Contains(result.CaseId) && result.Status != ConformanceCaseStatus.Passed);
            Console.WriteLine(
                $"Ghostagram conformance: {envelope.Totals.Passed} passed, {envelope.Totals.Failed} failed, " +
                $"{envelope.Totals.Unsupported} unsupported, {envelope.Totals.Missing} missing; result={Path.GetFullPath(options.OutputPath)}");
            return !assignedIncomplete && envelope.Totals.Failed == 0 && envelope.Totals.Missing == 0 ? 0 : 1;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Console.Error.WriteLine($"Ghostagram conformance runner failed: {PublicMessage(exception.Message)}");
            return 2;
        }
    }

    private static void VerifyProfileDigest(ConformanceCorpus corpus)
    {
        var profile = corpus.Manifest.Fixtures.Single(item => item.Id == "profile");
        var digest = ConformanceDigest.ComputeCanonicalJson(File.ReadAllText(corpus.ResolveFixturePath(profile)));
        if (!string.Equals(digest, corpus.Manifest.ProfileDigest, StringComparison.Ordinal))
            throw new InvalidDataException("The manifest profile digest does not match canonical profile JSON.");
    }

    private static void WriteValidated(string outputPath, string serialized)
    {
        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath) ?? throw new InvalidDataException("The result path has no directory.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, serialized + "\n", new System.Text.UTF8Encoding(false));
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static string PublicMessage(string value) => value.Length <= 512 ? value : value[..512];
}

public sealed record CaseObservation(
    string Seam,
    string Outcome,
    string Category,
    IReadOnlyList<string> DiagnosticCodes,
    IReadOnlyDictionary<string, string?> Fields,
    string Detail);
