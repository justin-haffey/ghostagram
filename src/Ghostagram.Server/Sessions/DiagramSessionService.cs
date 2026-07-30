using System.Collections.Concurrent;
using System.Collections.Immutable;
using Ghostagram.Contracts;
using Ghostagram.Server.Export;
using Ghostagram.Server.Layout;

namespace Ghostagram.Server.Sessions;

/// <summary>
/// Coordinates explicit collaboration handles while keeping documents, revisions, and commits
/// authoritative in <see cref="DiagramCommandService"/>.
/// </summary>
public sealed class DiagramSessionService(
    DiagramCommandService commands,
    DiagramLayoutService layouts,
    IDiagramExporter exporter)
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);
    private readonly ConcurrentDictionary<string, LiveSession> _sessions = new(StringComparer.Ordinal);

    public async Task<DiagramSessionResult> OpenAsync(
        string documentId,
        string actorId,
        string? sessionId,
        CancellationToken cancellationToken)
    {
        RequireActor(actorId);
        var snapshot = await commands.GetSnapshotAsync(documentId, cancellationToken);
        if (snapshot is null)
            return new(false, "DIAGRAM_NOT_FOUND", $"Diagram '{documentId}' does not exist.");

        LiveSession session;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            session = new LiveSession($"gps_{Guid.NewGuid():N}", documentId);
            session.Add(actorId);
            _sessions[session.SessionId] = session;
        }
        else
        {
            if (!TryGetSession(sessionId, out session))
                return new(false, "SESSION_NOT_FOUND", $"Session '{sessionId}' does not exist or has expired.");
            if (!string.Equals(session.DocumentId, documentId, StringComparison.Ordinal))
                return new(false, "SESSION_DOCUMENT_MISMATCH", "The session belongs to a different diagram.");
            session.Add(actorId);
        }

        return new(true, "SESSION_OPEN", "Diagram session is ready.", ToContract(session, actorId, snapshot));
    }

    public async Task<DiagramSessionResult> CreateAsync(
        string documentId,
        string actorId,
        string commandId,
        IReadOnlyList<GhostagramOperation>? initialOperations,
        CancellationToken cancellationToken)
    {
        RequireActor(actorId);
        if (string.IsNullOrWhiteSpace(commandId)) throw new ArgumentException("commandId is required.", nameof(commandId));
        if (await commands.GetSnapshotAsync(documentId, cancellationToken) is not null)
            return new(false, "DIAGRAM_EXISTS", $"Diagram '{documentId}' already exists.");

        var operations = initialOperations is { Count: > 0 }
            ? initialOperations.ToImmutableArray()
            : [Operation("viewport.set", new { x = 0, y = 0, zoom = 1 })];
        var command = await commands.SubmitAsync(
            new DiagramCommand(documentId, actorId, commandId, 0, operations),
            cancellationToken);
        if (!command.Accepted)
            return new(false, command.Code, command.Message, Command: command);

        var opened = await OpenAsync(documentId, actorId, null, cancellationToken);
        return opened with { Command = command };
    }

    public async Task<DiagramReadResult> ReadAsync(
        string sessionId,
        string actorId,
        long? afterRevision,
        CancellationToken cancellationToken)
    {
        if (!TryGetParticipant(sessionId, actorId, out var session, out var code, out var message))
            return ReadRejected(sessionId, code, message);
        if (afterRevision is < 0)
            return ReadRejected(sessionId, "INVALID_REVISION", "afterRevision cannot be negative.", session.DocumentId);

        var snapshot = await commands.GetSnapshotAsync(session.DocumentId, cancellationToken);
        if (snapshot is null)
            return ReadRejected(sessionId, "DIAGRAM_NOT_FOUND", "The session diagram no longer exists.", session.DocumentId);
        var changes = afterRevision is { } revision
            ? await commands.GetChangesAsync(session.DocumentId, revision, cancellationToken)
            : [];
        session.Touch();
        return new(
            true,
            "DIAGRAM_READ",
            afterRevision is null ? "Authoritative snapshot returned." : "Authoritative snapshot and subsequent changes returned.",
            session.SessionId,
            session.DocumentId,
            snapshot.Revision,
            snapshot,
            changes,
            session.Participants());
    }

    public async Task<DiagramCommandResult> ApplyAsync(
        string sessionId,
        string actorId,
        string commandId,
        long baseRevision,
        IReadOnlyList<GhostagramOperation> operations,
        CancellationToken cancellationToken)
    {
        if (!TryGetParticipant(sessionId, actorId, out var session, out var code, out var message))
            return new(false, code, message, 0);
        var result = await commands.SubmitAsync(
            new DiagramCommand(session.DocumentId, actorId, commandId, baseRevision, operations.ToImmutableArray()),
            cancellationToken);
        session.Touch();
        return result;
    }

    public async Task<DiagramLayoutResult> LayoutAsync(
        string sessionId,
        string actorId,
        string commandId,
        long baseRevision,
        string algorithm,
        int seed,
        bool dryRun,
        DiagramLayoutOptions? options,
        CancellationToken cancellationToken)
    {
        if (!TryGetParticipant(sessionId, actorId, out var session, out var code, out var message))
            return LayoutRejected(code, message, baseRevision, algorithm, seed, dryRun);
        var result = await layouts.ExecuteAsync(
            new DiagramLayoutRequest(session.DocumentId, actorId, commandId, baseRevision, algorithm, seed, dryRun, options),
            cancellationToken);
        session.Touch();
        return result;
    }

    public async Task<DiagramExportResult> ExportSvgAsync(
        string sessionId,
        string actorId,
        CancellationToken cancellationToken)
    {
        if (!TryGetParticipant(sessionId, actorId, out var session, out var code, out var message))
            return new(false, code, message, sessionId, string.Empty, 0, "image/svg+xml", null);
        var snapshot = await commands.GetSnapshotAsync(session.DocumentId, cancellationToken);
        if (snapshot is null)
            return new(false, "DIAGRAM_NOT_FOUND", "The session diagram no longer exists.", sessionId, session.DocumentId, 0, "image/svg+xml", null);

        var artifact = exporter.Export(snapshot);
        session.Touch();
        return new(true, "SVG_EXPORTED", "SVG generated from the authoritative diagram revision.", sessionId, session.DocumentId, snapshot.Revision, artifact.MediaType, artifact.Content);
    }

    public Task<DiagramSessionResult> CloseAsync(string sessionId, string actorId)
    {
        if (!TryGetSession(sessionId, out var session))
            return Task.FromResult(new DiagramSessionResult(false, "SESSION_NOT_FOUND", $"Session '{sessionId}' does not exist or has expired."));
        if (!session.Remove(actorId))
            return Task.FromResult(new DiagramSessionResult(false, "SESSION_PARTICIPANT_REQUIRED", $"Actor '{actorId}' has not joined this session."));
        if (session.IsEmpty) _sessions.TryRemove(sessionId, out _);
        return Task.FromResult(new DiagramSessionResult(true, "SESSION_CLOSED", "The actor left the diagram session."));
    }

    private bool TryGetParticipant(
        string sessionId,
        string actorId,
        out LiveSession session,
        out string code,
        out string message)
    {
        RequireActor(actorId);
        if (!TryGetSession(sessionId, out session))
        {
            code = "SESSION_NOT_FOUND";
            message = $"Session '{sessionId}' does not exist or has expired.";
            return false;
        }
        if (!session.Contains(actorId))
        {
            code = "SESSION_PARTICIPANT_REQUIRED";
            message = $"Actor '{actorId}' must join the session with open_session before using it.";
            return false;
        }
        code = string.Empty;
        message = string.Empty;
        return true;
    }

    private bool TryGetSession(string sessionId, out LiveSession session)
    {
        session = null!;
        if (string.IsNullOrWhiteSpace(sessionId) || !_sessions.TryGetValue(sessionId, out var found)) return false;
        session = found;
        if (DateTimeOffset.UtcNow - session.LastActiveUtc <= SessionLifetime) return true;
        _sessions.TryRemove(sessionId, out _);
        session = null!;
        return false;
    }

    private static DiagramSession ToContract(LiveSession session, string actorId, DiagramSnapshot snapshot)
        => new(
            session.SessionId,
            session.DocumentId,
            actorId,
            snapshot.Revision,
            session.OpenedUtc,
            session.LastActiveUtc,
            session.Participants(),
            snapshot);

    private static DiagramReadResult ReadRejected(string sessionId, string code, string message, string documentId = "")
        => new(false, code, message, sessionId, documentId, 0, null, [], []);

    private static DiagramLayoutResult LayoutRejected(
        string code,
        string message,
        long baseRevision,
        string algorithm,
        int seed,
        bool dryRun)
        => new(false, code, message, baseRevision, baseRevision, algorithm, string.Empty, seed, dryRun, []);

    private static GhostagramOperation Operation(string type, object value)
        => new(type, System.Text.Json.JsonSerializer.SerializeToElement(value));

    private static void RequireActor(string actorId)
    {
        if (string.IsNullOrWhiteSpace(actorId)) throw new ArgumentException("actorId is required.", nameof(actorId));
    }

    private sealed class LiveSession(string sessionId, string documentId)
    {
        private readonly object _gate = new();
        private readonly HashSet<string> _participants = new(StringComparer.Ordinal);

        public string SessionId { get; } = sessionId;
        public string DocumentId { get; } = documentId;
        public DateTimeOffset OpenedUtc { get; } = DateTimeOffset.UtcNow;
        public DateTimeOffset LastActiveUtc { get; private set; } = DateTimeOffset.UtcNow;
        public bool IsEmpty { get { lock (_gate) return _participants.Count == 0; } }

        public void Add(string actorId) { lock (_gate) { _participants.Add(actorId); LastActiveUtc = DateTimeOffset.UtcNow; } }
        public bool Remove(string actorId) { lock (_gate) { var removed = _participants.Remove(actorId); LastActiveUtc = DateTimeOffset.UtcNow; return removed; } }
        public bool Contains(string actorId) { lock (_gate) return _participants.Contains(actorId); }
        public void Touch() { lock (_gate) LastActiveUtc = DateTimeOffset.UtcNow; }
        public IReadOnlyList<string> Participants() { lock (_gate) return _participants.Order(StringComparer.Ordinal).ToArray(); }
    }
}
