using DosBoxxer.Core.Models;

namespace DosBoxxer.Core.Abstractions;

public interface IGameRepository
{
    Task<IReadOnlyList<Game>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<Game?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Returns the game whose launch file matches, used to prevent accidental duplicates.</summary>
    Task<Game?> FindByLaunchFileAsync(string launchFile, CancellationToken cancellationToken = default);

    Task AddAsync(Game game, CancellationToken cancellationToken = default);

    Task UpdateAsync(Game game, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Atomically records the outcome of a play session.</summary>
    Task RecordPlaySessionAsync(Guid id, DateTimeOffset startedAt, long durationSeconds, CancellationToken cancellationToken = default);

    Task SetFavoriteAsync(Guid id, bool isFavorite, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<GenreKey, int>> GetGenreCountsAsync(CancellationToken cancellationToken = default);
}
