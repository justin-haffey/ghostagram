using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ghostagram.Contracts;

namespace Ghostagram.Server.Layout;

public sealed class DiagramLayoutService(
    DiagramCommandService commands,
    IDiagramLayoutStrategyResolver strategies)
{
    public IReadOnlyList<object> Capabilities() => strategies.Capabilities();

    public async Task<DiagramLayoutResult> ExecuteAsync(DiagramLayoutRequest request, CancellationToken cancellationToken)
    {
        Validate(request);
        var strategy = strategies.Resolve(request.Algorithm);
        var options = request.Options ?? new DiagramLayoutOptions();

        if (request.DryRun)
        {
            var snapshot = await commands.GetSnapshotAsync(request.DocumentId, cancellationToken);
            if (snapshot is null)
                return Rejected(request, strategy, "DOCUMENT_NOT_FOUND", "The document does not exist.", 0);
            if (snapshot.Revision != request.BaseRevision)
                return Rejected(request, strategy, "REVISION_CONFLICT", $"Expected revision {snapshot.Revision}, received {request.BaseRevision}.", snapshot.Revision);

            var preview = strategy.Compute(snapshot, options, request.Seed, cancellationToken);
            return new(true, "LAYOUT_PREVIEW", "Deterministic layout preview generated.", snapshot.Revision, snapshot.Revision + 1,
                strategy.Name, strategy.Version, request.Seed, true, preview.Operations, preview.Metrics);
        }

        LayoutComputation? computation = null;
        var metadata = JsonSerializer.SerializeToElement(new
        {
            kind = "layout",
            algorithm = strategy.Name,
            algorithmVersion = strategy.Version,
            request.Seed,
            options
        });
        var result = await commands.SubmitGeneratedAsync(
            request.DocumentId,
            request.ActorId,
            request.CommandId,
            request.BaseRevision,
            IntentHash(request, strategy, options),
            snapshot => (computation = strategy.Compute(snapshot, options, request.Seed, cancellationToken)).Operations,
            metadata,
            cancellationToken);

        var operations = computation?.Operations ?? result.Change?.Operations ?? ImmutableArray<GhostagramOperation>.Empty;
        return new(
            result.Accepted,
            result.Code,
            result.Message,
            request.BaseRevision,
            result.Accepted ? result.Revision : request.BaseRevision,
            strategy.Name,
            strategy.Version,
            request.Seed,
            false,
            operations,
            computation?.Metrics,
            result);
    }

    private static string IntentHash(DiagramLayoutRequest request, IDiagramLayoutStrategy strategy, DiagramLayoutOptions options)
    {
        var json = JsonSerializer.Serialize(new
        {
            request.DocumentId,
            request.ActorId,
            request.CommandId,
            request.BaseRevision,
            algorithm = strategy.Name,
            algorithmVersion = strategy.Version,
            request.Seed,
            options
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static DiagramLayoutResult Rejected(
        DiagramLayoutRequest request,
        IDiagramLayoutStrategy strategy,
        string code,
        string message,
        long revision)
        => new(false, code, message, revision, revision, strategy.Name, strategy.Version, request.Seed, request.DryRun,
            ImmutableArray<GhostagramOperation>.Empty);

    private static void Validate(DiagramLayoutRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        DocumentIdRules.Require(request.DocumentId);
        if (string.IsNullOrWhiteSpace(request.ActorId) || string.IsNullOrWhiteSpace(request.CommandId))
            throw new ArgumentException("actorId and commandId are required.");
        if (request.BaseRevision < 0) throw new ArgumentOutOfRangeException(nameof(request.BaseRevision));
        if (string.IsNullOrWhiteSpace(request.Algorithm)) throw new ArgumentException("algorithm is required.");
    }
}
