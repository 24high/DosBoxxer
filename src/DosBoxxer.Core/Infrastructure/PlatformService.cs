using System.Diagnostics;
using System.Runtime.InteropServices;
using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Helpers;
using Microsoft.Extensions.Logging;

namespace DosBoxxer.Core.Infrastructure;

/// <summary>
/// Opens directories in the platform file manager. Each platform gets its own launcher command;
/// the path is always passed as a separate argument, never interpolated into a shell string.
/// </summary>
public sealed class PlatformService : IPlatformService
{
    private readonly ILogger<PlatformService> _logger;

    public PlatformService(ILogger<PlatformService> logger) => _logger = logger;

    public bool OpenDirectory(string directory)
    {
        if (!PathHelper.DirectoryExistsSafe(directory))
        {
            _logger.LogWarning("Cannot open a directory that does not exist");
            return false;
        }

        return Start(GetFileManagerCommand(), directory);
    }

    public bool RevealFile(string filePath)
    {
        if (!PathHelper.FileExistsSafe(filePath))
        {
            var directory = Path.GetDirectoryName(filePath);
            return directory is not null && OpenDirectory(directory);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Start("explorer.exe", "/select," + filePath);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return Start("open", "-R", filePath);
        }

        var parent = Path.GetDirectoryName(filePath);
        return parent is not null && OpenDirectory(parent);
    }

    private static string GetFileManagerCommand()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "explorer.exe";
        }

        return RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "open" : "xdg-open";
    }

    private bool Start(string fileName, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo);
            return process is not null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            _logger.LogWarning("Could not start the platform file manager ({Type})", ex.GetType().Name);
            return false;
        }
    }
}
