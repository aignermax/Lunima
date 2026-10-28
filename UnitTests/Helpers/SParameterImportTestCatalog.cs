using CAP_DataAccess.Import;

namespace UnitTests.Helpers;

/// <summary>Explicit parser composition for fixtures that exercise file imports.</summary>
internal static class SParameterImportTestCatalog
{
    internal static ISParameterImportCatalog Create(IReadOnlyList<ISParameterImporter>? importers = null) =>
        new SParameterImportCatalog(importers ?? [new LumericalSParameterImporter(), new TouchstoneImporter()]);
}
