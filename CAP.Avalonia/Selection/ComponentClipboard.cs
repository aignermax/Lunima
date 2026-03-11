using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Selection;

/// <summary>
/// Stores copied component data for paste operations.
/// Captures component templates and internal connections between copied components.
/// </summary>
public class ComponentClipboard
{
    /// <summary>
    /// Offset applied to pasted components relative to original positions.
    /// </summary>
    private const double PasteOffsetMicrometers = 50.0;

    private List<ClipboardEntry> _entries = new();
    private List<ClipboardConnection> _internalConnections = new();

    /// <summary>
    /// Whether the clipboard has content to paste.
    /// </summary>
    public bool HasContent => _entries.Count > 0;

    /// <summary>
    /// Copies the given components and their internal connections.
    /// </summary>
    public void Copy(
        IReadOnlyList<ComponentViewModel> components,
        IEnumerable<WaveguideConnectionViewModel> allConnections)
    {
        _entries.Clear();
        _internalConnections.Clear();

        var componentSet = new HashSet<Component>(
            components.Select(c => c.Component));

        foreach (var comp in components)
        {
            _entries.Add(new ClipboardEntry(
                comp.Component,
                comp.TemplateName,
                comp.X,
                comp.Y));
        }

        // Capture internal connections (both endpoints inside selection)
        foreach (var conn in allConnections)
        {
            var startComp = conn.Connection.StartPin.ParentComponent;
            var endComp = conn.Connection.EndPin.ParentComponent;

            if (componentSet.Contains(startComp) && componentSet.Contains(endComp))
            {
                int startIdx = components.ToList().FindIndex(
                    c => c.Component == startComp);
                int endIdx = components.ToList().FindIndex(
                    c => c.Component == endComp);

                _internalConnections.Add(new ClipboardConnection(
                    startIdx,
                    conn.Connection.StartPin.Name,
                    endIdx,
                    conn.Connection.EndPin.Name));
            }
        }
    }

    /// <summary>
    /// Pastes copied components onto the canvas.
    /// If targetX/targetY are provided, pastes relative to cursor position.
    /// Otherwise pastes with fixed offset from original positions.
    /// Returns the list of newly created component ViewModels.
    /// </summary>
    public PasteResult? Paste(DesignCanvasViewModel canvas, double? targetX = null, double? targetY = null)
    {
        if (!HasContent) return null;

        var newComponents = new List<ComponentViewModel>();
        var clonedComps = new List<Component>();

        // Calculate offset: either from cursor position or fixed offset
        double offsetX, offsetY;
        if (targetX.HasValue && targetY.HasValue)
        {
            // Paste at cursor - calculate offset from first component's original position
            var firstEntry = _entries[0];
            offsetX = targetX.Value - firstEntry.OriginalX;
            offsetY = targetY.Value - firstEntry.OriginalY;
        }
        else
        {
            // Paste with fixed offset
            offsetX = PasteOffsetMicrometers;
            offsetY = PasteOffsetMicrometers;
        }

        foreach (var entry in _entries)
        {
            var cloned = (Component)entry.OriginalComponent.Clone();
            cloned.PhysicalX = entry.OriginalX + offsetX;
            cloned.PhysicalY = entry.OriginalY + offsetY;

            clonedComps.Add(cloned);
            var vm = canvas.AddComponent(cloned, entry.TemplateName);
            newComponents.Add(vm);
        }

        // Reconnect internal connections
        var newConnections = new List<WaveguideConnectionViewModel>();
        foreach (var conn in _internalConnections)
        {
            if (conn.StartComponentIndex >= newComponents.Count ||
                conn.EndComponentIndex >= newComponents.Count)
                continue;

            var startComp = newComponents[conn.StartComponentIndex].Component;
            var endComp = newComponents[conn.EndComponentIndex].Component;

            var startPin = startComp.PhysicalPins
                .FirstOrDefault(p => p.Name == conn.StartPinName);
            var endPin = endComp.PhysicalPins
                .FirstOrDefault(p => p.Name == conn.EndPinName);

            if (startPin != null && endPin != null)
            {
                var connVm = canvas.ConnectPins(startPin, endPin);
                if (connVm != null)
                    newConnections.Add(connVm);
            }
        }

        return new PasteResult(newComponents, newConnections);
    }

    /// <summary>
    /// Data stored for a single copied component.
    /// </summary>
    private sealed record ClipboardEntry(
        Component OriginalComponent,
        string? TemplateName,
        double OriginalX,
        double OriginalY);

    /// <summary>
    /// Data stored for an internal connection between copied components.
    /// </summary>
    private sealed record ClipboardConnection(
        int StartComponentIndex,
        string StartPinName,
        int EndComponentIndex,
        string EndPinName);
}

/// <summary>
/// Result of a paste operation containing newly created components and connections.
/// </summary>
public sealed record PasteResult(
    List<ComponentViewModel> Components,
    List<WaveguideConnectionViewModel> Connections);
