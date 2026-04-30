namespace CAP.Avalonia.Services;

/// <summary>
/// Shows a modal dialog letting the user reconcile imported S-parameter port
/// names with the component's pin names. Abstracted as an interface so the
/// import flow's tests can substitute a deterministic stub instead of having
/// to instantiate an Avalonia Window.
/// </summary>
public interface IPortMappingDialogService
{
    /// <summary>
    /// Displays the mapping dialog. Returns the user's chosen
    /// <c>importedName → componentPinName</c> mapping on OK, or <c>null</c>
    /// when the user cancels (in which case the caller should abort the
    /// import rather than fall back to defaults).
    /// </summary>
    /// <param name="componentDisplayName">Human-readable name shown in the dialog title.</param>
    /// <param name="importedNames">Port names as they appeared in the imported file, in matrix order.</param>
    /// <param name="componentPinNames">Pin names available on the component, in physical-pin order.</param>
    Task<IReadOnlyDictionary<string, string>?> ShowAsync(
        string componentDisplayName,
        IReadOnlyList<string> importedNames,
        IReadOnlyList<string> componentPinNames);
}
