using System.IO;
using System.Linq;
using CAP.Avalonia.Services;
using CAP_Core.Components.Core;
using CAP_DataAccess.Components.ComponentDraftMapper;
using Shouldly;
using Xunit;

namespace UnitTests.Components
{
    /// <summary>
    /// End-to-end coverage for the ONA Analyzer pipeline: PDK JSON → loader →
    /// PdkTemplateConverter → ComponentTemplate → Component. Catches regressions
    /// where the shipped tools-pdk.json fails to reach the canvas due to
    /// validation, conversion, or category-filtering bugs.
    /// </summary>
    public class ToolsPdkAnalyzerTests
    {
        private static string GetToolsPdkPath()
        {
            return Path.Combine(
                Directory.GetCurrentDirectory(),
                "..", "..", "..", "..",
                "CAP-DataAccess", "PDKs", "tools-pdk.json");
        }

        [Fact]
        public void LoadFromJson_AnalysisToolWithoutOffset_PassesValidation()
        {
            // Analysis tools are skipped during GDS export and therefore exempt
            // from the nazcaOriginOffset requirement. Without this exemption
            // tools-pdk.json would fail to load and the ONA Analyzer would never
            // reach the library — the regression Daisy hit on the first build.
            var loader = new PdkLoader();
            var json = @"{
                ""name"": ""Analysis Tools"",
                ""components"": [
                    {
                        ""name"": ""ONA Analyzer"",
                        ""category"": ""Analysis"",
                        ""nazcaFunction"": ""__analyzer__"",
                        ""widthMicrometers"": 80,
                        ""heightMicrometers"": 40,
                        ""pins"": [
                            { ""name"": ""source"", ""offsetXMicrometers"": 80, ""offsetYMicrometers"": 20, ""angleDegrees"": 0 },
                            { ""name"": ""measurement_1"", ""offsetXMicrometers"": 0, ""offsetYMicrometers"": 5, ""angleDegrees"": 180 }
                        ]
                    }
                ]
            }";

            var pdk = loader.LoadFromJson(json);

            pdk.Components.Count.ShouldBe(1);
            pdk.Components[0].NazcaFunction.ShouldBe("__analyzer__");
        }

        [Fact]
        public void LoadFromJson_PhysicalComponentWithoutOffset_StillFailsValidation()
        {
            // Regression guard: the exemption must apply ONLY to analysis tools.
            // Regular PDK components must still require nazcaOriginOffsetX/Y.
            var loader = new PdkLoader();
            var json = @"{
                ""name"": ""Bad PDK"",
                ""components"": [
                    {
                        ""name"": ""MMI"",
                        ""nazcaFunction"": ""pdk.mmi"",
                        ""widthMicrometers"": 50,
                        ""heightMicrometers"": 30,
                        ""pins"": [
                            { ""name"": ""in"", ""offsetXMicrometers"": 0, ""offsetYMicrometers"": 15, ""angleDegrees"": 180 }
                        ]
                    }
                ]
            }";

            Should.Throw<PdkValidationException>(() => loader.LoadFromJson(json));
        }

        [Fact]
        public void LoadFromFile_ToolsPdk_LoadsSuccessfully()
        {
            var path = GetToolsPdkPath();
            if (!File.Exists(path)) return; // CI guard

            var loader = new PdkLoader();
            var pdk = loader.LoadFromFile(path);

            pdk.Name.ShouldBe("Analysis Tools");
            pdk.Components.Count.ShouldBeGreaterThanOrEqualTo(1);
            pdk.Components.Any(c => c.Name == "ONA Analyzer").ShouldBeTrue();
        }

        [Fact]
        public void LoadFromFile_OnaAnalyzer_HasExpectedPinStructure()
        {
            var path = GetToolsPdkPath();
            if (!File.Exists(path)) return; // CI guard

            var loader = new PdkLoader();
            var pdk = loader.LoadFromFile(path);

            var analyzer = pdk.Components.First(c => c.Name == "ONA Analyzer");
            analyzer.Category.ShouldBe("Analysis");
            analyzer.NazcaFunction.ShouldBe("__analyzer__");
            analyzer.Pins.Any(p => p.Name == "source").ShouldBeTrue();
            analyzer.Pins.Any(p => p.Name.StartsWith("measurement")).ShouldBeTrue();
        }

        [Fact]
        public void ConvertToTemplate_OnaAnalyzer_PropagatesAnalysisCategoryAndSentinel()
        {
            // Verifies the conversion pipeline carries the Analysis category and
            // the __analyzer__ sentinel through to ComponentTemplate so the
            // library filter ("ONA") can find it and SimulationService.IsLightSource
            // can keep ignoring it.
            var path = GetToolsPdkPath();
            if (!File.Exists(path)) return;

            var loader = new PdkLoader();
            var pdk = loader.LoadFromFile(path);
            var draft = pdk.Components.First(c => c.Name == "ONA Analyzer");

            var template = PdkTemplateConverter.ConvertToTemplate(draft, pdk.Name, pdk.NazcaModuleName);

            template.Name.ShouldBe("ONA Analyzer");
            template.Category.ShouldBe("Analysis");
            template.NazcaFunctionName.ShouldBe("__analyzer__");
            template.PdkSource.ShouldBe("Analysis Tools");
        }

        [Fact]
        public void CreateFromTemplate_OnaAnalyzer_ProducesIsAnalysisToolComponent()
        {
            // Final hop: a Component placed from the ONA Analyzer template must
            // be detected as an analysis tool by the rest of the system (export
            // skip, special UI flow).
            var path = GetToolsPdkPath();
            if (!File.Exists(path)) return;

            var loader = new PdkLoader();
            var pdk = loader.LoadFromFile(path);
            var draft = pdk.Components.First(c => c.Name == "ONA Analyzer");
            var template = PdkTemplateConverter.ConvertToTemplate(draft, pdk.Name, pdk.NazcaModuleName);

            var component = CAP.Avalonia.ViewModels.Library.ComponentTemplates.CreateFromTemplate(template, 0, 0);

            component.IsAnalysisTool.ShouldBeTrue();
            component.NazcaFunctionName.ShouldBe(Component.AnalysisToolNazcaSentinel);
            component.PhysicalPins.Any(p => p.Name == "source").ShouldBeTrue();
        }
    }
}
