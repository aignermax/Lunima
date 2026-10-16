using CAP.Avalonia.Commands.Dies;
using CAP.Avalonia.Services.Localization;
using CAP_Core.Components.Core;
using CAP_Core.Components.Process;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Dies;

public partial class DiePropertiesViewModel
{
    /// <summary>Live library source lookup, shared with placement enforcement.</summary>
    public Func<Component, string?>? ResolvePdkSource { get; set; }
    /// <summary>Live target-process membership, including process-agnostic PDKs.</summary>
    public Func<ActiveProcessSelection, IReadOnlyCollection<string>>? ResolveAllowedPdkNames { get; set; }
    [ObservableProperty] private bool _hasProcessPreview;
    [ObservableProperty] private string _processPreviewText = "";
    private ChangeDieProcessCommand? _pendingProcess;
    private ComponentGroup? _pendingDie;

    private void PreviewProcessChange(DieProcessOption? option)
    {
        if (_loading || !IsEditable || option == null || Die is not { } die) return;
        if (SameProcess(option.Selection, die.ProcessBinding)) return;
        var roots = _canvas.ChipDie.IsProxy(die)
            ? _canvas.Components.Select(c => c.Component).ToList() : die.ChildComponents.ToList();
        var leaves = roots.SelectMany<Component, Component>(c => c is ComponentGroup g ? g.GetAllComponentsRecursive() : new[] { c })
            .Where(c => c is not ComponentGroup).ToList();
        var target = option.Selection;
        var after = DieState.Of(die) with { Process = target, Definition = die.Die! with { EnabledPdkNames = null } };
        if (leaves.Count == 0)
        {
            Commit(_ => after);
            return;
        }
        var allowed = (ResolveAllowedPdkNames?.Invoke(target) ?? target.MemberPdkNames.ToArray())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removed = leaves.Where(c => !target.IsPlayground &&
            (_canvas.ViewModelOf(c)?.TemplatePdkSource ?? ResolvePdkSource?.Invoke(c)) is { Length: > 0 } source &&
            !allowed.Contains(source)).ToList();
        if (!_canvas.ChipDie.IsProxy(die) && UpdateDieCommand.TryCreate(_canvas, die, after, out var refusal) == null)
        {
            StatusText = refusal ?? "";
            Reload();
            return;
        }
        var pending = new ChangeDieProcessCommand(_canvas, die, after, removed);
        Reload();
        _pendingProcess = pending;
        _pendingDie = die;
        ProcessPreviewText = string.Format(LocalizationService.Instance.Translate("Dies.ProcessChange.Preview"),
            target.DisplayName, removed.Count, _pendingProcess.AffectedConnectionCount) +
            (removed.Count == 0 ? "" : "\n" + string.Join("\n", removed.Select(c => "• " + c.Identifier))) +
            (pending.AffectedConnectionCount == 0 ? "" : "\n" + string.Join("\n", pending.AffectedConnections.Select(c => "• " + c)));
        HasProcessPreview = true;
    }

    [RelayCommand]
    private void CancelProcessChange()
    {
        ClearProcessPreview();
        Reload();
    }

    [RelayCommand]
    private void ConfirmProcessChange()
    {
        if (!IsEditable || _pendingProcess == null || !ReferenceEquals(Die, _pendingDie)) return;
        var command = _pendingProcess;
        ClearProcessPreview();
        _commands.ExecuteCommand(command);
        var vm = _canvas.Components.FirstOrDefault(c => ReferenceEquals(c.Component, command.ResultDie));
        if (vm != null)
        {
            _canvas.Selection.SelectSingle(vm);
            _canvas.SelectedComponent = vm;
        }
        Show(command.ResultDie);
    }

    private void ClearProcessPreview()
    {
        _pendingProcess = null;
        _pendingDie = null;
        HasProcessPreview = false;
        ProcessPreviewText = "";
    }
}
