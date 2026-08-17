using DosBoxxer.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace DosBoxxer.Core.Infrastructure.Database;

public interface IDatabaseInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Creates and migrates the SQLite schema. Migrations are driven by <c>PRAGMA user_version</c>:
/// each migration bumps the version by one, so upgrading an existing database only ever runs
/// the steps it has not seen yet.
/// </summary>
public sealed class DatabaseInitializer : IDatabaseInitializer
{
    /// <summary>Increment together with a new entry in <see cref="Migrations"/>.</summary>
    public const int CurrentSchemaVersion = 2;

    private static readonly string[] Migrations =
    {
        // ---- version 1 : initial schema ------------------------------------------------
        """
        CREATE TABLE genres (
            id      INTEGER PRIMARY KEY,
            key     TEXT NOT NULL UNIQUE
        );

        CREATE TABLE games (
            id                        TEXT PRIMARY KEY,
            title                     TEXT NOT NULL,
            sort_title                TEXT NOT NULL,
            original_title            TEXT NULL,
            game_directory            TEXT NOT NULL,
            launch_file               TEXT NOT NULL,
            relative_launch_file      TEXT NOT NULL,
            description               TEXT NULL,
            publisher                 TEXT NULL,
            developer                 TEXT NULL,
            release_year              INTEGER NULL,
            platform                  TEXT NOT NULL DEFAULT 'PC (DOS)',
            players                   TEXT NULL,
            screenscraper_id          TEXT NULL,
            cover_image_path          TEXT NULL,
            emulator_profile          TEXT NOT NULL DEFAULT 'dosbox',
            date_added                TEXT NOT NULL,
            last_played               TEXT NULL,
            play_count                INTEGER NOT NULL DEFAULT 0,
            total_play_time_seconds   INTEGER NOT NULL DEFAULT 0,
            metadata_last_updated     TEXT NULL,
            is_favorite               INTEGER NOT NULL DEFAULT 0,
            manual_fields             INTEGER NOT NULL DEFAULT 0
        );

        CREATE INDEX ix_games_sort_title ON games (sort_title);
        CREATE INDEX ix_games_last_played ON games (last_played);
        CREATE UNIQUE INDEX ux_games_launch_file ON games (launch_file);

        CREATE TABLE game_genres (
            game_id   TEXT NOT NULL REFERENCES games (id) ON DELETE CASCADE,
            genre_id  INTEGER NOT NULL REFERENCES genres (id) ON DELETE CASCADE,
            PRIMARY KEY (game_id, genre_id)
        );

        CREATE INDEX ix_game_genres_genre ON game_genres (genre_id);

        CREATE TABLE screenshots (
            id          TEXT PRIMARY KEY,
            game_id     TEXT NOT NULL REFERENCES games (id) ON DELETE CASCADE,
            local_path  TEXT NOT NULL,
            remote_url  TEXT NULL,
            sort_order  INTEGER NOT NULL DEFAULT 0
        );

        CREATE INDEX ix_screenshots_game ON screenshots (game_id, sort_order);

        CREATE TABLE game_dosbox_settings (
            game_id                 TEXT PRIMARY KEY REFERENCES games (id) ON DELETE CASCADE,
            cycles                  TEXT NULL,
            core                    TEXT NULL,
            machine                 TEXT NULL,
            memsize                 TEXT NULL,
            scaler                  TEXT NULL,
            aspect                  INTEGER NULL,
            fullscreen              INTEGER NULL,
            output                  TEXT NULL,
            mixer_rate              TEXT NULL,
            sblaster_type           TEXT NULL,
            pc_speaker              INTEGER NULL,
            additional_config_lines TEXT NULL,
            pre_launch_commands     TEXT NULL
        );
        """,

        // ---- version 2 : savegame / cloud-save configuration --------------------------
        """
        CREATE TABLE game_savegame_config (
            game_id                 TEXT PRIMARY KEY REFERENCES games (id) ON DELETE CASCADE,
            is_configured           INTEGER NOT NULL DEFAULT 0,
            matched_catalog_title   TEXT NULL,
            is_manual               INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE game_savegame_entries (
            game_id     TEXT NOT NULL REFERENCES games (id) ON DELETE CASCADE,
            sort_order  INTEGER NOT NULL,
            pattern     TEXT NOT NULL,
            kind        INTEGER NOT NULL DEFAULT 0,
            PRIMARY KEY (game_id, sort_order)
        );
        """,
    };

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(IDbConnectionFactory connectionFactory, ILogger<DatabaseInitializer> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);

        var version = await GetUserVersionAsync(connection, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Database schema version {Version} (expected {Expected})", version, CurrentSchemaVersion);

        if (version > CurrentSchemaVersion)
        {
            _logger.LogWarning(
                "The database was created by a newer version of DosBoxxer (schema {Version}); no migration is applied",
                version);
            return;
        }

        for (var target = version; target < Migrations.Length; target++)
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = (SqliteTransaction)transaction;
                command.CommandText = Migrations[target];
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var versionCommand = connection.CreateCommand())
            {
                versionCommand.Transaction = (SqliteTransaction)transaction;

                // PRAGMA does not accept parameters; the value is an int constant we control.
                versionCommand.CommandText = $"PRAGMA user_version = {target + 1};";
                await versionCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Applied database migration to version {Version}", target + 1);
        }

        await SeedGenresAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> GetUserVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is null ? 0 : Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task SeedGenresAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        foreach (var genre in GenreKeys.All)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = "INSERT OR IGNORE INTO genres (id, key) VALUES ($id, $key);";
            command.Parameters.AddWithValue("$id", (int)genre);
            command.Parameters.AddWithValue("$key", genre.ToString());
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
