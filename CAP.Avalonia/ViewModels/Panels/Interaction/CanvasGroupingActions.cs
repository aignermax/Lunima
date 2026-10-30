using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.ViewModels.Panels.Interaction;

/// <summary>Groups and ungroups the current selection through undoable die-aware commands.</summary>
internal sealed class CanvasGroupingActions
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly CommandManager _commandManager;
    private readonly CanvasDieInteractionRules _rules;
    private readonly Action<string> _status;
    private readonly bool _hasLibrary;

    public CanvasGroupingActions(DesignCanvasViewModel canvas, CommandManager commands,
        CanvasDieInteractionRules rules, bool hasLibrary, Action<string> status)
    {
        _canvas = canvas;
        _commandManager = commands;
        _rules = rules;
        _hasLibrary = hasLibrary;
        _status = status;
    }

    public void Create(ComponentViewModel? primary)
    {
        if (_rules.GroupBlockedReason(primary) is { } reason)
        {
            _status(reason);
            return;
        }
        var selectedComponents = _canvas.Selection.SelectedComponents.ToList();
        var dies = selectedComponents.Select(_canvas.Surface.DieOf).Distinct().ToList();
        if (dies.Count > 1 && dies.Any(d => d != null))
        {
            _status(Services.Localization.LocalizationService.Instance.Translate("Dies.Status.GroupOneDie"));
            return;
        }
        var cmd = _rules.OnDieSurface(selectedComponents, () => new CreateGroupCommand(_canvas, selectedComponents), "Create group");
        _commandManager.ExecuteCommand(cmd);
        _canvas.Selection.ClearSelection();

        if (_hasLibrary)
        {
            _status($"✓ Created group from {selectedComponents.Count} components and saved to 'Saved Groups' library");
        }
        else
        {
            _status($"Created group from {selectedComponents.Count} components (not saved to library)");
        }
    }

    public void Ungroup()
    {
        var selectedGroup = _canvas.Selection.SelectedComponents
            .Select(c => c.Component)
            .OfType<CAP_Core.Components.Core.ComponentGroup>()
            .FirstOrDefault();

        if (selectedGroup is { IsDie: true })
        {
            _status(Services.Localization.LocalizationService.Instance.Translate("Dies.Status.NotAGroup"));
            return;
        }
        if (selectedGroup != null)
        {
            var cmd = _rules.OnDieSurface(_canvas.Selection.SelectedComponents.ToList(),
                () => new UngroupCommand(_canvas, selectedGroup), "Ungroup");
            _commandManager.ExecuteCommand(cmd);
            _canvas.Selection.ClearSelection();
            _status($"Ungrouped: {selectedGroup.GroupName}");
        }
    }

}
