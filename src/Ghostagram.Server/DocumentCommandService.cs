using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ghostagram.Contracts;

namespace Ghostagram.Server;

public interface IDocumentCommandQueue
{
    Task<T> EnqueueAsync<T>(string documentId, Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken);
}

/// <summary>One command writer per document in this process. Multi-instance ownership is a later deployment concern.</summary>
public sealed class DocumentCommandQueue : IDocumentCommandQueue
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);

    public async Task<T> EnqueueAsync<T>(string documentId, Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        var gate = _gates.GetOrAdd(DocumentIdRules.Require(documentId), static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try { return await work(cancellationToken); }
        finally { gate.Release(); }
    }
}

public interface IDocumentStore
{
    Task<StoredDocument?> LoadAsync(string documentId, CancellationToken cancellationToken);
    Task SaveAsync(StoredDocument document, CancellationToken cancellationToken);
}

public sealed record StoredCommand(string PayloadHash, long Revision, DateTimeOffset CommittedUtc);

public sealed class StoredDocument
{
    public required string DocumentId { get; init; }
    public long Revision { get; set; }
    public required JsonElement Model { get; set; }
    public List<DiagramChange> Changes { get; init; } = [];
    public Dictionary<string, StoredCommand> CommandLedger { get; init; } = new(StringComparer.Ordinal);
}

public sealed class DiagramCommandService(
    IDocumentStore store,
    IDocumentCommandQueue queue,
    IDocumentEventPublisher publisher)
{
    public async Task<DiagramSnapshot?> GetSnapshotAsync(string documentId, CancellationToken cancellationToken)
    {
        var document = await store.LoadAsync(DocumentIdRules.Require(documentId), cancellationToken);
        return document is null ? null : Snapshot(document);
    }

    public async Task<IReadOnlyList<DiagramChange>> GetChangesAsync(string documentId, long afterRevision, CancellationToken cancellationToken)
    {
        if (afterRevision < 0) throw new ArgumentOutOfRangeException(nameof(afterRevision));
        var document = await store.LoadAsync(DocumentIdRules.Require(documentId), cancellationToken);
        return document?.Changes.Where(change => change.Revision > afterRevision).ToArray() ?? [];
    }

    public Task<DiagramCommandResult> SubmitAsync(DiagramCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommand(command);
        return queue.EnqueueAsync(command.DocumentId, token => SubmitSerializedAsync(command, token), cancellationToken);
    }

    public Task<DiagramCommandResult> SubmitGeneratedAsync(
        string documentId,
        string actorId,
        string commandId,
        long baseRevision,
        string intentHash,
        Func<DiagramSnapshot, ImmutableArray<GhostagramOperation>> generate,
        JsonElement? metadata,
        CancellationToken cancellationToken)
    {
        DocumentIdRules.Require(documentId);
        if (string.IsNullOrWhiteSpace(actorId) || string.IsNullOrWhiteSpace(commandId))
            throw new ArgumentException("actorId and commandId are required.");
        if (baseRevision < 0) throw new ArgumentOutOfRangeException(nameof(baseRevision));
        if (string.IsNullOrWhiteSpace(intentHash)) throw new ArgumentException("An intent hash is required.", nameof(intentHash));
        ArgumentNullException.ThrowIfNull(generate);
        return queue.EnqueueAsync(
            documentId,
            token => SubmitGeneratedSerializedAsync(documentId, actorId, commandId, baseRevision, intentHash, generate, metadata, token),
            cancellationToken);
    }

    private async Task<DiagramCommandResult> SubmitSerializedAsync(DiagramCommand command, CancellationToken cancellationToken)
    {
        var payloadHash = PayloadHash(command);
        var ledgerKey = $"{command.ActorId}:{command.CommandId}";
        var document = await store.LoadAsync(command.DocumentId, cancellationToken) ?? Create(command.DocumentId);

        if (Replay(document, ledgerKey, payloadHash) is { } replay) return replay;

        if (command.BaseRevision != document.Revision)
            return new(false, "REVISION_CONFLICT", $"Expected revision {document.Revision}, received {command.BaseRevision}.", document.Revision, Snapshot(document));

        JsonElement nextModel;
        try { nextModel = GhostagramDocumentReducer.Apply(document.Model, command.Operations); }
        catch (DiagramCommandException error) { return new(false, error.Code, error.Message, document.Revision, Snapshot(document)); }

        return await CommitAsync(document, command.ActorId, command.CommandId, payloadHash, command.Operations, command.Metadata, nextModel, cancellationToken);
    }

    private async Task<DiagramCommandResult> SubmitGeneratedSerializedAsync(
        string documentId,
        string actorId,
        string commandId,
        long baseRevision,
        string intentHash,
        Func<DiagramSnapshot, ImmutableArray<GhostagramOperation>> generate,
        JsonElement? metadata,
        CancellationToken cancellationToken)
    {
        var ledgerKey = $"{actorId}:{commandId}";
        var document = await store.LoadAsync(documentId, cancellationToken) ?? Create(documentId);
        if (Replay(document, ledgerKey, intentHash) is { } replay) return replay;
        if (baseRevision != document.Revision)
            return new(false, "REVISION_CONFLICT", $"Expected revision {document.Revision}, received {baseRevision}.", document.Revision, Snapshot(document));

        ImmutableArray<GhostagramOperation> operations;
        JsonElement nextModel;
        try
        {
            operations = generate(Snapshot(document));
            if (operations.IsDefaultOrEmpty)
            {
                document.CommandLedger[ledgerKey] = new StoredCommand(intentHash, document.Revision, DateTimeOffset.UtcNow);
                await store.SaveAsync(document, cancellationToken);
                return new(true, "NO_CHANGES", "The generated command produced no changes.", document.Revision, Snapshot(document));
            }
            nextModel = GhostagramDocumentReducer.Apply(document.Model, operations);
        }
        catch (DiagramCommandException error)
        {
            return new(false, error.Code, error.Message, document.Revision, Snapshot(document));
        }

        return await CommitAsync(document, actorId, commandId, intentHash, operations, metadata, nextModel, cancellationToken);
    }

    private async Task<DiagramCommandResult> CommitAsync(
        StoredDocument document,
        string actorId,
        string commandId,
        string payloadHash,
        ImmutableArray<GhostagramOperation> operations,
        JsonElement? metadata,
        JsonElement nextModel,
        CancellationToken cancellationToken)
    {
        var change = new DiagramChange(document.DocumentId, document.Revision + 1, actorId, commandId, operations, DateTimeOffset.UtcNow, metadata);
        document.Model = nextModel;
        document.Revision = change.Revision;
        document.Changes.Add(change);
        document.CommandLedger[$"{actorId}:{commandId}"] = new StoredCommand(payloadHash, change.Revision, change.CommittedUtc);

        // The store write is the commit point. SignalR is notified only afterwards.
        await store.SaveAsync(document, cancellationToken);
        await publisher.PublishCommittedAsync(change, cancellationToken);
        return new(true, "COMMITTED", "Command committed.", change.Revision, Snapshot(document), change);
    }

    private static DiagramCommandResult? Replay(StoredDocument document, string ledgerKey, string payloadHash)
    {
        if (!document.CommandLedger.TryGetValue(ledgerKey, out var completed)) return null;
        var change = document.Changes.FirstOrDefault(item => item.Revision == completed.Revision);
        return completed.PayloadHash == payloadHash
            ? new(true, "IDEMPOTENT_REPLAY", "The command was already committed.", completed.Revision, Snapshot(document), change)
            : new(false, "IDEMPOTENCY_KEY_REUSED", "commandId cannot be reused with a different payload.", document.Revision, Snapshot(document));
    }

    private static StoredDocument Create(string documentId)
    {
        var model = JsonSerializer.SerializeToElement(new
        {
            documentId,
            nodes = Array.Empty<object>(), ports = Array.Empty<object>(), edges = Array.Empty<object>(),
            groups = Array.Empty<object>(), edgeTypes = Array.Empty<object>(), selection = Array.Empty<string>(),
            viewport = new { x = 0, y = 0, zoom = 1 }
        });
        return new StoredDocument { DocumentId = documentId, Revision = 0, Model = model };
    }

    private static DiagramSnapshot Snapshot(StoredDocument document) => new(document.DocumentId, document.Revision, document.Model.Clone());
    private static string PayloadHash(DiagramCommand command)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { command.BaseRevision, command.Operations, command.Metadata }))));

    private static void ValidateCommand(DiagramCommand command)
    {
        DocumentIdRules.Require(command.DocumentId);
        if (string.IsNullOrWhiteSpace(command.ActorId) || string.IsNullOrWhiteSpace(command.CommandId))
            throw new ArgumentException("actorId and commandId are required.");
        if (command.BaseRevision < 0) throw new ArgumentOutOfRangeException(nameof(command.BaseRevision));
        if (command.Operations.IsDefaultOrEmpty) throw new ArgumentException("At least one operation is required.", nameof(command));
    }
}

public static class DocumentIdRules
{
    public static string Require(string documentId)
    {
        if (string.IsNullOrWhiteSpace(documentId) || documentId.Length > 128 || documentId.Any(character => !(char.IsLetterOrDigit(character) || character is '-' or '_')))
            throw new ArgumentException("Document IDs must be 1-128 letters, numbers, hyphens, or underscores.", nameof(documentId));
        return documentId;
    }
}
