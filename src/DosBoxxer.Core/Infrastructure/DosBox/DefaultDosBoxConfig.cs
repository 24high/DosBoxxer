using System.Reflection;
using DosBoxxer.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace DosBoxxer.Core.Infrastructure.DosBox;

/// <summary>
/// Serves the embedded default <c>dosbox.conf</c> and writes it to disk on demand.
///
/// The configuration is compiled into the assembly as the resource
/// <c>DosBoxxer.Core.Assets.dosbox-default.conf</c>. On game start it is materialised into the
/// program directory as <c>dosbox.conf</c> (falling back to the user data directory when the
/// program directory cannot be written to, e.g. a read-only install). The original file is never
/// modified when a game runs — the launcher always generates a separate per-game configuration
/// from it.
/// </summary>
public sealed class DefaultDosBoxConfig : IDefaultDosBoxConfig
{
    public const string ResourceName = "DosBoxxer.Core.Assets.dosbox-default.conf";
    public const string FileName = "dosbox.conf";

    private readonly IAppPaths _paths;
    private readonly ILogger<DefaultDosBoxConfig> _logger;
    private readonly Lazy<string> _content;

    public DefaultDosBoxConfig(IAppPaths paths, ILogger<DefaultDosBoxConfig> logger)
    {
        _paths = paths;
        _logger = logger;
        _content = new Lazy<string>(LoadEmbedded);
    }

    public string GetContent() => _content.Value;

    public async Task<string> MaterializeAsync(CancellationToken cancellationToken = default)
    {
        var content = _content.Value;

        // Preferred location: the program directory, as requested. Fall back to the data
        // directory when that is not writable (e.g. installed under Program Files or /usr).
        var programPath = Path.Combine(_paths.ProgramDirectory, FileName);
        if (await TryWriteAsync(programPath, content, cancellationToken).ConfigureAwait(false))
        {
            return programPath;
        }

        var dataPath = Path.Combine(_paths.DataRoot, FileName);
        Directory.CreateDirectory(_paths.DataRoot);
        await File.WriteAllTextAsync(dataPath, content, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Default dosbox.conf materialised in the data directory (program directory not writable)");
        return dataPath;
    }

    private async Task<bool> TryWriteAsync(string path, string content, CancellationToken cancellationToken)
    {
        try
        {
            // Only rewrite when missing or changed, so a user who edits the materialised file
            // keeps their changes until the embedded default itself changes.
            if (File.Exists(path))
            {
                var existing = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                if (string.Equals(existing, content, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            await File.WriteAllTextAsync(path, content, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Default dosbox.conf materialised at {Path}", path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug("Could not write the default dosbox.conf to {Path}: {Type}", path, ex.GetType().Name);
            return false;
        }
    }

    private static string LoadEmbedded()
    {
        var assembly = typeof(DefaultDosBoxConfig).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' is missing from the assembly.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
