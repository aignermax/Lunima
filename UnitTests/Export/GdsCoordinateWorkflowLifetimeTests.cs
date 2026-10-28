using CAP.Avalonia.Services.Lifetime;
using CAP.Avalonia.ViewModels.Diagnostics;
using CAP.Avalonia.ViewModels.Export;
using CAP_Core.Export.Contracts;
using Shouldly;

namespace UnitTests.Export;

public sealed class GdsCoordinateWorkflowLifetimeTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(8);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Retirement_CancelsAndWaitsForActualAcceptedWork(bool comparison, bool applicationStop)
    {
        await using var fixture = new Workflow(comparison);
        var running = fixture.Run();
        await fixture.Port.Started.Task.WaitAsync(Guard);
        var stopping = applicationStop ? fixture.Application.StopAsync() : fixture.Stop();
        try
        {
            stopping.IsCompleted.ShouldBeFalse("retirement must retain the accepted service operation until its cleanup finishes");
            await fixture.Port.Cancelled.Task.WaitAsync(Guard);
            fixture.Port.Release.TrySetResult();
            await Task.WhenAll(running, stopping).WaitAsync(Guard);
            fixture.HasResult().ShouldBeFalse("a retired coordinate operation cannot publish its late result");
            await fixture.Run().WaitAsync(Guard);
            fixture.Port.Calls.ShouldBe(1, "retirement closes admission permanently");
        }
        finally
        {
            fixture.Port.Release.TrySetResult();
            await ObserveAsync(running);
            await ObserveAsync(stopping);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateResult_AfterStop_DoesNotOverwriteRetiredPresentation(bool comparison)
    {
        await using var fixture = new Workflow(comparison);
        var running = fixture.Run();
        await fixture.Port.Started.Task.WaitAsync(Guard);
        var stopping = fixture.Stop();
        try
        {
            fixture.Port.Release.TrySetResult();
            await Task.WhenAll(running, stopping).WaitAsync(Guard);
            fixture.HasResult().ShouldBeFalse();
        }
        finally
        {
            fixture.Port.Release.TrySetResult();
            await ObserveAsync(running);
            await ObserveAsync(stopping);
        }
    }

    private static async Task ObserveAsync(Task task)
    {
        try { await task.WaitAsync(Guard); }
        catch (OperationCanceledException) when (task.IsCompleted) { }
    }

    private sealed class Workflow : IAsyncDisposable
    {
        public ApplicationWorkCoordinator Application { get; } = new();
        public HeldCoordinates Port { get; } = new();
        public Func<Task> Run { get; }
        public Func<Task> Stop { get; }
        public Func<bool> HasResult { get; }

        public Workflow(bool comparison)
        {
            if (comparison)
            {
                var vm = new GdsCoordinateComparisonViewModel(Port, Application)
                    { ReferenceJsonPath = "reference.json", SystemJsonPath = "system.json" };
                Run = () => vm.RunComparisonCommand.ExecuteAsync(null);
                Stop = vm.StopAsync;
                HasResult = () => vm.HasResults;
            }
            else
            {
                var vm = new GdsCoordExtractViewModel(Port, Application) { GdsFilePath = "chip.gds" };
                Run = () => vm.ExtractCoordinatesCommand.ExecuteAsync(null);
                Stop = vm.StopAsync;
                HasResult = () => vm.HasResult;
            }
        }

        public async ValueTask DisposeAsync()
        {
            Port.Release.TrySetResult();
            await Stop().WaitAsync(Guard);
            await Application.StopAsync().WaitAsync(Guard);
        }
    }

    private sealed class HeldCoordinates : IGdsCoordinateExtractor, IGdsCoordinateComparer
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }
        public void SetCustomPythonPath(string? path) { }

        private async Task HoldAsync(CancellationToken token)
        {
            Calls++;
            using var registration = token.Register(() => Cancelled.TrySetResult());
            Started.TrySetResult();
            // A cooperative cancellation signal does not mean actual cleanup is complete.
            await Release.Task;
        }

        public async Task<GdsCoordinateExtractionResult> ExtractAsync(string path, string? output = null,
            CancellationToken cancellationToken = default)
        {
            await HoldAsync(cancellationToken);
            return new() { Success = true, JsonOutputPath = "old.json", JsonContent = "{}", Status = "old extraction" };
        }

        public Task<GdsCoordinateComparisonResult> CompareAsync(string reference, string system, string? script = null)
            => CompareAsync(reference, system, script, default);

        public async Task<GdsCoordinateComparisonResult> CompareAsync(string reference, string system,
            string? script, CancellationToken cancellationToken)
        {
            await HoldAsync(cancellationToken);
            return new() { Passed = true, RawOutput = "old comparison" };
        }
    }
}
