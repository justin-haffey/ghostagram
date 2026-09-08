using System.Security.Cryptography;
using System.Text.Json;
using Ghostagram.Bridge.DeclarativeCompositionProjection;
using Ghostagram.Bridge.Tests.DeclarativeComposition;
using Ghostworx.System.Composition;
using Ghostworx.System.Composition.Compiler;
using Ghostworx.System.Composition.Conformance.Support;
using Ghostworx.System.Composition.Exchange;

namespace Ghostagram.Graph.Conformance.Composition;

public sealed record ProducerProjectionInventory(IReadOnlyList<CompositionExecutableCase> Cases, byte[] ApplicabilityBytes);
public sealed class CompositionCaseUnavailableException(string code, string publicDetail) : InvalidOperationException(publicDetail)
{
    public string Code { get; } = code;
    public string PublicDetail { get; } = publicDetail;
}

/// <summary>Prepares exact owner operations before manifest freeze, then executes only actual consumer Project calls.</summary>
public static class ProducerProjectionCases
{
    private const string CatalogIdentity = "realized-fixtures-v1.json";
    public const string ApplicabilityIdentity = "ghostagram.projection.producer-applicability/1";
    private static readonly string[] OwnerFields =
    ["fixtureAnchor", "ownerOperations", "ownerStatus", "ownerReplayReceiptSha256", "ownerReplayReady", "ownerObservedFieldsPending"];

    public static ProducerProjectionInventory Inventory(CompositionProducerInputs inputs)
    {
        if (inputs.Corpus.Fixtures.Count == 0)
            throw new InvalidDataException("The admitted corpus has no realized request catalog; recipes cannot be replayed as consumer cases.");
        var anchors = inputs.Corpus.Fixtures.Values.Select(item => item.Anchor).OrderBy(item => item.CaseIdentity, StringComparer.Ordinal).ToArray();
        if (!anchors.Select(item => item.CaseIdentity).Order(StringComparer.Ordinal)
            .SequenceEqual(inputs.Corpus.RetainedCaseIds.Order(StringComparer.Ordinal)))
            throw new InvalidDataException("Realized fixture anchors do not cover every admitted producer case exactly once.");
        using var catalog = JsonDocument.Parse(inputs.Catalog.Require(CatalogIdentity).Bytes);
        var paths = catalog.RootElement.GetProperty("fixtures").EnumerateArray().ToDictionary(
            item => item.GetProperty("fixtureIdentity").GetString()!, item =>
                (item.TryGetProperty("candidatePath", out var path) ? path : item.GetProperty("vectorDescriptorPath")).GetString()!, StringComparer.Ordinal);
        var required = new List<PreparedCase>();
        var applicability = new List<object>();
        foreach (var anchor in anchors)
        {
            var operation = CompositionFixtureOperations.Require(anchor.DeclaredOperationIdentity);
            if (operation.ResultSchemaIdentity != anchor.ResultSchemaIdentity || anchor.Classification == CompositionFixtureClassification.Unclassified)
                throw new InvalidDataException("The fixture lacks its exact owner operation/schema/classification.");
            if (anchor.Classification == CompositionFixtureClassification.Tooling && operation.Classification == "tooling")
            {
                applicability.Add(new { anchor, applicable = false, classification = "tooling",
                    reason = "The owner registry classifies this admission/runner operation as tooling, without a semantic projection source." });
                continue;
            }
            if (anchor.Classification != CompositionFixtureClassification.SemanticOutput || operation.Classification != "semantic-output")
                throw new InvalidDataException("The admitted fixture classification differs from the owner operation registry.");
            CompositionOwnerReplayResult? replay = null;
            CompositionCaseUnavailableException? unavailable = null;
            try { replay = CompositionOwnerReplay.Prepare(inputs, anchor); }
            catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
            {
                unavailable = exception as CompositionCaseUnavailableException ?? new("GRAM-COMP-OWNER-PREPARATION-FAILED",
                    "The exact owner preparation did not produce a complete result; this semantic case remains required.");
            }
            // Readiness includes exact outcome, original producer and every checkpoint/prerequisite.
            // Pending owner fields or a mismatch can never become a semantic-success exclusion.
            var excluded = replay?.SemanticNonProjectable == true;
            JsonElement? receipt = replay is null ? null : JsonSerializer.SerializeToElement(replay.Receipt, CompositionConsumerProtocol.JsonOptions);
            var receiptDigest = receipt is null ? null : Convert.ToHexStringLower(SHA256.HashData(CompositionConsumerProtocol.Canonical(receipt.Value)));
            applicability.Add(new { anchor, applicable = !excluded,
                classification = excluded ? "semantic-success-without-public-view" : "required-semantic-result",
                reason = excluded ? "The exact matching successful owner result has no accepted IR PublicView; no consumer Project was executed or counted as passed."
                    : "Actual public-view or rejected owner results require Project; incomplete preparation remains required.",
                ownerReplayReceipt = receipt, ownerReplayReceiptSha256 = receiptDigest,
                unavailableCode = unavailable?.Code, unavailableDetail = unavailable?.PublicDetail });
            if (!excluded) required.Add(new(anchor, paths[anchor.FixtureIdentity], replay, receiptDigest, unavailable));
        }
        if (required.Count == 0) throw new InvalidDataException("No required semantic projection case remains in the concrete inventory.");
        var applicabilityBytes = CompositionConsumerProtocol.Canonical(JsonSerializer.SerializeToElement(new
        { corpusIdentity = inputs.Corpus.CorpusIdentity, producerEnvelopeDigest = inputs.Producer.EnvelopeDigest, applicability }, CompositionConsumerProtocol.JsonOptions));
        var applicabilityDigest = Convert.ToHexStringLower(SHA256.HashData(applicabilityBytes));
        var cases = required.Select(prepared => new CompositionExecutableCase(
            new("GRAM:" + prepared.Anchor.CaseIdentity + ":PROJECT", "DeclarativeCompositionProjection.Project",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [CatalogIdentity] = inputs.Corpus.ArtifactDigests[CatalogIdentity],
                    [prepared.ArtifactPath] = inputs.Corpus.ArtifactDigests[prepared.ArtifactPath],
                    [ApplicabilityIdentity] = applicabilityDigest
                }, CompositionConsumerSession.ProjectionFields.Concat(OwnerFields).ToArray()),
            () => Execute(prepared))).ToArray();
        return new(Array.AsReadOnly(cases), applicabilityBytes);
    }

    private static CompositionScenarioResult Execute(PreparedCase prepared)
    {
        if (prepared.Unavailable is { } unavailable) throw unavailable;
        var replay = prepared.Replay ?? throw new InvalidOperationException("Prepared owner result is missing.");
        var profiles = new CompositionProfileAdmission();
        var contexts = new CompositionOperationContextAdmission();
        var profile = ProjectionFixtures.Profile();
        var context = ProjectionFixtures.Context(correlationId: "ghostagram-project:" + prepared.Anchor.RealizedRawByteDigest);
        var admittedProfile = profiles.Admit(profile);
        if (!admittedProfile.IsAccepted) throw new InvalidOperationException("Projection invocation profile admission failed.");
        var admittedContext = contexts.Admit(context, admittedProfile.Profile!);
        if (!admittedContext.IsAccepted) throw new InvalidOperationException("Projection invocation context admission failed.");
        var sidecar = CompositionPresentationSidecar.Admit(0, [], [], [], [], null, admittedProfile.Profile!, admittedContext.Context!).Sidecar
            ?? throw new InvalidOperationException("Empty presentation sidecar failed admission.");
        var projector = new DeclarativeCompositionProjector(profiles, contexts);
        // Every arm consumes the actual returned typed result. No alternate result wrapper
        // or owner operation is constructed or executed within this consumer invocation.
        var result = replay.ProjectionSource switch
        {
            CompositionCompilationResult source => projector.Project(source, profile, context, sidecar),
            CompositionVerificationResult source => projector.Project(source, profile, context, sidecar),
            CompositionDecodeResult source => projector.Project(source, profile, context, sidecar),
            CompositionEncodeResult source => projector.Project(source, profile, context, sidecar),
            CompositionMigrationResult source => projector.Project(source, profile, context, sidecar),
            CompatibilityInspectionResult source => projector.Project(source, profile, context, sidecar),
            CompatibilitySelectionResult source => projector.Project(source, profile, context, sidecar),
            DefinitionReferenceResult source => projector.Project(source, profile, context, sidecar),
            _ => throw new CompositionCaseUnavailableException("GRAM-COMP-SOURCE-VARIANT-UNAVAILABLE",
                "The actual owner result type has no implemented consumer Project input.")
        };
        var projected = replay.OwnerStatus == CompositionResultStatus.Accepted
            ? replay.HasAcceptedPublicView && result is CompositionProjectionResult.Fresh &&
                result.SourceAnchor?.ContentDigest == replay.Ir!.ContentIdentity.DigestHex
            : result is not CompositionProjectionResult.Fresh && result.SourceAnchor is null &&
                result.SourceDiagnostics.SequenceEqual(replay.OwnerDiagnostics.Diagnostics);
        return new(result, replay.ReadyForProjection && projected,
            "The exact fully checked owner replay is projected with its admitted public-view identity or unchanged rejection diagnostics.",
            replay.ReadyForProjection ? null : replay.OwnerObservedFieldsPending
                ? "Owner observed-field contract is pending; this actual Project observation is not a passing conformance case."
                : "Owner expectation, producer, prerequisite, verification or scheduled-checkpoint checks did not all match.",
            null, new Dictionary<string, object?>
            {
                ["fixtureAnchor"] = replay.Anchor,
                ["ownerOperations"] = replay.Receipt.Operations.Select(item => item.OperationIdentity).ToArray(),
                ["ownerStatus"] = replay.OwnerStatus.ToString(),
                ["ownerReplayReceiptSha256"] = prepared.ReceiptDigest,
                ["ownerReplayReady"] = replay.ReadyForProjection,
                ["ownerObservedFieldsPending"] = replay.OwnerObservedFieldsPending
            });
    }

    private sealed record PreparedCase(CompositionFixtureAnchor Anchor, string ArtifactPath,
        CompositionOwnerReplayResult? Replay, string? ReceiptDigest, CompositionCaseUnavailableException? Unavailable);
}
