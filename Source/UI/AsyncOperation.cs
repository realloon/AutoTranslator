namespace Translator.UI;

/// <summary>
/// Tracks a single background task that the IMGUI loop polls once per frame.
/// Owns a cancellation source so replacing an operation aborts the old request,
/// and converts exceptions (including synchronous ones) into data.
/// All members are called from the IMGUI thread.
/// </summary>
internal sealed class AsyncOperation<T> where T : class {
    private CancellationTokenSource? _cancellation;
    private Task<T>? _task;
    private Exception? _startError;

    public bool IsRunning => _task is { IsCompleted: false };

    public void Start(Func<CancellationToken, Task<T>> operation) {
        DisposeCancellation();
        _startError = null;

        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;

        try {
            _task = operation(cancellation.Token);
        } catch (Exception ex) {
            _startError = ex;
            _task = null;
            DisposeCancellation();
        }
    }

    public void Cancel() {
        _task = null;
        DisposeCancellation();
    }

    public bool TryTakeResult(out T result, out Exception? error) {
        result = null!;
        error = null;

        if (_startError is not null) {
            error = _startError;
            _startError = null;
            return true;
        }

        var task = _task;
        if (task is null || !task.IsCompleted) {
            return false;
        }

        _task = null;
        DisposeCancellation();

        if (task.IsCompletedSuccessfully) {
            result = task.Result;
        } else {
            error = task.Exception?.GetBaseException() ?? new OperationCanceledException();
        }

        return true;
    }

    private void DisposeCancellation() {
        var cancellation = _cancellation;
        _cancellation = null;
        if (cancellation is null) {
            return;
        }

        cancellation.Cancel();
        cancellation.Dispose();
    }
}
