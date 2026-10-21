using CAP_DataAccess.Import.Gds;

namespace CAP.Avalonia.Services.GdsImport;

/// <summary>A concrete source placement, before application-coordinate conversion.</summary>
public sealed record GdsReviewCellPlacement(string CellName, GdsTransform Transform);
