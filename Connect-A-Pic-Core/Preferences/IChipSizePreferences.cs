namespace CAP_Core.Preferences;

/// <summary>Default physical dimensions for newly created chips.</summary>
public interface IChipSizePreferences
{
    double GetDefaultChipWidthMm();
    double GetDefaultChipHeightMm();
    void SetDefaultChipSize(double widthMm, double heightMm);
}
