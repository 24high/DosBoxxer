using System.Data;
using System.Globalization;
using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Infrastructure.Database;
using DosBoxxer.Core.Models;
using DosBoxxer.Core.Models.Savegame;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace DosBoxxer.Core.Infrastructure.Repositories;

/// <summary>
/// SQLite backed game repository. All statements are parameterised; no SQL is built from user
/// input. Writes that touch several tables run inside a transaction.
/// </summary>
public sealed class GameRepository : IGameRepository
{
    private const string SelectGameColumns = """
        SELECT id, title, sort_title, original_title, game_directory, launch_file,
               relative_launch_file, description, publisher, developer, release_year,
               platform, players, screenscraper_id, cover_image_path, emulator_profile,
               date_added, last_played, play_count, total_play_time_seconds,
               metadata_last_updated, is_favorite, manual_fields
        FROM games
        """;

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<GameRepository> _logger;

    public GameRepository(IDbConnectionFactory connectionFactory, ILogger<GameRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Game>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);

        var games = new Dictionary<Guid, Game>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = SelectGameColumns + " ORDER BY sort_title;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var game = ReadGame(reader);
                games[game.Id] = game;
            }
        }

        if (games.Count == 0)
        {
            return Array.Empty<Game>();
        }

        await LoadGenresAsync(connection, games, cancellationToken).ConfigureAwait(false);
        await LoadScreenshotsAsync(connection, games, cancellationToken).ConfigureAwait(false);
        await LoadDosBoxSettingsAsync(connection, games, cancellationToken).ConfigureAwait(false);
        await LoadSavegameConfigAsync(connection, games, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Loaded {Count} game(s) from the library", games.Count);
        return games.Values.ToList();
    }

    public async Task<Game?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);

        Game? game = null;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = SelectGameColumns + " WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id.ToString("D"));

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                game = ReadGame(reader);
            }
        }

        if (game is null)
        {
            return null;
        }

        var map = new Dictionary<Guid, Game> { [game.Id] = game };
        await LoadGenresAsync(connection, map, cancellationToken).ConfigureAwait(false);
        await LoadScreenshotsAsync(connection, map, cancellationToken).ConfigureAwait(false);
        await LoadDosBoxSettingsAsync(connection, map, cancellationToken).ConfigureAwait(false);
        await LoadSavegameConfigAsync(connection, map, cancellationToken).ConfigureAwait(false);

        return game;
    }

    public async Task<Game?> FindByLaunchFileAsync(string launchFile, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(launchFile))
        {
            return null;
        }

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM games WHERE launch_file = $path LIMIT 1;";
        command.Parameters.AddWithValue("$path", launchFile);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (result is not string text || !Guid.TryParse(text, out var id))
        {
            return null;
        }

        return await GetAsync(id, cancellationToken).ConfigureAwait(false);
    }

    public async Task AddAsync(Game game, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(game);

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO games (
                    id, title, sort_title, original_title, game_directory, launch_file,
                    relative_launch_file, description, publisher, developer, release_year,
                    platform, players, screenscraper_id, cover_image_path, emulator_profile,
                    date_added, last_played, play_count, total_play_time_seconds,
                    metadata_last_updated, is_favorite, manual_fields)
                VALUES (
                    $id, $title, $sortTitle, $originalTitle, $gameDirectory, $launchFile,
                    $relativeLaunchFile, $description, $publisher, $developer, $releaseYear,
                    $platform, $players, $screenScraperId, $coverImagePath, $emulatorProfile,
                    $dateAdded, $lastPlayed, $playCount, $totalPlayTime,
                    $metadataLastUpdated, $isFavorite, $manualFields);
                """;
            BindGame(command, game);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await WriteGenresAsync(connection, (SqliteTransaction)transaction, game, cancellationToken).ConfigureAwait(false);
        await WriteScreenshotsAsync(connection, (SqliteTransaction)transaction, game, cancellationToken).ConfigureAwait(false);
        await WriteDosBoxSettingsAsync(connection, (SqliteTransaction)transaction, game, cancellationToken).ConfigureAwait(false);
        await WriteSavegameConfigAsync(connection, (SqliteTransaction)transaction, game, cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Added game '{Title}' ({Id}) to the library", game.Title, game.Id);
    }

    public async Task UpdateAsync(Game game, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(game);

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                UPDATE games SET
                    title = $title,
                    sort_title = $sortTitle,
                    original_title = $originalTitle,
                    game_directory = $gameDirectory,
                    launch_file = $launchFile,
                    relative_launch_file = $relativeLaunchFile,
                    description = $description,
                    publisher = $publisher,
                    developer = $developer,
                    release_year = $releaseYear,
                    platform = $platform,
                    players = $players,
                    screenscraper_id = $screenScraperId,
                    cover_image_path = $coverImagePath,
                    emulator_profile = $emulatorProfile,
                    date_added = $dateAdded,
                    last_played = $lastPlayed,
                    play_count = $playCount,
                    total_play_time_seconds = $totalPlayTime,
                    metadata_last_updated = $metadataLastUpdated,
                    is_favorite = $isFavorite,
                    manual_fields = $manualFields
                WHERE id = $id;
                """;
            BindGame(command, game);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await WriteGenresAsync(connection, (SqliteTransaction)transaction, game, cancellationToken).ConfigureAwait(false);
        await WriteScreenshotsAsync(connection, (SqliteTransaction)transaction, game, cancellationToken).ConfigureAwait(false);
        await WriteDosBoxSettingsAsync(connection, (SqliteTransaction)transaction, game, cancellationToken).ConfigureAwait(false);
        await WriteSavegameConfigAsync(connection, (SqliteTransaction)transaction, game, cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Updated game '{Title}' ({Id})", game.Title, game.Id);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM games WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Removed game {Id} from the library", id);
    }

    public async Task RecordPlaySessionAsync(
        Guid id,
        DateTimeOffset startedAt,
        long durationSeconds,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE games SET
                last_played = $lastPlayed,
                play_count = play_count + 1,
                total_play_time_seconds = total_play_time_seconds + $duration
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$lastPlayed", FormatDate(startedAt));
        command.Parameters.AddWithValue("$duration", Math.Max(0, durationSeconds));
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetFavoriteAsync(Guid id, bool isFavorite, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE games SET is_favorite = $value WHERE id = $id;";
        command.Parameters.AddWithValue("$value", isFavorite ? 1 : 0);
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<GenreKey, int>> GetGenreCountsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT genre_id, COUNT(*) FROM game_genres GROUP BY genre_id;";

        var result = new Dictionary<GenreKey, int>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var genre = (GenreKey)reader.GetInt32(0);
            result[genre] = reader.GetInt32(1);
        }

        return result;
    }

    // ---- mapping helpers ----------------------------------------------------------------

    private static Game ReadGame(IDataRecord record) => new()
    {
        Id = Guid.Parse(record.GetString(0)),
        Title = record.GetString(1),
        SortTitle = record.GetString(2),
        OriginalTitle = GetNullableString(record, 3),
        GameDirectory = record.GetString(4),
        LaunchFile = record.GetString(5),
        RelativeLaunchFile = record.GetString(6),
        Description = GetNullableString(record, 7),
        Publisher = GetNullableString(record, 8),
        Developer = GetNullableString(record, 9),
        ReleaseYear = record.IsDBNull(10) ? null : record.GetInt32(10),
        Platform = record.GetString(11),
        Players = GetNullableString(record, 12),
        ScreenScraperId = GetNullableString(record, 13),
        CoverImagePath = GetNullableString(record, 14),
        EmulatorProfile = record.GetString(15),
        DateAdded = ParseDate(record.GetString(16)) ?? DateTimeOffset.UtcNow,
        LastPlayed = record.IsDBNull(17) ? null : ParseDate(record.GetString(17)),
        PlayCount = record.GetInt32(18),
        TotalPlayTimeSeconds = record.GetInt64(19),
        MetadataLastUpdated = record.IsDBNull(20) ? null : ParseDate(record.GetString(20)),
        IsFavorite = record.GetInt32(21) != 0,
        ManualFields = (GameField)record.GetInt32(22),
    };

    private static void BindGame(SqliteCommand command, Game game)
    {
        command.Parameters.AddWithValue("$id", game.Id.ToString("D"));
        command.Parameters.AddWithValue("$title", game.Title);
        command.Parameters.AddWithValue("$sortTitle", game.SortTitle);
        command.Parameters.AddWithValue("$originalTitle", (object?)game.OriginalTitle ?? DBNull.Value);
        command.Parameters.AddWithValue("$gameDirectory", game.GameDirectory);
        command.Parameters.AddWithValue("$launchFile", game.LaunchFile);
        command.Parameters.AddWithValue("$relativeLaunchFile", game.RelativeLaunchFile);
        command.Parameters.AddWithValue("$description", (object?)game.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("$publisher", (object?)game.Publisher ?? DBNull.Value);
        command.Parameters.AddWithValue("$developer", (object?)game.Developer ?? DBNull.Value);
        command.Parameters.AddWithValue("$releaseYear", (object?)game.ReleaseYear ?? DBNull.Value);
        command.Parameters.AddWithValue("$platform", game.Platform);
        command.Parameters.AddWithValue("$players", (object?)game.Players ?? DBNull.Value);
        command.Parameters.AddWithValue("$screenScraperId", (object?)game.ScreenScraperId ?? DBNull.Value);
        command.Parameters.AddWithValue("$coverImagePath", (object?)game.CoverImagePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$emulatorProfile", game.EmulatorProfile);
        command.Parameters.AddWithValue("$dateAdded", FormatDate(game.DateAdded));
        command.Parameters.AddWithValue("$lastPlayed", game.LastPlayed.HasValue ? FormatDate(game.LastPlayed.Value) : DBNull.Value);
        command.Parameters.AddWithValue("$playCount", game.PlayCount);
        command.Parameters.AddWithValue("$totalPlayTime", game.TotalPlayTimeSeconds);
        command.Parameters.AddWithValue("$metadataLastUpdated", game.MetadataLastUpdated.HasValue ? FormatDate(game.MetadataLastUpdated.Value) : DBNull.Value);
        command.Parameters.AddWithValue("$isFavorite", game.IsFavorite ? 1 : 0);
        command.Parameters.AddWithValue("$manualFields", (int)game.ManualFields);
    }

    private static async Task WriteGenresAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Game game,
        CancellationToken cancellationToken)
    {
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM game_genres WHERE game_id = $id;";
            delete.Parameters.AddWithValue("$id", game.Id.ToString("D"));
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var genre in game.Genres.Distinct())
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT OR IGNORE INTO game_genres (game_id, genre_id) VALUES ($gameId, $genreId);";
            insert.Parameters.AddWithValue("$gameId", game.Id.ToString("D"));
            insert.Parameters.AddWithValue("$genreId", (int)genre);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task WriteScreenshotsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Game game,
        CancellationToken cancellationToken)
    {
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM screenshots WHERE game_id = $id;";
            delete.Parameters.AddWithValue("$id", game.Id.ToString("D"));
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var order = 0;
        foreach (var screenshot in game.Screenshots)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO screenshots (id, game_id, local_path, remote_url, sort_order)
                VALUES ($id, $gameId, $localPath, $remoteUrl, $sortOrder);
                """;
            insert.Parameters.AddWithValue("$id", screenshot.Id.ToString("D"));
            insert.Parameters.AddWithValue("$gameId", game.Id.ToString("D"));
            insert.Parameters.AddWithValue("$localPath", screenshot.LocalPath);
            insert.Parameters.AddWithValue("$remoteUrl", (object?)screenshot.RemoteUrl ?? DBNull.Value);
            insert.Parameters.AddWithValue("$sortOrder", order++);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task WriteDosBoxSettingsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Game game,
        CancellationToken cancellationToken)
    {
        var settings = game.DosBoxSettings;

        if (!settings.HasAnyOverride)
        {
            await using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM game_dosbox_settings WHERE game_id = $id;";
            delete.Parameters.AddWithValue("$id", game.Id.ToString("D"));
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO game_dosbox_settings (
                game_id, cycles, core, machine, memsize, scaler, aspect, fullscreen,
                output, mixer_rate, sblaster_type, pc_speaker, additional_config_lines,
                pre_launch_commands)
            VALUES (
                $gameId, $cycles, $core, $machine, $memsize, $scaler, $aspect, $fullscreen,
                $output, $mixerRate, $sblasterType, $pcSpeaker, $additionalLines,
                $preLaunch)
            ON CONFLICT (game_id) DO UPDATE SET
                cycles = excluded.cycles,
                core = excluded.core,
                machine = excluded.machine,
                memsize = excluded.memsize,
                scaler = excluded.scaler,
                aspect = excluded.aspect,
                fullscreen = excluded.fullscreen,
                output = excluded.output,
                mixer_rate = excluded.mixer_rate,
                sblaster_type = excluded.sblaster_type,
                pc_speaker = excluded.pc_speaker,
                additional_config_lines = excluded.additional_config_lines,
                pre_launch_commands = excluded.pre_launch_commands;
            """;

        command.Parameters.AddWithValue("$gameId", game.Id.ToString("D"));
        command.Parameters.AddWithValue("$cycles", (object?)settings.Cycles ?? DBNull.Value);
        command.Parameters.AddWithValue("$core", (object?)settings.Core ?? DBNull.Value);
        command.Parameters.AddWithValue("$machine", (object?)settings.Machine ?? DBNull.Value);
        command.Parameters.AddWithValue("$memsize", (object?)settings.MemSize ?? DBNull.Value);
        command.Parameters.AddWithValue("$scaler", (object?)settings.Scaler ?? DBNull.Value);
        command.Parameters.AddWithValue("$aspect", settings.Aspect.HasValue ? (settings.Aspect.Value ? 1 : 0) : DBNull.Value);
        command.Parameters.AddWithValue("$fullscreen", settings.Fullscreen.HasValue ? (settings.Fullscreen.Value ? 1 : 0) : DBNull.Value);
        command.Parameters.AddWithValue("$output", (object?)settings.Output ?? DBNull.Value);
        command.Parameters.AddWithValue("$mixerRate", (object?)settings.MixerRate ?? DBNull.Value);
        command.Parameters.AddWithValue("$sblasterType", (object?)settings.SoundBlasterType ?? DBNull.Value);
        command.Parameters.AddWithValue("$pcSpeaker", settings.PcSpeaker.HasValue ? (settings.PcSpeaker.Value ? 1 : 0) : DBNull.Value);
        command.Parameters.AddWithValue("$additionalLines", (object?)settings.AdditionalConfigLines ?? DBNull.Value);
        command.Parameters.AddWithValue("$preLaunch", (object?)settings.PreLaunchCommands ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteSavegameConfigAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Game game,
        CancellationToken cancellationToken)
    {
        var config = game.SavegameConfig;

        await using (var deleteEntries = connection.CreateCommand())
        {
            deleteEntries.Transaction = transaction;
            deleteEntries.CommandText = "DELETE FROM game_savegame_entries WHERE game_id = $id;";
            deleteEntries.Parameters.AddWithValue("$id", game.Id.ToString("D"));
            await deleteEntries.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        // A never-configured game keeps no row at all, matching the DosBox-settings convention.
        if (!config.IsConfigured && config.Entries.Count == 0 && config.MatchedCatalogTitle is null)
        {
            await using var deleteConfig = connection.CreateCommand();
            deleteConfig.Transaction = transaction;
            deleteConfig.CommandText = "DELETE FROM game_savegame_config WHERE game_id = $id;";
            deleteConfig.Parameters.AddWithValue("$id", game.Id.ToString("D"));
            await deleteConfig.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO game_savegame_config (game_id, is_configured, matched_catalog_title, is_manual)
                VALUES ($id, $configured, $matched, $manual)
                ON CONFLICT (game_id) DO UPDATE SET
                    is_configured = excluded.is_configured,
                    matched_catalog_title = excluded.matched_catalog_title,
                    is_manual = excluded.is_manual;
                """;
            command.Parameters.AddWithValue("$id", game.Id.ToString("D"));
            command.Parameters.AddWithValue("$configured", config.IsConfigured ? 1 : 0);
            command.Parameters.AddWithValue("$matched", (object?)config.MatchedCatalogTitle ?? DBNull.Value);
            command.Parameters.AddWithValue("$manual", config.IsManual ? 1 : 0);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var order = 0;
        foreach (var entry in config.Entries)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO game_savegame_entries (game_id, sort_order, pattern, kind)
                VALUES ($id, $order, $pattern, $kind);
                """;
            insert.Parameters.AddWithValue("$id", game.Id.ToString("D"));
            insert.Parameters.AddWithValue("$order", order++);
            insert.Parameters.AddWithValue("$pattern", entry.RelativePattern);
            insert.Parameters.AddWithValue("$kind", (int)entry.Kind);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task LoadSavegameConfigAsync(
        SqliteConnection connection,
        Dictionary<Guid, Game> games,
        CancellationToken cancellationToken)
    {
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT game_id, is_configured, matched_catalog_title, is_manual FROM game_savegame_config;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!Guid.TryParse(reader.GetString(0), out var gameId) || !games.TryGetValue(gameId, out var game))
                {
                    continue;
                }

                game.SavegameConfig = new SavegameConfig
                {
                    GameId = gameId,
                    IsConfigured = reader.GetInt32(1) != 0,
                    MatchedCatalogTitle = GetNullableString(reader, 2),
                    IsManual = reader.GetInt32(3) != 0,
                };
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT game_id, pattern, kind FROM game_savegame_entries ORDER BY game_id, sort_order;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!Guid.TryParse(reader.GetString(0), out var gameId) || !games.TryGetValue(gameId, out var game))
                {
                    continue;
                }

                game.SavegameConfig.Entries.Add(new SavegameEntry(
                    reader.GetString(1),
                    (SavegameEntryKind)reader.GetInt32(2)));
            }
        }
    }

    private static async Task LoadGenresAsync(
        SqliteConnection connection,
        Dictionary<Guid, Game> games,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT game_id, genre_id FROM game_genres;";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (Guid.TryParse(reader.GetString(0), out var gameId) && games.TryGetValue(gameId, out var game))
            {
                game.Genres.Add((GenreKey)reader.GetInt32(1));
            }
        }
    }

    private static async Task LoadScreenshotsAsync(
        SqliteConnection connection,
        Dictionary<Guid, Game> games,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, game_id, local_path, remote_url, sort_order FROM screenshots ORDER BY game_id, sort_order;";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!Guid.TryParse(reader.GetString(1), out var gameId) || !games.TryGetValue(gameId, out var game))
            {
                continue;
            }

            game.Screenshots.Add(new Screenshot
            {
                Id = Guid.Parse(reader.GetString(0)),
                GameId = gameId,
                LocalPath = reader.GetString(2),
                RemoteUrl = reader.IsDBNull(3) ? null : reader.GetString(3),
                SortOrder = reader.GetInt32(4),
            });
        }
    }

    private static async Task LoadDosBoxSettingsAsync(
        SqliteConnection connection,
        Dictionary<Guid, Game> games,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT game_id, cycles, core, machine, memsize, scaler, aspect, fullscreen,
                   output, mixer_rate, sblaster_type, pc_speaker, additional_config_lines,
                   pre_launch_commands
            FROM game_dosbox_settings;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!Guid.TryParse(reader.GetString(0), out var gameId) || !games.TryGetValue(gameId, out var game))
            {
                continue;
            }

            game.DosBoxSettings = new GameDosBoxSettings
            {
                GameId = gameId,
                Cycles = GetNullableString(reader, 1),
                Core = GetNullableString(reader, 2),
                Machine = GetNullableString(reader, 3),
                MemSize = GetNullableString(reader, 4),
                Scaler = GetNullableString(reader, 5),
                Aspect = reader.IsDBNull(6) ? null : reader.GetInt32(6) != 0,
                Fullscreen = reader.IsDBNull(7) ? null : reader.GetInt32(7) != 0,
                Output = GetNullableString(reader, 8),
                MixerRate = GetNullableString(reader, 9),
                SoundBlasterType = GetNullableString(reader, 10),
                PcSpeaker = reader.IsDBNull(11) ? null : reader.GetInt32(11) != 0,
                AdditionalConfigLines = GetNullableString(reader, 12),
                PreLaunchCommands = GetNullableString(reader, 13),
            };
        }
    }

    private static string? GetNullableString(IDataRecord record, int index) =>
        record.IsDBNull(index) ? null : record.GetString(index);

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
}
