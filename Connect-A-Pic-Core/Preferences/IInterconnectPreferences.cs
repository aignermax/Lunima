using CAP_Core.Routing.InterconnectRouting;

namespace CAP_Core.Preferences;

/// <summary>Persisted waveguide width, bend radius and export layer defaults.</summary>
public interface IInterconnectPreferences
{
    InterconnectSettings GetInterconnectSettings();
    void SetInterconnectSettings(InterconnectSettings settings);
}
