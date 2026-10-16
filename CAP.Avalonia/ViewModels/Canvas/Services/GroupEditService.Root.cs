namespace CAP.Avalonia.ViewModels.Canvas.Services;

public partial class GroupEditService
{
    /// <summary>The design root, even while a group's contents occupy the canvas.</summary>
    public IEnumerable<ComponentViewModel> RootComponents =>
        _rootCanvasBackup is { } backup ? backup.Components : _components;

    /// <summary>The root's wires, excluding the currently edited group's internal wires.</summary>
    public IEnumerable<WaveguideConnectionViewModel> RootConnections =>
        _rootCanvasBackup is { } backup ? backup.Connections : _connections;
}
