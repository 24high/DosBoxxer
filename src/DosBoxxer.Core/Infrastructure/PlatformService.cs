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

    public bool OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            _logger.LogWarning("Refusing to open a non-http(s) URL");
            return false;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // On Windows a URL must go through the shell to reach the default browser.
            try
            {
                using var process = Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                return process is not null;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
            {
                _logger.LogWarning("Could not open the browser ({Type})", ex.GetType().Name);
                return false;
            }
        }

        var command = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "open" : "xdg-open";
        return Start(command, url);
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
