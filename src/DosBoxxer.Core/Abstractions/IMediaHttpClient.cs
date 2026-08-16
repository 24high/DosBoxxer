namespace DosBoxxer.Core.Abstractions;

/// <summary>
/// Downloads a media file (cover / screenshot) from the active metadata provider's CDN.
///
/// Each provider validates the URL against its own allowed hosts before fetching, so a
/// manipulated API response can never make the launcher download from an arbitrary host.
/// This abstraction lets <c>IMediaDownloader</c> stay independent of which provider is active.
/// </summary>
public interface IMediaHttpClient
{
    /// <summary>
    /// True when this client recognises the URL's host as one of its own CDNs. Used by the
    /// composite client to route a media URL to the provider that produced it.
    /// </summary>
    bool AcceptsUrl(string url);

    /// <summary>
    /// Returns a seekable stream with the downloaded bytes, or <c>null</c> when the URL is not
    /// from an allowed host or the request failed. The caller owns and disposes the stream.
    /// </summary>
    Task<Stream?> DownloadMediaAsync(string url, CancellationToken cancellationToken);
}
