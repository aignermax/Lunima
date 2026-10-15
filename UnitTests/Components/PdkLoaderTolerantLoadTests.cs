using System;
using System.IO;
using System.Linq;
using CAP_DataAccess.Components.ComponentDraftMapper;
using Shouldly;
using Xunit;

namespace UnitTests.Components
{
    /// <summary>
    /// <see cref="PdkLoader.LoadFromFileTolerant"/> — the STARTUP load path (issue
    /// #1530): one invalid component must not drop its valid siblings. File-level
    /// problems still throw; the strict user-facing paths are untouched.
    /// </summary>
    public class PdkLoaderTolerantLoadTests : IDisposable
    {
        private readonly string _root =
            Path.Combine(Path.GetTempPath(), "pdk-tolerant-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        private string WritePdk(string json, string fileName = "pdk.json")
        {
            Directory.CreateDirectory(_root);
            var path = Path.Combine(_root, fileName);
            File.WriteAllText(path, json);
            return path;
        }

        /// <summary>Two valid components plus one with neither pins nor any geometry source.</summary>
        private const string MixedJson = @"{
            ""name"": ""Mixed PDK"",
            ""components"": [
                {
                    ""name"": ""Good A"",
                    ""category"": ""Test"",
                    ""nazcaFunction"": ""test.straight"",
                    ""widthMicrometers"": 10,
                    ""heightMicrometers"": 2,
                    ""pins"": [
                        { ""name"": ""a0"", ""offsetXMicrometers"": 0, ""offsetYMicrometers"": 1, ""angleDegrees"": 180 },
                        { ""name"": ""b0"", ""offsetXMicrometers"": 10, ""offsetYMicrometers"": 1, ""angleDegrees"": 0 }
                    ]
                },
                {
                    ""name"": ""Broken"",
                    ""category"": ""Test"",
                    ""nazcaFunction"": ""test.broken"",
                    ""widthMicrometers"": 10,
                    ""heightMicrometers"": 2,
                    ""pins"": []
                },
                {
                    ""name"": ""Good B"",
                    ""category"": ""Test"",
                    ""nazcaFunction"": ""test.bend"",
                    ""widthMicrometers"": 5,
                    ""heightMicrometers"": 5,
                    ""pins"": [
                        { ""name"": ""a0"", ""offsetXMicrometers"": 0, ""offsetYMicrometers"": 2.5, ""angleDegrees"": 180 }
                    ]
                }
            ]
        }";

        [Fact]
        public void LoadFromFileTolerant_OneInvalidComponent_KeepsValidSiblingsAndReportsPerComponent()
        {
            var result = new PdkLoader().LoadFromFileTolerant(WritePdk(MixedJson));

            result.Pdk.Name.ShouldBe("Mixed PDK");
            result.Pdk.Components.Select(c => c.Name).ShouldBe(new[] { "Good A", "Good B" });
            var error = result.ComponentErrors.ShouldHaveSingleItem();
            error.ShouldContain("Mixed PDK");
            error.ShouldContain("Broken");
            error.ShouldContain("pin");
        }

        [Fact]
        public void LoadFromFileTolerant_SameFile_StrictLoadStillThrowsEveryError()
        {
            // The split: tolerant loading is for startup only — the explicit
            // user-facing path must keep failing loudly on the same file.
            var path = WritePdk(MixedJson);

            var ex = Should.Throw<PdkValidationException>(
                () => new PdkLoader().LoadFromFileForEditing(path));
            ex.Errors.ShouldContain(e => e.Contains("Broken"));
        }

        [Fact]
        public void LoadFromFileTolerant_MissingFile_ThrowsFileNotFound()
        {
            Should.Throw<FileNotFoundException>(() =>
                new PdkLoader().LoadFromFileTolerant(Path.Combine(_root, "nope.json")));
        }

        [Fact]
        public void LoadFromFileTolerant_MissingPdkName_IsFileLevel_Throws()
        {
            var path = WritePdk(@"{ ""components"": [] }");

            Should.Throw<InvalidOperationException>(() => new PdkLoader().LoadFromFileTolerant(path));
        }

        [Fact]
        public void LoadFromFileTolerant_UnreadableJson_IsFileLevel_Throws()
        {
            var path = WritePdk("{ this is not json");

            Should.Throw<System.Text.Json.JsonException>(() => new PdkLoader().LoadFromFileTolerant(path));
        }

        [Fact]
        public void LoadFromFileTolerant_ComponentWithThrowingValidator_DropsOnlyThatComponent()
        {
            // A parametric S-matrix whose validation THROWS (not error-list):
            // in tolerant mode that exception must poison only its own component.
            var path = WritePdk(@"{
                ""name"": ""Mixed PDK"",
                ""components"": [
                    {
                        ""name"": ""BadMatrix"",
                        ""category"": ""Test"",
                        ""nazcaFunction"": ""test.matrix"",
                        ""widthMicrometers"": 10,
                        ""heightMicrometers"": 2,
                        ""pins"": [
                            { ""name"": ""a0"", ""offsetXMicrometers"": 0, ""offsetYMicrometers"": 1, ""angleDegrees"": 180 }
                        ],
                        ""sMatrix"": {
                            ""connections"": [
                                { ""fromPin"": ""a0"", ""toPin"": ""a0"", ""magnitude"": 1, ""phaseDegrees"": 0, ""magnitudeFormula"": ""p"" }
                            ],
                            ""parameters"": [
                                { ""name"": ""p"" },
                                { ""name"": ""p"" }
                            ]
                        }
                    },
                    {
                        ""name"": ""Good"",
                        ""category"": ""Test"",
                        ""nazcaFunction"": ""test.straight"",
                        ""widthMicrometers"": 10,
                        ""heightMicrometers"": 2,
                        ""pins"": [
                            { ""name"": ""a0"", ""offsetXMicrometers"": 0, ""offsetYMicrometers"": 1, ""angleDegrees"": 180 }
                        ]
                    }
                ]
            }");

            var result = new PdkLoader().LoadFromFileTolerant(path);

            result.Pdk.Components.Select(c => c.Name).ShouldBe(new[] { "Good" });
            result.ComponentErrors.ShouldHaveSingleItem().ShouldContain("BadMatrix");
        }
    }
}
