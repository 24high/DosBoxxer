using System.Diagnostics;
using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Helpers;
using DosBoxxer.Core.Models;
using Microsoft.Extensions.Logging;

namespace DosBoxxer.Core.Infrastructure.DosBox;

/// <summary>
/// Starts DOSBox with a generated, game specific configuration file.
///
/// The process is started with <c>UseShellExecute = false</c> and an argument *vector*
/// (<see cref="ProcessStartInfo.ArgumentList"/>). No shell is involved and no string is
/// concatenated into a command line, so neither paths nor the user's additional arguments can
/// inject a command.
/// </summary>
public sealed class DosBoxLauncher : IDosBoxLauncher
{
    private readonly ISettingsService _settings;
    private readonly IDosBoxConfigBuilder _configBuilder;
    private readonly IAppPaths _paths;
    private readonly ILogger<DosBoxLauncher> _logger;

    private int _runningCount;

    public DosBoxLauncher(
        ISettingsService settings,
        IDosBoxConfigBuilder configBuilder,
        IAppPaths paths,
        ILogger<DosBoxLauncher> logger)
    {
        _settings = settings;
        _configBuilder = configBuilder;
        _paths = paths;
        _logger = logger;
    }

    public bool IsRunning => Volatile.Read(ref _runningCount) > 0;

    public event EventHandler<Game>? GameStarted;

    public event EventHandler<LaunchResult>? GameExited;

    public async Task<LaunchResult> LaunchAsync(Game game, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(game);

        var settings = _settings.Current;

        var validation = Validate(game, settings);
        if (validation is not null)
        {
            _logger.LogWarning("Launch of '{Title}' rejected: {Status}", game.Title, validation.Status);
            return validation;
        }

        var configPath = Path.Combine(_paths.TempDirectory, $"game-{game.Id:N}.conf");

        DosBoxConfigResult configResult;
        try
        {
            configResult = await _configBuilder.BuildAsync(
                new DosBoxConfigRequest
                {
                    BaseConfigPath = settings.BaseDosBoxConfigPath,
                    GameDirectory = game.GameDirectory,
                    LaunchFile = game.LaunchFile,
                    Overrides = game.DosBoxSettings,
                    AppendExit = settings.CloseDosBoxAfterExit,
                    OutputPath = configPath,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Could not write the generated DOSBox configuration");
            return LaunchResult.Failed(LaunchStatus.ConfigWriteFailed, ex.Message, configPath);
        }

        if (configResult.MangledNames.Count > 0)
        {
            _logger.LogWarning(
                "The following name(s) had to be shortened to DOS 8.3 form and may not resolve inside DOSBox: {Names}",
                string.Join(", ", configResult.MangledNames));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = settings.DosBoxExecutablePath!,
            UseShellExecute = false,
            WorkingDirectory = PathHelper.DirectoryExistsSafe(game.GameDirectory)
                ? game.GameDirectory
                : _paths.DataRoot,
        };

        startInfo.ArgumentList.Add("-conf");
        startInfo.ArgumentList.Add(configPath);

        foreach (var argument in CommandLineSplitter.Split(settings.DosBoxAdditionalArguments))
        {
            startInfo.ArgumentList.Add(argument);
        }

        _logger.LogInformation(
            "Starting DOSBox for '{Title}' with {ArgumentCount} argument(s)",
            game.Title,
            startInfo.ArgumentList.Count);

        var startedAt = DateTimeOffset.Now;
        Process? process = null;

        try
        {
            process = Process.Start(startInfo);

            if (process is null)
            {
                _logger.LogError("Process.Start returned no process handle");
                return LaunchResult.Failed(LaunchStatus.StartFailed, "Process.Start returned null", configPath);
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            _logger.LogError(ex, "DOSBox could not be started");
            process?.Dispose();
            return LaunchResult.Failed(LaunchStatus.StartFailed, ex.Message, configPath);
        }

        Interlocked.Increment(ref _runningCount);
        GameStarted?.Invoke(this, game);

        LaunchResult result;
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            var exitedAt = DateTimeOffset.Now;
            var exitCode = SafeExitCode(process);

            _logger.LogInformation(
                "DOSBox exited for '{Title}' with code {ExitCode} after {Seconds:F0}s",
                game.Title,
                exitCode,
                (exitedAt - startedAt).TotalSeconds);

            result = new LaunchResult
            {
                Status = LaunchStatus.Completed,
                ExitCode = exitCode,
                StartedAt = startedAt,
                ExitedAt = exitedAt,
                ConfigFilePath = configPath,
            };
        }
        catch (OperationCanceledException)
        {
            // The launcher is shutting down; leave the emulator running and record what we know.
            _logger.LogInformation("Stopped waiting for DOSBox because the operation was cancelled");
            result = new LaunchResult
            {
                Status = LaunchStatus.Completed,
                StartedAt = startedAt,
                ExitedAt = DateTimeOffset.Now,
                ConfigFilePath = configPath,
            };
        }
        finally
        {
            Interlocked.Decrement(ref _runningCount);
            process.Dispose();
        }

        GameExited?.Invoke(this, result);
        return result;
    }

    private static int? SafeExitCode(Process process)
    {
        try
        {
            return process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static LaunchResult? Validate(Game game, AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.DosBoxExecutablePath))
        {
            return LaunchResult.Failed(LaunchStatus.ExecutableNotConfigured);
        }

        if (!PathHelper.FileExistsSafe(settings.DosBoxExecutablePath))
        {
            return LaunchResult.Failed(LaunchStatus.ExecutableNotFound, settings.DosBoxExecutablePath);
        }

        if (!string.IsNullOrWhiteSpace(settings.BaseDosBoxConfigPath) &&
            !PathHelper.FileExistsSafe(settings.BaseDosBoxConfigPath))
        {
            return LaunchResult.Failed(LaunchStatus.BaseConfigNotFound, settings.BaseDosBoxConfigPath);
        }

        if (!PathHelper.DirectoryExistsSafe(game.GameDirectory))
        {
            return LaunchResult.Failed(LaunchStatus.GameDirectoryMissing, game.GameDirectory);
        }

        if (!PathHelper.FileExistsSafe(game.LaunchFile))
        {
            return LaunchResult.Failed(LaunchStatus.LaunchFileMissing, game.LaunchFile);
        }

        return null;
    }
}
