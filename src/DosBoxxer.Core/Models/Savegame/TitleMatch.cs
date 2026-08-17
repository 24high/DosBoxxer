namespace DosBoxxer.Core.Models.Savegame;

/// <summary>Coarse confidence bucket derived from the numeric similarity score.</summary>
public enum MatchConfidence
{
    VeryLikely = 0,
    Likely = 1,
    Similar = 2,
    Weak = 3,
}

/// <summary>
/// A ranked candidate produced by the title matcher: a catalog entry together with a normalised
/// similarity score in <c>[0,1]</c> and a coarse confidence label. Never applied automatically —
/// the user always confirms the match.
/// </summary>
public sealed class TitleMatch
{
    public required CatalogEntry Catalog { get; init; }

    /// <summary>Combined similarity score in <c>[0,1]</c>. Higher is more similar.</summary>
    public required double Score { get; init; }

    public MatchConfidence Confidence { get; init; }

    public string Title => Catalog.Title;
}
