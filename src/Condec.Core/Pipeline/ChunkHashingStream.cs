// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

namespace Condec.Core.Pipeline;

/// <summary>
/// Write-only, forward-only stream in front of the temporary output file. Every byte a converter writes
/// is hashed on its way to disk, so verification compares the file against what was really written.
/// Seeking is unsupported on purpose (see <see cref="Conversion.ConversionRequest.Output"/>).
/// Disposing it doesn't close the file or the hasher: both belong to the pipeline.
/// </summary>
internal sealed class ChunkHashingStream : Stream
{
    private readonly Stream _inner;
    private readonly ChunkHasher _hasher;
    private readonly CancellationToken _ct;
    private bool _closed;

    public ChunkHashingStream(Stream inner, ChunkHasher hasher, CancellationToken ct)
    {
        _inner = inner;
        _hasher = hasher;
        _ct = ct;
    }

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => !_closed;

    public override long Length => throw new NotSupportedException();

    /// <summary>Bytes written so far. Setting it would be a seek, which isn't supported.</summary>
    public override long Position
    {
        get => _hasher.Length;
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        _inner.Flush();
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        return _inner.FlushAsync(cancellationToken);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        Write(buffer.AsSpan(offset, count));
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        _ct.ThrowIfCancellationRequested();

        _hasher.Append(buffer);
        _inner.Write(buffer);
    }

    public override void WriteByte(byte value) => Write(new ReadOnlySpan<byte>(in value));

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);
        return WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        _ct.ThrowIfCancellationRequested();
        cancellationToken.ThrowIfCancellationRequested();

        _hasher.Append(buffer.Span);
        await _inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        _closed = true;
        base.Dispose(disposing);
    }
}
