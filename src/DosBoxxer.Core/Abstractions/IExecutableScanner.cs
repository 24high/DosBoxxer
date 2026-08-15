using DosBoxxer.Core.Models;

namespace DosBoxxer.Core.Abstractions;

public interface IExecutableScanner
{
    /// <summary>
    /// Recursively scans <paramref name="rootDirectory"/> for <c>.exe</c>, <c>.bat</c> and
    /// <c>.com</c> files. Results are ordered by descending heuristic score; installers and
    /// setup utilities are pushed to the end but never removed.
    /// </summary>
    Task<IReadOnlyList<ExecutableCandidate>> ScanAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default);
}
