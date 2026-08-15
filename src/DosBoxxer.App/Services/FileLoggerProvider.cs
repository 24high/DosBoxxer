using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;
using DosBoxxer.Core.Helpers;
using Microsoft.Extensions.Logging;

namespace DosBoxxer.App.Services;

/// <summary>
/// Minimal rolling file logger. A dedicated logging framework would be overkill here and would
/// add a dependency for a single feature; this provider writes structured, timestamped lines
/// into <c>logs/dosboxxer-yyyyMMdd.log</c> and keeps the last few days.
///
/// Every message passes through <see cref="LogSanitizer"/>, so credentials that slipped into an
/// exception message or a URL never reach the file.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly LogLevel _minimumLevel;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new(StringComparer.Ordinal);
    private readonly object _writeLock = new();

    public FileLoggerProvider(string directory, LogLevel minimumLevel = LogLevel.Information)
    {
        _directory = directory;
        _minimumLevel = minimumLevel;

        try
        {
            Directory.CreateDirectory(_directory);
            PruneOldLogs();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Logging must never prevent the application from starting.
        }
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new FileLogger(this, name));

    public void Dispose() => _loggers.Clear();

    private void Write(LogLevel level, string category, string message, Exception? exception)
    {
        if (level < _minimumLevel)
        {
            return;
        }

        var builder = new StringBuilder();
        builder.Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture));
        builder.Append(" [").Append(Abbreviate(level)).Append("] ");
        builder.Append(ShortCategory(category)).Append(": ");
        builder.Append(LogSanitizer.Sanitize(message));

        if (exception is not null)
        {
            builder.AppendLine();
            builder.Append(LogSanitizer.Sanitize(exception.ToString()));
        }

        var line = builder.ToString();

        lock (_writeLock)
        {
            try
            {
                var path = Path.Combine(
                    _directory,
                    "dosboxxer-" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");

                File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Swallow: a failing log must not break the application.
            }
        }
    }

    private void PruneOldLogs()
    {
        var cutoff = DateTime.Now.AddDays(-14);

        foreach (var file in Directory.EnumerateFiles(_directory, "dosboxxer-*.log"))
        {
            try
            {
                if (File.GetLastWriteTime(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Ignore individual files we cannot remove.
            }
        }
    }

    private static string Abbreviate(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???",
    };

    private static string ShortCategory(string category)
    {
        var index = category.LastIndexOf('.');
        return index >= 0 && index < category.Length - 1 ? category[(index + 1)..] : category;
    }

    private sealed class FileLogger : ILogger
    {
        private readonly FileLoggerProvider _provider;
        private readonly string _category;

        public FileLogger(FileLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= _provider._minimumLevel;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            _provider.Write(logLevel, _category, formatter(state, exception), exception);
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
