namespace CAP_Core.Export.FoundryDrc;

/// <summary>Runs foundry DRC decks on GDS files (implemented with KLayout).</summary>
public interface IFoundryDrcRunner
{
    /// <summary>The decks available without a path (name → deck file).</summary>
    IReadOnlyDictionary<string, string> ShippedDecks();

    /// <summary>Runs <paramref name="deckPath"/> on <paramref name="gdsPath"/>.</summary>
    Task<FoundryDrcResult> RunAsync(string gdsPath, string deckPath, CancellationToken cancellationToken = default);
}
