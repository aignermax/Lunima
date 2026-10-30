using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.PdkOffset;
using CAP_Core.Export;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using Shouldly;

namespace UnitTests.PdkOffset;

/// <summary>Batch calibration report content and culture-independent numeric formatting.</summary>
[Collection("NazcaComponentPreviewService")]
public class NazcaComponentPreviewReportTests
{
    // Keep localized status assertions independent of the developer or CI language.
    public NazcaComponentPreviewReportTests() =>
        LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);

    [Fact]
    public void FormatBatchReport_FullReport_IncludesAllRowsAndMarkdownHeader()
    {
        var rows = new List<ComponentCheckResult>
        {
            new("good_one", ComponentCheckStatus.Aligned, 2, 2, 0.05, "All 2 pins within 0.5 µm."),
            new("bad_one",  ComponentCheckStatus.PinCountMismatch, 1, 2, double.NaN, "Lunima 1, Nazca 2."),
        };

        var text = PdkOffsetEditorViewModel.FormatBatchReport(rows, errorsOnly: false);

        text.ShouldContain("PDK calibration report — 2 component(s)");
        text.ShouldContain("| Component | Status | Pins L/N | Δmax (µm) | Message |");
        text.ShouldContain("good_one");
        text.ShouldContain("bad_one");
        text.ShouldContain("0.05");           // delta formatted with invariant culture (no locale comma)
        text.ShouldContain("—");              // NaN delta rendered as em-dash
    }

    [Fact]
    public void FormatBatchReport_ErrorsOnly_UsesErrorsHeader()
    {
        var rows = new List<ComponentCheckResult>
        {
            new("bad", ComponentCheckStatus.RenderFailed, 1, 0, double.NaN, "module not found"),
        };

        var text = PdkOffsetEditorViewModel.FormatBatchReport(rows, errorsOnly: true);

        text.ShouldContain("PDK calibration — 1 unresolved component(s)");
        text.ShouldContain("module not found");
    }

    [Fact]
    public void FormatBatchReport_DeltaUsesInvariantCulture()
    {
        // Markdown tables are language-neutral — German locale would render
        // 1.50 as 1,50 and break the column. Pin the formatter.
        var prevCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture =
                System.Globalization.CultureInfo.GetCultureInfo("de-DE");
            var rows = new List<ComponentCheckResult>
            {
                new("x", ComponentCheckStatus.Misaligned, 1, 1, 1.5, "off"),
            };
            var text = PdkOffsetEditorViewModel.FormatBatchReport(rows, errorsOnly: false);
            text.ShouldContain("1.50");
            text.ShouldNotContain("1,50");
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = prevCulture;
        }
    }
}
