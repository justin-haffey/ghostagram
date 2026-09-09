using System.Security.Cryptography;
using System.Text.Json;
using Ghostworx.System.Composition.Conformance.Runner;

namespace Ghostagram.Graph.Conformance.Composition;

/// <summary>Explicit composition tooling entry point; local previews never claim producer-linked conformance.</summary>
public static class CompositionRunner
{
    public static int Run(IReadOnlyList<string> args)
    {
        try
        {
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            var local = false;
            var materialize = false;
            for (var index = 0; index < args.Count; index++)
            {
                var name = args[index];
                if (name == "--local-projections")
                {
                    if (local) throw new ArgumentException("Duplicate local mode.");
                    local = true;
                    continue;
                }
                if (name == "--materialize-manifest")
                {
                    if (materialize) throw new ArgumentException("Duplicate manifest mode.");
                    materialize = true;
                    continue;
                }
                if (name is not ("--profile" or "--output" or "--corpus" or "--producer-envelope" or
                    "--consumer-manifest" or "--manifest-digest" or "--source-root" or "--source-revision" or
                    "--e0-source" or "--e0-corpus") ||
                    ++index >= args.Count || !options.TryAdd(name, args[index]))
                    throw new ArgumentException("Unknown, duplicate or missing composition option.");
            }
            if (Require("--profile") != "composition") throw new ArgumentException("The composition profile is required.");
            if (local)
            {
                if (materialize || options.Keys.Any(key => key is not ("--profile" or "--output")))
                    throw new ArgumentException("Local projection mode cannot be combined with producer-linked conformance options.");
                return RunLocal(Require("--output"));
            }
            var hasE0Source = options.ContainsKey("--e0-source");
            var hasE0Corpus = options.ContainsKey("--e0-corpus");
            if (hasE0Source != hasE0Corpus)
                throw new ArgumentException("--e0-source and --e0-corpus must be supplied together.");
            using var e0Source = hasE0Source
                ? AcquiredDiagnosticResultSource.Acquire(Require("--e0-source"), Require("--e0-corpus"))
                : null;
            var inputs = CompositionConsumerSession.LoadProducer(Require("--corpus"), Require("--producer-envelope"), e0Source);
            e0Source?.RequireAvailable();
            var producer = ProducerProjectionCases.Inventory(inputs);
            e0Source?.RequireAvailable();
            var bundle = SourceFixtureBundle(Require("--source-root"));
            const string bundleIdentity = "ghostagram.projection.source-fixture-bundle/1";
            var digests = new Dictionary<string, string>
            { [bundleIdentity] = Convert.ToHexStringLower(SHA256.HashData(bundle)) };
            var cases = producer.Cases.Concat(CompositionConsumerSession.LocalCases(digests)).ToArray();
            KeyValuePair<string, ReadOnlyMemory<byte>>[] artifacts =
            [new(bundleIdentity, bundle), new(ProducerProjectionCases.ApplicabilityIdentity, producer.ApplicabilityBytes)];
            var manifestPath = Require("--consumer-manifest");
            if (materialize)
            {
                if (options.ContainsKey("--manifest-digest") || options.ContainsKey("--output"))
                    throw new ArgumentException("Manifest materialization is separate from execution and its externally frozen digest.");
                CompositionConsumerSession.Materialize(cases, artifacts, manifestPath);
                e0Source?.RequireAvailable();
                return 0;
            }
            var result = CompositionConsumerSession.Run(inputs, cases, artifacts, manifestPath, Require("--manifest-digest"),
                Require("--output"), Require("--source-revision"));
            e0Source?.RequireAvailable();
            return result;

            string Require(string key) => options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value : throw new ArgumentException("Missing " + key + ".");
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or JsonException or InvalidOperationException)
        {
            Console.Error.WriteLine("Composition tooling failed: " + exception.Message);
            return 2;
        }
    }

    private static byte[] SourceFixtureBundle(string sourceRoot)
    {
        var root = Path.GetFullPath(sourceRoot);
        var directories = new[]
        {
            "src/Ghostagram.Bridge/DeclarativeCompositionProjection",
            "tests/Ghostagram.Bridge.Tests/DeclarativeComposition",
            "tests/Ghostagram.Graph.Conformance/Composition"
        };
        var files = directories.SelectMany(directory => Directory.GetFiles(Path.Combine(root, directory), "*.cs"))
            .Order(StringComparer.Ordinal).Select(path =>
            {
                var bytes = CompositionConsumerSession.ReadBounded(path, 1_048_576);
                return new { path = Path.GetRelativePath(root, path).Replace('\\', '/'),
                    bytes = Convert.ToBase64String(bytes), sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) };
            }).ToArray();
        if (files.Length == 0) throw new InvalidDataException("Fixture implementation source is missing.");
        return CompositionConsumerProtocol.Canonical(JsonSerializer.SerializeToElement(new
        { identity = "ghostagram.projection.source-fixture-bundle/1", files }, CompositionConsumerProtocol.JsonOptions));
    }

    private static int RunLocal(string output)
    {
        var destination = Path.GetFullPath(output);
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
            throw new IOException("Local projection output must be a new or empty directory.");
        Directory.CreateDirectory(destination);
        var observations = new List<object>();
        var failed = 0;
        foreach (var test in CompositionConsumerSession.LocalCases(new Dictionary<string, string>()))
        {
            try
            {
                var actual = test.Execute();
                var bytes = actual.Result is null ? null : CompositionConsumerProtocol.SerializeProjection(actual.Result);
                var fileName = bytes is null ? null : test.Manifest.CaseId + ".json";
                if (bytes is not null)
                    using (var file = new FileStream(Path.Combine(destination, fileName!), FileMode.CreateNew)) file.Write(bytes);
                observations.Add(new { caseId = test.Manifest.CaseId, operationIdentity = test.Manifest.OperationIdentity,
                    actual.Passed, actual.Assertion, actual.LocalObservation, actual.AdmissionOutcome,
                    status = actual.Result?.Status.ToString(), fileName,
                    sha256 = bytes is null ? null : Convert.ToHexStringLower(SHA256.HashData(bytes)) });
                if (!actual.Passed) failed++;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
            {
                failed++;
                observations.Add(new { caseId = test.Manifest.CaseId, operationIdentity = test.Manifest.OperationIdentity,
                    passed = false, assertion = "The local case produced a complete actual observation.",
                    diagnosticCode = "GRAM-COMP-LOCAL-CASE-FAILED", fileName = (string?)null, sha256 = (string?)null });
            }
        }
        using (var index = new FileStream(Path.Combine(destination, "local-projection-index.json"), FileMode.CreateNew))
            JsonSerializer.Serialize(index, new { kind = "local-projection-checks", producerLinkedConformance = false, observations }, CompositionConsumerProtocol.JsonOptions);
        Console.WriteLine($"Local projection checks: {observations.Count - failed}/{observations.Count} passed; producer-linked conformance is separate.");
        return failed == 0 ? 0 : 1;
    }
}
