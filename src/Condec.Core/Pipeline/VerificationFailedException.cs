namespace Condec.Core.Pipeline;

public enum VerificationFailure
{
    /// <summary>The file on disk isn't as long as what was written.</summary>
    LengthMismatch,

    /// <summary>A chunk read back from disk doesn't match its hash from writing.</summary>
    ChunkMismatch,

    /// <summary>The SHA-256 of the whole file doesn't match its hash from writing.</summary>
    FileHashMismatch,

    /// <summary>The output can't be reopened as its target format.</summary>
    InvalidOutput,
}

/// <summary>The output failed verification, so it was deleted instead of saved.</summary>
public sealed class VerificationFailedException : Exception
{
    public VerificationFailedException(VerificationFailure failure, string message, int chunkNumber = 0, Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
        ChunkNumber = chunkNumber;
    }

    public VerificationFailure Failure { get; }

    /// <summary>The failing chunk, counted from 1, for <see cref="VerificationFailure.ChunkMismatch"/>; otherwise 0.</summary>
    public int ChunkNumber { get; }
}
