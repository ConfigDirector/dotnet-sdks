namespace ConfigDirector.EventSource;

// Cancels the stream when nothing arrives on it for a while.
//
// The timer resets on bytes, not on events, because the server keeps a quiet stream alive with SSE
// comments and the parser does not surface those. Measured between events, a connection with
// nothing to report but working perfectly would be torn down on every keepalive interval.
internal sealed class IdleTimeoutStream : Stream
{
    private readonly Stream _inner;
    private readonly CancellationTokenSource _idle;
    private readonly TimeSpan _timeout;

    internal IdleTimeoutStream(Stream inner, CancellationTokenSource idle, TimeSpan timeout)
    {
        _inner = inner;
        _idle = idle;
        _timeout = timeout;
        Reset();
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

#if !NET8_0_OR_GREATER
    public override async Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        var read = await _inner.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
        Reset(read);
        return read;
    }
#endif

#if NET8_0_OR_GREATER
    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        var read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        Reset(read);
        return read;
    }
#endif

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = _inner.Read(buffer, offset, count);
        Reset(read);
        return read;
    }

    public override void Flush() => _inner.Flush();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }

    private void Reset(int read)
    {
        if (read > 0)
        {
            Reset();
        }
    }

    private void Reset()
    {
        if (_timeout > TimeSpan.Zero)
        {
            _idle.CancelAfter(_timeout);
        }
    }
}
