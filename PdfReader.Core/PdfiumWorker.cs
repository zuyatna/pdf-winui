using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace PdfReader.Core;

public sealed class PdfiumWorker : IAsyncDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly object _gate = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly TaskCompletionSource<bool> _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread _thread;
    private readonly int _capacity;
    private int _normalOutstanding;
    private bool _stopping;
    private bool _disposed;

    public static PdfiumWorker Shared { get; } = new();

    public PdfiumWorker(int capacity = 8)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _capacity = capacity;
        _thread = new Thread(Run)
        {
            Name = "PDFium worker",
            IsBackground = true,
        };
        _thread.Start();
    }

    public Task RunAsync(Action work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        return RunAsync(
            () =>
            {
                work();
                return true;
            },
            cancellationToken);
    }

    public Task<T> RunAsync<T>(Func<T> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);

        if (Thread.CurrentThread == _thread)
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_stopping || _disposed, this);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(work());
        }

        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        using CancellationTokenRegistration registration = linked.Token.Register(() =>
        {
            lock (_gate)
            {
                Monitor.PulseAll(_gate);
            }
        });

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopping || _disposed, this);
            cancellationToken.ThrowIfCancellationRequested();

            while (_normalOutstanding >= _capacity && !_stopping && !_disposed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Monitor.Wait(_gate);
            }

            ObjectDisposedException.ThrowIf(_stopping || _disposed, this);
            cancellationToken.ThrowIfCancellationRequested();

            _normalOutstanding++;
            _queue.Add(() =>
            {
                try
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        tcs.TrySetCanceled(cancellationToken);
                        return;
                    }

                    T result = work();
                    tcs.TrySetResult(result);
                }
                catch (OperationCanceledException oce)
                {
                    tcs.TrySetCanceled(oce.CancellationToken.IsCancellationRequested ? oce.CancellationToken : cancellationToken);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
                finally
                {
                    ReleaseNormalSlot();
                }
            });
        }

        return tcs.Task;
    }

    internal Task RunCleanupAsync(Action cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);

        if (Thread.CurrentThread == _thread)
        {
            cleanup();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_gate)
        {
            if (_stopping || _disposed)
            {
                return Task.CompletedTask;
            }

            _queue.Add(() =>
            {
                try
                {
                    cleanup();
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });
        }

        return tcs.Task;
    }

    private void ReleaseNormalSlot()
    {
        lock (_gate)
        {
            _normalOutstanding--;
            Monitor.PulseAll(_gate);
        }
    }

    private void Run()
    {
        foreach (Action job in _queue.GetConsumingEnumerable())
            job();

        _stopped.TrySetResult(true);
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (!_stopping && !_disposed)
            {
                _stopping = true;
                _shutdown.Cancel();
                _queue.CompleteAdding();
                Monitor.PulseAll(_gate);
            }
        }

        await _stopped.Task.ConfigureAwait(false);

        lock (_gate)
        {
            if (!_disposed)
            {
                _disposed = true;
                _queue.Dispose();
                _shutdown.Dispose();
            }
        }
    }
}
