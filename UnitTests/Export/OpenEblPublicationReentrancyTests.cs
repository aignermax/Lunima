using Avalonia.Headless.XUnit;
using Shouldly;

namespace UnitTests.Export;

public sealed class OpenEblPublicationReentrancyTests
{
    [AvaloniaFact]
    public async Task ReportObserverRetiresOwner_NoLaterReportFieldsArePublished()
    {
        await using var fixture = new ExportWorkflowLifetimeFixture();
        var vm = new OpenEblWorkflowLifetimeTests.TestableViewModel(fixture, OpenEblWindowLifetimeTests.Checker());
        Task? stopping = null;
        (string Status, string Headline, bool Submission, bool Verification)? atStop = null;
        vm.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName != nameof(vm.HasResult) || !vm.HasResult || stopping != null) return;
            stopping = vm.StopAsync();
            atStop = (vm.StatusText, vm.Headline, vm.SubmissionPassed, vm.VerificationPassed);
        };
        fixture.Runner.Release.TrySetResult();
        try
        {
            await vm.CheckAnywayCommand.ExecuteAsync(null).WaitAsync(ExportWorkflowLifetimeFixture.Guard);
            stopping.ShouldNotBeNull();
            await stopping!.WaitAsync(ExportWorkflowLifetimeFixture.Guard);
            (vm.StatusText, vm.Headline, vm.SubmissionPassed, vm.VerificationPassed).ShouldBe(atStop!.Value);
        }
        finally { await vm.StopAsync(); OpenEblWindowLifetimeTests.RemoveArtifacts(fixture); }
    }
}
