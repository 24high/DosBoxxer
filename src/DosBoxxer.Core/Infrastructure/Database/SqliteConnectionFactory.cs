using DosBoxxer.Core.Abstractions;
using Microsoft.Data.Sqlite;

namespace DosBoxxer.Core.Infrastructure.Database;

public interface IDbConnectionFactory
{
    /// <summary>Opens a new connection with foreign keys enabled. The caller owns it.</summary>
    Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default);
}

public sealed class SqliteConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory(IAppPaths paths)
    {
        Directory.CreateDirectory(paths.DatabaseDirectory);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabaseFile,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
            ForeignKeys = true,
        }.ToString();
    }

    /// <summary>Test constructor for an in-memory or explicitly located database.</summary>
    public SqliteConnectionFactory(string connectionString) => _connectionString = connectionString;

    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL; PRAGMA busy_timeout = 5000;";
        await pragma.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        return connection;
    }
}
