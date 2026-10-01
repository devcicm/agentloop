namespace AgentLoop.Security;

/// <summary>Caps bytes before StreamReader/HTTP materializes an untrusted response.</summary>
public sealed class BoundedReadStream(Stream inner, long maximum) : Stream
{
    private long _read;
    private void Account(int count) { _read += count; if (_read > maximum) throw new IOException("Provider response exceeds byte budget."); }
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => _read; set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) { var n = inner.Read(buffer, offset, count); Account(n); return n; }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    { var n = await inner.ReadAsync(buffer, cancellationToken); Account(n); return n; }
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
