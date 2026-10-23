using CAP_Core.Export;
using Shouldly;

namespace UnitTests.PdkOffset;

public sealed class NazcaPreviewCacheEnvironmentTests : IDisposable
{
    private readonly string _script = Path.GetTempFileName();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedInterpreter_DoesNotReturnOldSuccess(bool rawCode)
    {
        var selected = "first";
        var service = new Renderer(_script) { PythonExecutableResolver = () => selected };
        async Task<NazcaPreviewResult> Render() => rawCode
            ? await service.RenderRawCodeAsync("cell code")
            : await service.RenderAsync("pdk", "cell", "");
        (await Render()).XMax.ShouldBe(1);
        selected = "second";
        (await Render()).XMax.ShouldBe(2);
    }

    [Fact]
    public async Task ChangedRendererFile_DoesNotReturnOldSuccess()
    {
        var service = new Renderer(_script);
        (await service.RenderAsync("pdk", "cell", "")).XMax.ShouldBe(1);
        File.WriteAllText(_script, "new renderer version");
        (await service.RenderAsync("pdk", "cell", "")).XMax.ShouldBe(2);
    }

    [Fact]
    public async Task InterpreterChangedDuringRender_CannotCacheOldCompletion()
    {
        var selected = "first";
        var delayed = new TaskCompletionSource<NazcaPreviewResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new Renderer(_script) { PythonExecutableResolver = () => selected, Pending = delayed.Task };
        var old = service.RenderAsync("pdk", "cell", "");
        selected = "second";
        service.Pending = null;
        delayed.SetResult(new NazcaPreviewResult { Success = true, XMax = 99 });
        (await old).XMax.ShouldBe(99);
        (await service.RenderAsync("pdk", "cell", "")).XMax.ShouldBe(2);
    }

    [Fact]
    public async Task ChangedPdkRevision_DoesNotReturnOldSuccess()
    {
        var revision = "original";
        var service = new Renderer(_script) { EnvironmentRevisionResolver = () => revision };
        (await service.RenderAsync("pdk", "cell", "")).XMax.ShouldBe(1);
        revision = "edited";
        (await service.RenderAsync("pdk", "cell", "")).XMax.ShouldBe(2);
    }

    [Fact]
    public async Task InvalidationDuringRender_CannotReplaceNewResult()
    {
        var delayed = new TaskCompletionSource<NazcaPreviewResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new Renderer(_script) { Pending = delayed.Task };
        var old = service.RenderAsync("pdk", "cell", "");
        service.InvalidateCache();
        service.Pending = null;
        (await service.RenderAsync("pdk", "cell", "")).XMax.ShouldBe(2);
        delayed.SetResult(new NazcaPreviewResult { Success = true, XMax = 99 });
        await old;
        (await service.RenderAsync("pdk", "cell", "")).XMax.ShouldBe(2);
    }

    private sealed class Renderer(string script) : NazcaComponentPreviewService("unused", script)
    {
        private int _renders;
        public Task<NazcaPreviewResult>? Pending { get; set; }
        protected override Task<NazcaPreviewResult> RunProcessAsync(string pythonExecutable, IReadOnlyList<string> arguments, CancellationToken ct)
        {
            var ordinal = ++_renders;
            return Pending ?? Task.FromResult(new NazcaPreviewResult { Success = true, XMax = ordinal });
        }
    }

    public void Dispose() => File.Delete(_script);
}
