using System;
using System.IO;

namespace DosBoxxer.Tests;

/// <summary>
/// Disposable scratch directory. Tests that touch the file system create one of these so they
/// never write outside the system temp folder and always clean up after themselves.
/// </summary>
public sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "dosboxxer-tests-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string CreateFile(string relativePath, string content = "", int padToBytes = 0)
    {
        var full = System.IO.Path.Combine(Path, relativePath.Replace('\\', System.IO.Path.DirectorySeparatorChar));
        var directory = System.IO.Path.GetDirectoryName(full);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (padToBytes > content.Length)
        {
            content += new string('x', padToBytes - content.Length);
        }

        File.WriteAllText(full, content);
        return full;
    }

    public string CreateSubdirectory(string relativePath)
    {
        var full = System.IO.Path.Combine(Path, relativePath.Replace('\\', System.IO.Path.DirectorySeparatorChar));
        Directory.CreateDirectory(full);
        return full;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // A locked file must not fail the test run.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
