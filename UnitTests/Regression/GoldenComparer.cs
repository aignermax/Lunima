namespace UnitTests.Regression;

/// <summary>
/// Compares a golden-gate run's measurements against the pinned reference:
/// sweep wavelengths must match (else the reference is stale), each output
/// pin's transmission must match within the manifest's relative tolerance,
/// and every authored truth-table row must hold.
/// </summary>
public static class GoldenComparer
{
    /// <summary>Absolute floor added to the relative tolerance (values at the −120 dB floor).</summary>
    public const double AbsoluteFloor = 1e-6;

    /// <summary>Compares a run's measurements against the pinned reference.</summary>
    /// <returns>Human-readable failures; empty when everything matches.</returns>
    public static List<string> Compare(
        GoldenManifestEntry entry, GoldenReference reference, GoldenGateResult actual)
    {
        var failures = new List<string>();
        if (!reference.WavelengthsNm.SequenceEqual(actual.WavelengthsNm))
        {
            failures.Add($"'{entry.Name}': sweep wavelengths changed from [{string.Join(", ", reference.WavelengthsNm)}] "
                + $"to [{string.Join(", ", actual.WavelengthsNm)}] — regenerate with "
                + $"{GoldenManifest.UpdateSwitchName}=1 and review the diff");
            return failures;
        }

        foreach (var (pinRef, series) in actual.Transmission)
        {
            if (!reference.Transmission.TryGetValue(pinRef, out var expected) || expected.Length != series.Length)
            {
                failures.Add($"'{entry.Name}': output '{pinRef}' missing from golden reference — "
                    + $"regenerate with {GoldenManifest.UpdateSwitchName}=1");
                continue;
            }
            for (int i = 0; i < series.Length; i++)
            {
                if (Deviates(series[i], expected[i], entry.Tolerance))
                    failures.Add($"'{entry.Name}' pin '{pinRef}' @ {actual.WavelengthsNm[i]} nm: "
                        + $"expected {expected[i]:G6}, actual {series[i]:G6} (tolerance {entry.Tolerance:G2} relative)");
            }
        }

        for (int row = 0; row < reference.TruthTable.Count; row++)
        {
            CompareTruthRow(entry, reference.TruthTable[row], row, actual, failures);
        }
        return failures;
    }

    private static void CompareTruthRow(
        GoldenManifestEntry entry, GoldenTruthRow truthRow, int rowIndex,
        GoldenGateResult actual, List<string> failures)
    {
        if (rowIndex >= actual.TruthActuals.Count)
        {
            failures.Add($"'{entry.Name}': truth row {rowIndex} was not evaluated");
            return;
        }
        foreach (var (pinRef, expectedValue) in truthRow.Expected)
        {
            actual.TruthActuals[rowIndex].TryGetValue(pinRef, out var actualValue);
            if (Deviates(actualValue, expectedValue, entry.Tolerance))
                failures.Add($"'{entry.Name}' truth row {rowIndex} pin '{pinRef}' @ {truthRow.WavelengthNm} nm: "
                    + $"expected {expectedValue:G6}, actual {actualValue:G6}");
        }
    }

    private static bool Deviates(double actualValue, double expectedValue, double tolerance) =>
        Math.Abs(actualValue - expectedValue)
            > tolerance * Math.Max(Math.Abs(expectedValue), AbsoluteFloor);
}
