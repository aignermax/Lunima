namespace CAP_Core.Preferences;

/// <summary>Persisted widths of the editor's two side panels.</summary>
public interface IPanelLayoutPreferences
{
    double GetLeftPanelWidth();
    void SetLeftPanelWidth(double width);
    double GetRightPanelWidth();
    void SetRightPanelWidth(double width);
}
