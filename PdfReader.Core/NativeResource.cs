using System;
using System.Threading;

namespace PdfReader.Core;

public sealed class NativeResource<THandle> : IDisposable
    where THandle : class
{
    private readonly Action<THandle> _release;
    private THandle? _handle;

    public NativeResource(THandle handle, Action<THandle> release)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(release);

        _handle = handle;
        _release = release;
    }

    public THandle Handle =>
        _handle ?? throw new ObjectDisposedException(typeof(NativeResource<THandle>).Name);

    public void Dispose()
    {
        THandle? handle = Interlocked.Exchange(ref _handle, null);
        if (handle is not null)
        {
            _release(handle);
        }
    }
}
