using System;
using System.IO;
using Shouldly;
using Xunit;

namespace UnitTests.Architecture
{
    /// <summary>
    /// Placement guards for issue #1183 (rest of #1156): the Logic panel is
    /// whole-design analysis (build the network, toggle inputs, step/run the
    /// clock, timeline + waveforms), so it lives as a tab of the bottom analysis
    /// dock — not in the right sidebar, which is reserved for properties of the
    /// current selection. These tests pin that state so the panel cannot
    /// silently drift back into the sidebar.
    /// </summary>
    public class LogicPanelPlacementTests
    {
        private static string MainWindowAxaml =>
            File.ReadAllText(Path.Combine(FindRepoRoot(), "CAP.Avalonia", "Views", "MainWindow.axaml"));

        private static string AnalysisDockAxaml =>
            File.ReadAllText(Path.Combine(FindRepoRoot(), "CAP.Avalonia", "Views", "Panels",
                "AnalysisDockPanel.axaml"));

        [Fact]
        public void MainWindow_RightSidebar_DoesNotHostLogicPanel()
        {
            MainWindowAxaml.ShouldNotContain("LogicPanel");
        }

        [Fact]
        public void MainWindow_RightSidebar_KeepsTruthTablePanel()
        {
            // The Truth Table panel acts on the selected gate group and stays.
            MainWindowAxaml.ShouldContain("TruthTablePanel");
        }

        [Fact]
        public void AnalysisDock_HostsLogicPanel_AsTab()
        {
            AnalysisDockAxaml.ShouldContain("<panels:LogicPanel/>");
            AnalysisDockAxaml.ShouldContain("AnalysisDock.TabLogic");
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CAP.Avalonia", "App.axaml.cs")))
                dir = dir.Parent;

            return dir?.FullName ?? throw new InvalidOperationException("Could not locate repository root");
        }
    }
}
