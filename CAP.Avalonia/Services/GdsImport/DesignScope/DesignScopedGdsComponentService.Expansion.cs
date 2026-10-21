using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;

namespace CAP.Avalonia.Services.GdsImport.DesignScope;

public sealed partial class DesignScopedGdsComponentService
{
    /// <summary>Adds newly exposed source children without replacing existing templates.</summary>
    internal void RegisterExpandedChildren(DesignScopedGdsSet set, IReadOnlyList<PdkComponentDraft> drafts)
    {
        if (!_sets.Contains(set))
            throw new InvalidOperationException("The source design was closed. Select the imported component again.");
        var additions = drafts.Where(d => !set.Drafts.Any(e => e.Name == d.Name)).ToList();
        if (additions.Count == 0) return;
        var runtime = SubstituteForRuntime(set.Drafts.Concat(additions).ToList(), MaterializeGds(set.GdsBytes));
        _removePdk?.Invoke(set.PdkName);
        try { _registerPdk?.Invoke(set.PdkName, runtime); }
        catch
        {
            _removePdk?.Invoke(set.PdkName);
            _registerPdk?.Invoke(set.PdkName, SubstituteForRuntime(set.Drafts, MaterializeGds(set.GdsBytes)));
            throw;
        }
        set.Drafts.AddRange(additions);
    }
}
