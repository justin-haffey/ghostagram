using System.Runtime.CompilerServices;

namespace Ghostagram.Execution;

public enum GraphExecutionSessionState
{
    Prepared,
    Running,
    WaitingForExternalInput,
    Completed,
    Failed,
    Cancelled,
    Disposed
}

/// <summary>
/// Retains the adapter run across human/external-input pauses. The caller owns this
/// session and must dispose it after completion, cancellation, or abandonment.
/// </summary>
public interface IGraphExecutionSession : IAsyncDisposable
{
    ExecutionRunIdentity Identity { get; }
    GraphExecutionSessionState State { get; }
    GraphExecutionResult Result { get; }
    OrchestrationCheckpointReference? LatestCheckpoint { get; }
    ExternalInputRequest? PendingExternalInput { get; }
    IAsyncEnumerable<OrchestrationEvent> StartAsync(CancellationToken cancellationToken = default);
    IAsyncEnumerable<OrchestrationEvent> ResumeAsync(OrchestrationCheckpointReference checkpoint, CancellationToken cancellationToken = default);
    IAsyncEnumerable<OrchestrationEvent> SubmitExternalResponseAsync(ExternalInputResponse response, CancellationToken cancellationToken = default);
    ValueTask CancelAsync(CancellationToken cancellationToken = default);
}

internal sealed class GraphExecutionSession(
    IOrchestrationAdapter adapter,
    IOrchestrationRun run,
    GraphExecutionRequest request) : IGraphExecutionSession
{
    private readonly HashSet<string> _graphNodeIds = request.Graph.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
    private readonly List<string> _completedNodeIds = [];
    private readonly SemaphoreSlim _segmentGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly object _sync = new();
    private CancellationTokenSource? _activeSegmentCancellation;
    private long _previousSequence = -1;
    private bool _disposeRequested;
    private bool _disposed;

    public ExecutionRunIdentity Identity => request.Identity;
    public GraphExecutionSessionState State { get; private set; } = GraphExecutionSessionState.Prepared;
    public GraphExecutionResult Result { get; private set; } = new(false, []);
    public OrchestrationCheckpointReference? LatestCheckpoint { get; private set; } = request.Checkpoint;
    public ExternalInputRequest? PendingExternalInput { get; private set; }

    public IAsyncEnumerable<OrchestrationEvent> StartAsync(CancellationToken cancellationToken = default) =>
        ProcessSegmentAsync(
            SegmentKind.Start,
            [GraphExecutionSessionState.Prepared],
            token => request.Checkpoint is null ? run.StartAsync(token) : run.ResumeAsync(request.Checkpoint, token),
            cancellationToken);

    public IAsyncEnumerable<OrchestrationEvent> ResumeAsync(OrchestrationCheckpointReference checkpoint, CancellationToken cancellationToken = default)
    {
        if (!adapter.Capabilities.SupportsCheckpoints)
            throw new InvalidOperationException($"Orchestration adapter '{adapter.Id}' does not support checkpoint resume.");
        GraphExecutionEngine.ValidateCheckpoint(checkpoint, adapter.Id, request.Graph.PlanFingerprint, request.Identity);
        return ProcessSegmentAsync(
            SegmentKind.Resume,
            [GraphExecutionSessionState.Prepared, GraphExecutionSessionState.WaitingForExternalInput],
            token =>
            {
                PendingExternalInput = null;
                return run.ResumeAsync(checkpoint, token);
            },
            cancellationToken);
    }

    public IAsyncEnumerable<OrchestrationEvent> SubmitExternalResponseAsync(ExternalInputResponse response, CancellationToken cancellationToken = default) =>
        ProcessSegmentAsync(
            SegmentKind.ExternalResponse,
            [GraphExecutionSessionState.WaitingForExternalInput],
            token =>
            {
                var pending = PendingExternalInput;
                if (pending is null || !string.Equals(response.RequestId, pending.RequestId, StringComparison.Ordinal))
                    throw new InvalidOperationException($"External response '{response.RequestId}' does not match the pending request.");
                PendingExternalInput = null;
                return run.SubmitExternalResponseAsync(response, token);
            },
            cancellationToken);

    public async ValueTask CancelAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!adapter.Capabilities.SupportsCancellation)
            throw new InvalidOperationException($"Orchestration adapter '{adapter.Id}' does not support cancellation.");

        CancellationTokenSource? active;
        lock (_sync)
        {
            if (State is GraphExecutionSessionState.Completed or GraphExecutionSessionState.Failed or GraphExecutionSessionState.Cancelled) return;
            active = _activeSegmentCancellation;
            SetTerminalState(GraphExecutionSessionState.Cancelled, "The orchestration run was cancelled.");
        }
        active?.Cancel();
        await run.CancelAsync(cancellationToken).ConfigureAwait(false);
    }

    private async IAsyncEnumerable<OrchestrationEvent> ProcessSegmentAsync(
        SegmentKind kind,
        GraphExecutionSessionState[] allowedStates,
        Func<CancellationToken, IAsyncEnumerable<OrchestrationEvent>> sourceFactory,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (!await _segmentGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("Only one execution-session segment may stream events at a time.");

        CancellationTokenSource? segmentCancellation = null;
        var boundaryReached = false;
        try
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                if (!allowedStates.Contains(State))
                    throw new InvalidOperationException($"Cannot begin {kind} while execution session state is '{State}'.");
                segmentCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeCancellation.Token);
                _activeSegmentCancellation = segmentCancellation;
                State = GraphExecutionSessionState.Running;
            }

            await using var enumerator = sourceFactory(segmentCancellation.Token).GetAsyncEnumerator(segmentCancellation.Token);
            while (true)
            {
                OrchestrationEvent item;
                try
                {
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false)) break;
                    item = enumerator.Current;
                    lock (_sync)
                    {
                        if (State != GraphExecutionSessionState.Running || _disposeRequested)
                            throw new OperationCanceledException("The execution session left the running state before the adapter event could be applied.", segmentCancellation.Token);
                        if (boundaryReached) throw new InvalidOperationException("An orchestration adapter emitted events after a pause or terminal event.");
                        ValidateEventLocked(item);
                        _previousSequence = item.Sequence;
                        switch (item.Kind)
                        {
                            case OrchestrationEventKind.NodeCompleted:
                                _completedNodeIds.Add(item.NodeId!);
                                if (_completedNodeIds.Count > request.Policy.MaxNodeExecutions)
                                    throw new InvalidOperationException("The orchestration exceeded its node-execution budget.");
                                break;
                            case OrchestrationEventKind.ExternalInputRequired:
                                PendingExternalInput = item.ExternalInput;
                                State = GraphExecutionSessionState.WaitingForExternalInput;
                                Result = new(false, _completedNodeIds.ToArray(), Checkpoint: LatestCheckpoint, PendingExternalInput: PendingExternalInput);
                                boundaryReached = true;
                                break;
                            case OrchestrationEventKind.RunCompleted:
                                State = GraphExecutionSessionState.Completed;
                                Result = new(true, _completedNodeIds.ToArray(), Checkpoint: LatestCheckpoint);
                                boundaryReached = true;
                                break;
                            case OrchestrationEventKind.RunFailed:
                                SetTerminalState(GraphExecutionSessionState.Failed, item.Error ?? "The orchestration adapter reported failure.");
                                boundaryReached = true;
                                break;
                            case OrchestrationEventKind.RunCancelled:
                                SetTerminalState(GraphExecutionSessionState.Cancelled, "The orchestration run was cancelled.");
                                boundaryReached = true;
                                break;
                        }
                    }
                }
                catch (Exception exception)
                {
                    await HandleStreamExceptionAsync(exception).ConfigureAwait(false);
                    throw;
                }
                yield return item;
            }

            try
            {
                ValidateLatestCheckpoint();
                lock (_sync)
                {
                    if (!boundaryReached && State == GraphExecutionSessionState.Running)
                        SetTerminalState(GraphExecutionSessionState.Failed, "The orchestration adapter event stream ended without a pause or terminal event.");
                }
            }
            catch (Exception exception)
            {
                await HandleStreamExceptionAsync(exception).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            lock (_sync)
            {
                if (!_disposeRequested && State == GraphExecutionSessionState.Running)
                    SetTerminalState(GraphExecutionSessionState.Failed, "The orchestration event stream was abandoned before a pause or terminal event.");
                _activeSegmentCancellation = null;
            }
            segmentCancellation?.Dispose();
            _segmentGate.Release();
        }
    }

    private async ValueTask HandleStreamExceptionAsync(Exception exception)
    {
        if (exception is OperationCanceledException && (_disposeRequested || _lifetimeCancellation.IsCancellationRequested)) return;
        if (exception is OperationCanceledException)
        {
            var notifyAdapter = false;
            lock (_sync)
            {
                if (State != GraphExecutionSessionState.Cancelled)
                {
                    SetTerminalState(GraphExecutionSessionState.Cancelled, "The orchestration event stream was cancelled.");
                    notifyAdapter = true;
                }
            }
            if (notifyAdapter && adapter.Capabilities.SupportsCancellation) await run.CancelAsync(CancellationToken.None).ConfigureAwait(false);
            return;
        }
        lock (_sync)
        {
            if (State is not (GraphExecutionSessionState.Cancelled or GraphExecutionSessionState.Disposed))
                SetTerminalState(GraphExecutionSessionState.Failed, exception.Message);
        }
    }

    private void ValidateEventLocked(OrchestrationEvent item)
    {
        if (item.Sequence <= _previousSequence)
            throw new InvalidOperationException("Orchestration events must have a strictly increasing sequence across the entire run.");
        if (item.Kind == OrchestrationEventKind.NodeCompleted && (item.NodeId is null || !_graphNodeIds.Contains(item.NodeId)))
            throw new InvalidOperationException($"Adapter completed unknown graph node '{item.NodeId}'.");
        if (item.Kind == OrchestrationEventKind.ExternalInputRequired)
        {
            if (!adapter.Capabilities.SupportsExternalInput)
                throw new InvalidOperationException($"Orchestration adapter '{adapter.Id}' emitted external input without declaring support.");
            var input = item.ExternalInput ?? throw new InvalidOperationException("ExternalInputRequired events must include a request.");
            if (!_graphNodeIds.Contains(input.NodeId) ||
                !string.Equals(input.RunId, request.Identity.RunId, StringComparison.Ordinal) ||
                !string.Equals(input.ConversationId, request.Identity.ConversationId, StringComparison.Ordinal))
                throw new InvalidOperationException("External input identity does not match the execution run, conversation, and graph.");
        }
        if (item.Checkpoint is { } checkpoint)
        {
            GraphExecutionEngine.ValidateCheckpoint(checkpoint, adapter.Id, request.Graph.PlanFingerprint, request.Identity);
            LatestCheckpoint = checkpoint;
        }
    }

    private void ValidateLatestCheckpoint()
    {
        if (run.LatestCheckpoint is not { } latest) return;
        GraphExecutionEngine.ValidateCheckpoint(latest, adapter.Id, request.Graph.PlanFingerprint, request.Identity);
        lock (_sync)
        {
            LatestCheckpoint = latest;
            Result = Result with { Checkpoint = latest };
        }
    }

    private void SetTerminalState(GraphExecutionSessionState state, string error)
    {
        State = state;
        PendingExternalInput = null;
        Result = new(false, _completedNodeIds.ToArray(), error, LatestCheckpoint);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed || _disposeRequested) throw new ObjectDisposedException(nameof(GraphExecutionSession));
    }

    public async ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            if (_disposed || _disposeRequested) return;
            _disposeRequested = true;
            _lifetimeCancellation.Cancel();
            _activeSegmentCancellation?.Cancel();
        }

        await _segmentGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await run.DisposeAsync().ConfigureAwait(false);
            lock (_sync)
            {
                _disposed = true;
                State = GraphExecutionSessionState.Disposed;
            }
        }
        finally
        {
            _segmentGate.Release();
            _lifetimeCancellation.Dispose();
            _segmentGate.Dispose();
        }
    }

    private enum SegmentKind
    {
        Start,
        Resume,
        ExternalResponse
    }
}
