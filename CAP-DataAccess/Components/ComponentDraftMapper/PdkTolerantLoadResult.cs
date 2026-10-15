using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;

namespace CAP_DataAccess.Components.ComponentDraftMapper;

/// <summary>
/// Result of <see cref="PdkLoader.LoadFromFileTolerant"/>: the draft reduced to
/// the components that passed validation, plus the per-component errors of the
/// dropped ones (same "[pdk/component] reason" format the strict path reports).
/// </summary>
/// <param name="Pdk">The loaded PDK containing only valid components.</param>
/// <param name="ComponentErrors">One entry per validation failure of a dropped component.</param>
public sealed record PdkTolerantLoadResult(PdkDraft Pdk, IReadOnlyList<string> ComponentErrors);
