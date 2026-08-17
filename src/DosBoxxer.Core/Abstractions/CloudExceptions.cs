using DosBoxxer.Core.Models.Cloud;

namespace DosBoxxer.Core.Abstractions;

/// <summary>
/// Raised by <see cref="ICloudStorage"/> / <see cref="ICloudAuthService"/> implementations to
/// signal a failure category the sync engine can map to a <see cref="SyncErrorKind"/> and react
/// to (retry, offer manual start, surface a clear message) without leaking provider specifics.
/// </summary>
public sealed class CloudStorageException : Exception
{
    public CloudStorageException(SyncErrorKind kind, string? message = null, Exception? inner = null)
        : base(message ?? kind.ToString(), inner) => Kind = kind;

    public SyncErrorKind Kind { get; }
}
