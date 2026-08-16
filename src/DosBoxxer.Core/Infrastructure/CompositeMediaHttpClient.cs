using DosBoxxer.Core.Abstractions;

namespace DosBoxxer.Core.Infrastructure;

/// <summary>
/// Routes a media URL to whichever provider client recognises its host. This lets the media
/// downloader stay independent of the active provider and even serve cached images from a
/// provider that is no longer the active one (each provider validates its own hosts).
/// </summary>
public sealed class CompositeMediaHttpClient : IMediaHttpClient
{
    private readonly IReadOnlyList<IMediaHttpClient> _clients;

    public CompositeMediaHttpClient(IEnumerable<IMediaHttpClient> clients)
    {
        // Guard against accidental self-registration causing infinite recursion.
        _clients = clients.Where(c => c is not CompositeMediaHttpClient).ToList();
    }

    public bool AcceptsUrl(string url) => _clients.Any(c => c.AcceptsUrl(url));

    public Task<Stream?> DownloadMediaAsync(string url, CancellationToken cancellationToken)
    {
        foreach (var client in _clients)
        {
            if (client.AcceptsUrl(url))
            {
                return client.DownloadMediaAsync(url, cancellationToken);
            }
        }

        return Task.FromResult<Stream?>(null);
    }
}
