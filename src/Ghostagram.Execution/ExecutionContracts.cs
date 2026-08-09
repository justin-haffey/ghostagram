using System.Collections.Concurrent;
using Ghostagram.Core;

namespace Ghostagram.Execution;

public interface IExecutionData
{
    bool TryGet<T>(string key, out T? value);
    T GetOrAdd<T>(string key, Func<string, T> valueFactory) where T : notnull;
    void Set<T>(string key, T value);
    bool TryRemove(string key, out object? value);
    IReadOnlyDictionary<string, object?> Snapshot();
    bool TryGet<T>(ExecutionDataKey key, out T? value, out long version);
    long Set<T>(ExecutionDataKey key, T value);
    bool TryUpdate<T>(ExecutionDataKey key, long expectedVersion, Func<T?, T> update, out long newVersion);
    IReadOnlyDictionary<ExecutionDataKey, ExecutionDataValue> SnapshotVersioned();
}

public sealed record ExecutionDataScope(string RunId, string? NodeId = null, string? ConversationId = null);
public sealed record ExecutionDataKey(ExecutionDataScope Scope, string Name);
public sealed record ExecutionDataValue(object? Value, long Version);

/// <summary>Thread-safe execution data shared by nodes scheduled in concurrent stages.</summary>
public sealed class ConcurrentExecutionData : IExecutionData
{
    private static readonly ExecutionDataScope DefaultScope = new("default");
    private readonly ConcurrentDictionary<ExecutionDataKey, ExecutionDataValue> _values = new();

    public bool TryGet<T>(string key, out T? value)
    {
        return TryGet(new(DefaultScope, key), out value, out _);
    }

    public bool TryGet<T>(ExecutionDataKey key, out T? value, out long version)
    {
        if (_values.TryGetValue(key, out var candidate) && candidate.Value is T typed)
        {
            value = typed;
            version = candidate.Version;
            return true;
        }
        value = default;
        version = candidate?.Version ?? 0;
        return false;
    }

    public T GetOrAdd<T>(string key, Func<string, T> valueFactory) where T : notnull =>
        (T)_values.GetOrAdd(new(DefaultScope, key), static (dataKey, factory) => new(factory(dataKey.Name), 1), valueFactory).Value!;

    public void Set<T>(string key, T value)
    {
        var dataKey = new ExecutionDataKey(DefaultScope, key);
        _values.AddOrUpdate(dataKey, _ => new(value, 1), (_, current) => new(value, current.Version + 1));
    }

    public bool TryUpdate<T>(ExecutionDataKey key, long expectedVersion, Func<T?, T> update, out long newVersion)
    {
        while (true)
        {
            if (!_values.TryGetValue(key, out var current) || current.Version != expectedVersion)
            {
                newVersion = current?.Version ?? 0;
                return false;
            }
            var typed = current.Value is T value ? value : default;
            var next = new ExecutionDataValue(update(typed), current.Version + 1);
            if (_values.TryUpdate(key, next, current))
            {
                newVersion = next.Version;
                return true;
            }
        }
    }

    public long Set<T>(ExecutionDataKey key, T value) =>
        _values.AddOrUpdate(key, _ => new(value, 1), (_, current) => new(value, current.Version + 1)).Version;

    public bool TryRemove(string key, out object? value)
    {
        var removed = _values.TryRemove(new(DefaultScope, key), out var entry);
        value = entry?.Value;
        return removed;
    }
    public IReadOnlyDictionary<string, object?> Snapshot() => _values.Where(pair => pair.Key.Scope == DefaultScope).ToDictionary(pair => pair.Key.Name, pair => pair.Value.Value, StringComparer.Ordinal);
    public IReadOnlyDictionary<ExecutionDataKey, ExecutionDataValue> SnapshotVersioned() => new Dictionary<ExecutionDataKey, ExecutionDataValue>(_values);
}

public sealed record ComponentActivationRequest(
    string ComponentKey,
    string NodeId,
    string RunId,
    string ActivationId,
    int Attempt,
    string? ConversationId = null,
    System.Text.Json.JsonElement? Configuration = null,
    IReadOnlyDictionary<string, System.Text.Json.JsonElement>? ActivationData = null)
{
    public void ValidateAgainst(ExecutionRunIdentity identity)
    {
        if (!string.Equals(RunId, identity.RunId, StringComparison.Ordinal) ||
            !string.Equals(ActivationId, identity.ActivationId, StringComparison.Ordinal) ||
            Attempt != identity.Attempt ||
            !string.Equals(ConversationId, identity.ConversationId, StringComparison.Ordinal))
            throw new InvalidOperationException("Component activation identity does not match the execution run.");
    }
}

public sealed record ComponentInvocation(string Operation, System.Text.Json.JsonElement? Input = null, string? CorrelationId = null);
public sealed record ComponentUpdate(long Sequence, string Kind, System.Text.Json.JsonElement? Value = null, string? Error = null);

public interface IExecutionComponentLease : IAsyncDisposable
{
    string ComponentKey { get; }
    IAsyncEnumerable<ComponentUpdate> InvokeAsync(ComponentInvocation invocation, CancellationToken cancellationToken = default);
}

/// <summary>
/// Neutral boundary for future agent-component bootstrapping. Implementations own scopes,
/// pooling, and disposal; no orchestration framework type crosses this contract.
/// </summary>
public interface IExecutionComponentActivator
{
    ValueTask<IExecutionComponentLease> AcquireAsync(ComponentActivationRequest request, CancellationToken cancellationToken = default);
}

public sealed record NodeExecutionContext(
    DiagramNode Node,
    ExecutionRunIdentity Identity,
    ExecutionRunPolicy Policy,
    IExecutionData Data,
    IExecutionComponentActivator Components)
{
    public ComponentActivationRequest CreateComponentActivation(
        string componentKey,
        System.Text.Json.JsonElement? configuration = null,
        IReadOnlyDictionary<string, System.Text.Json.JsonElement>? activationData = null) =>
        new(componentKey, Node.Id, Identity.RunId, Identity.ActivationId, Identity.Attempt, Identity.ConversationId, configuration, activationData);
}

public sealed record ExecutionRunIdentity(string RunId, string ActivationId, int Attempt, string? ConversationId = null);
public sealed record ExecutionRunPolicy(
    TimeSpan? Timeout = null,
    int MaxNodeExecutions = 10_000,
    int MaxParallelism = 32,
    long? MaxCostUnits = null);

public sealed record NodeExecutionResult(
    bool Succeeded,
    IReadOnlyDictionary<string, object?>? Outputs = null,
    string? NextPortId = null,
    string? Error = null)
{
    public static NodeExecutionResult Success(IReadOnlyDictionary<string, object?>? outputs = null, string? nextPortId = null) => new(true, outputs, nextPortId);
    public static NodeExecutionResult Failure(string error) => new(false, Error: error);
}

public sealed record GraphExecutionRequest(
    CompiledGraph Graph,
    string AdapterId,
    ExecutionRunIdentity Identity,
    ExecutionRunPolicy Policy,
    IExecutionData Data,
    IExecutionComponentActivator Components,
    OrchestrationCheckpointReference? Checkpoint = null);

public sealed record GraphExecutionResult(
    bool Succeeded,
    IReadOnlyList<string> CompletedNodeIds,
    string? Error = null,
    OrchestrationCheckpointReference? Checkpoint = null,
    ExternalInputRequest? PendingExternalInput = null);

public sealed record OrchestrationAdapterCapabilities(
    IReadOnlySet<GraphCompileProfile> CompileProfiles,
    bool SupportsCheckpoints,
    bool SupportsExternalInput,
    bool SupportsStreaming,
    bool SupportsCancellation);

public sealed record OrchestrationCheckpointReference(string AdapterId, string Value, string PlanFingerprint, string RunId, string? ConversationId = null);
public sealed record ExternalInputRequest(string RequestId, string RunId, string NodeId, string Kind, string? ConversationId = null, string? Prompt = null, System.Text.Json.JsonElement? Schema = null);
public sealed record ExternalInputResponse(string RequestId, System.Text.Json.JsonElement Value);

public enum OrchestrationEventKind
{
    RunStarted,
    NodeStarted,
    NodeCompleted,
    ExternalInputRequired,
    CheckpointCreated,
    RunCompleted,
    RunFailed,
    RunCancelled
}

public sealed record OrchestrationEvent(
    long Sequence,
    OrchestrationEventKind Kind,
    DateTimeOffset Timestamp,
    string? NodeId = null,
    System.Text.Json.JsonElement? Data = null,
    ExternalInputRequest? ExternalInput = null,
    OrchestrationCheckpointReference? Checkpoint = null,
    string? Error = null);

public sealed record OrchestrationPreparationRequest(
    CompiledGraph Graph,
    ExecutionRunIdentity Identity,
    ExecutionRunPolicy Policy,
    IExecutionData Data,
    IExecutionComponentActivator Components,
    OrchestrationCheckpointReference? Checkpoint = null);

/// <summary>A single prepared adapter run with ordered events and explicit lifecycle ownership.</summary>
public interface IOrchestrationRun : IAsyncDisposable
{
    string RunId { get; }
    string? ConversationId { get; }
    string PlanFingerprint { get; }
    OrchestrationCheckpointReference? LatestCheckpoint { get; }
    IAsyncEnumerable<OrchestrationEvent> StartAsync(CancellationToken cancellationToken = default);
    IAsyncEnumerable<OrchestrationEvent> ResumeAsync(OrchestrationCheckpointReference checkpoint, CancellationToken cancellationToken = default);
    IAsyncEnumerable<OrchestrationEvent> SubmitExternalResponseAsync(ExternalInputResponse response, CancellationToken cancellationToken = default);
    ValueTask CancelAsync(CancellationToken cancellationToken = default);
}

/// <summary>Adapter implemented by MAF or another orchestration runtime outside Ghostagram's neutral libraries.</summary>
public interface IOrchestrationAdapter
{
    string Id { get; }
    OrchestrationAdapterCapabilities Capabilities { get; }
    ValueTask<IOrchestrationRun> PrepareAsync(OrchestrationPreparationRequest request, CancellationToken cancellationToken = default);
}

public interface IGraphExecutionEngine
{
    GraphCompilationResult Compile(DiagramDocument document, GraphCompileOptions options);
    ValueTask<IGraphExecutionSession> StartSessionAsync(GraphExecutionRequest request, CancellationToken cancellationToken = default);
    ValueTask<GraphExecutionResult> ExecuteAsync(GraphExecutionRequest request, CancellationToken cancellationToken = default);
}

public sealed class GraphExecutionEngine(IGraphCompiler compiler, IEnumerable<IOrchestrationAdapter> adapters) : IGraphExecutionEngine
{
    private readonly IReadOnlyDictionary<string, IOrchestrationAdapter> _adapters = adapters.ToDictionary(adapter => adapter.Id, StringComparer.Ordinal);

    public GraphCompilationResult Compile(DiagramDocument document, GraphCompileOptions options) => compiler.Compile(document, options);

    public async ValueTask<IGraphExecutionSession> StartSessionAsync(GraphExecutionRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Identity.RunId) || string.IsNullOrWhiteSpace(request.Identity.ActivationId) || request.Identity.Attempt < 1)
            throw new ArgumentException("Run and activation identifiers are required and Attempt must be positive.", nameof(request));
        if (request.Policy.MaxNodeExecutions < 1 || request.Policy.MaxParallelism < 1 || request.Policy.MaxCostUnits < 0)
            throw new ArgumentException("Execution policy budgets must be positive.", nameof(request));
        if (!_adapters.TryGetValue(request.AdapterId, out var adapter))
            throw new KeyNotFoundException($"Orchestration adapter '{request.AdapterId}' is not registered.");
        if (!adapter.Capabilities.CompileProfiles.Contains(request.Graph.Profile))
            throw new InvalidOperationException($"Orchestration adapter '{adapter.Id}' does not support compile profile '{request.Graph.Profile}'.");
        if (request.Checkpoint is { } checkpoint)
        {
            if (!adapter.Capabilities.SupportsCheckpoints)
                throw new InvalidOperationException($"Orchestration adapter '{adapter.Id}' does not support checkpoint resume.");
            if (!string.Equals(checkpoint.AdapterId, adapter.Id, StringComparison.Ordinal))
                throw new InvalidOperationException($"Checkpoint adapter '{checkpoint.AdapterId}' does not match requested adapter '{adapter.Id}'.");
            ValidateCheckpoint(checkpoint, adapter.Id, request.Graph.PlanFingerprint, request.Identity);
        }
        var run = await adapter.PrepareAsync(
            new(request.Graph, request.Identity, request.Policy, request.Data, request.Components, request.Checkpoint), cancellationToken).ConfigureAwait(false);
        try
        {
            if (!string.Equals(run.RunId, request.Identity.RunId, StringComparison.Ordinal))
                throw new InvalidOperationException($"Adapter run id '{run.RunId}' does not match requested run '{request.Identity.RunId}'.");
            if (!string.Equals(run.ConversationId, request.Identity.ConversationId, StringComparison.Ordinal))
                throw new InvalidOperationException("Adapter run conversation id does not match the execution request.");
            if (!string.Equals(run.PlanFingerprint, request.Graph.PlanFingerprint, StringComparison.Ordinal))
                throw new InvalidOperationException($"Adapter run plan fingerprint '{run.PlanFingerprint}' does not match compiled graph '{request.Graph.PlanFingerprint}'.");
            return new GraphExecutionSession(adapter, run, request);
        }
        catch
        {
            await run.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask<GraphExecutionResult> ExecuteAsync(GraphExecutionRequest request, CancellationToken cancellationToken = default)
    {
        await using var session = await StartSessionAsync(request, cancellationToken).ConfigureAwait(false);
        await foreach (var _ in session.StartAsync(cancellationToken).ConfigureAwait(false)) { }
        if (session.State == GraphExecutionSessionState.WaitingForExternalInput)
            throw new InvalidOperationException("The orchestration requires external input. Use StartSessionAsync to retain and continue the execution session.");
        return session.Result;
    }

    internal static void ValidateCheckpoint(OrchestrationCheckpointReference checkpoint, string adapterId, string planFingerprint, ExecutionRunIdentity identity)
    {
        if (!string.Equals(checkpoint.AdapterId, adapterId, StringComparison.Ordinal) ||
            !string.Equals(checkpoint.PlanFingerprint, planFingerprint, StringComparison.Ordinal) ||
            !string.Equals(checkpoint.RunId, identity.RunId, StringComparison.Ordinal) ||
            !string.Equals(checkpoint.ConversationId, identity.ConversationId, StringComparison.Ordinal))
            throw new InvalidOperationException("Adapter checkpoint identity does not match the adapter, run, conversation, and compiled plan.");
    }
}
