using CAP.Avalonia.ViewModels.Diagnostics;
using CAP_Contracts.Logger;
using CAP_Core;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels;

/// <summary>
/// Unit and integration tests for <see cref="ErrorConsoleViewModel"/> and <see cref="ErrorConsoleService"/>.
/// </summary>
public class ErrorConsoleViewModelTests
{
    // ─── ErrorConsoleService unit tests ───────────────────────────────────────

    [Fact]
    public void Log_AddsEntryToEntries()
    {
        var service = new ErrorConsoleService();

        service.Log(LogLevel.Error, "test error");

        service.Entries.Count.ShouldBe(1);
        service.Entries[0].Message.ShouldBe("test error");
        service.Entries[0].Level.ShouldBe(LogLevel.Error);
    }

    [Fact]
    public void Log_CappsAtMaxEntries()
    {
        var service = new ErrorConsoleService();

        for (int i = 0; i < ErrorConsoleService.MaxEntries + 10; i++)
            service.Log(LogLevel.Info, $"msg {i}");

        service.Entries.Count.ShouldBe(ErrorConsoleService.MaxEntries);
    }

    [Fact]
    public void Log_OldestEntryDroppedWhenAtCapacity()
    {
        var service = new ErrorConsoleService();

        for (int i = 0; i < ErrorConsoleService.MaxEntries; i++)
            service.Log(LogLevel.Info, $"msg {i}");

        service.Log(LogLevel.Error, "newest");

        service.Entries.Count.ShouldBe(ErrorConsoleService.MaxEntries);
        service.Entries[^1].Message.ShouldBe("newest");
        service.Entries[0].Message.ShouldBe("msg 1"); // "msg 0" was dropped
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        var service = new ErrorConsoleService();
        service.Log(LogLevel.Error, "err1");
        service.Log(LogLevel.Warn, "warn1");

        service.Clear();

        service.Entries.Count.ShouldBe(0);
    }

    [Fact]
    public void LogError_IncludesExceptionMessage()
    {
        var service = new ErrorConsoleService();
        var ex = new InvalidOperationException("boom");

        service.LogError("context", ex);

        service.Entries[0].Message.ShouldContain("context");
        service.Entries[0].Message.ShouldContain("boom");
    }

    [Fact]
    public void EntryAdded_EventFiredOnLog()
    {
        var service = new ErrorConsoleService();
        Log? received = null;
        service.EntryAdded += (_, entry) => received = entry;

        service.Log(LogLevel.Warn, "hello");

        received.ShouldNotBeNull();
        received!.Value.Message.ShouldBe("hello");
    }

    // ─── ErrorConsoleViewModel integration tests ──────────────────────────────

    [Fact]
    public void ViewModel_DisplaysEntryWhenServiceLogs()
    {
        var service = new ErrorConsoleService();
        var vm = new ErrorConsoleViewModel(service);

        service.Log(LogLevel.Error, "test error");

        vm.DisplayEntries.Count.ShouldBe(1);
        vm.DisplayEntries[0].Text.ShouldContain("test error");
        vm.DisplayEntries[0].Text.ShouldContain("ERROR");
    }

    [Fact]
    public void ViewModel_ErrorCountUpdatedOnLog()
    {
        var service = new ErrorConsoleService();
        var vm = new ErrorConsoleViewModel(service);

        service.Log(LogLevel.Error, "e1");
        service.Log(LogLevel.Fatal, "e2");
        service.Log(LogLevel.Warn, "w1");

        vm.ErrorCount.ShouldBe(2);
        vm.WarningCount.ShouldBe(1);
    }

    [Fact]
    public void ViewModel_HasErrorsAndHasWarnings_ReflectCounts()
    {
        var service = new ErrorConsoleService();
        var vm = new ErrorConsoleViewModel(service);

        vm.HasErrors.ShouldBeFalse();
        vm.HasWarnings.ShouldBeFalse();

        service.Log(LogLevel.Error, "err");
        vm.HasErrors.ShouldBeTrue();
        vm.HasWarnings.ShouldBeFalse();

        service.Log(LogLevel.Warn, "warn");
        vm.HasWarnings.ShouldBeTrue();
    }

    [Fact]
    public void ViewModel_AutoExpandsOnError()
    {
        var service = new ErrorConsoleService();
        var vm = new ErrorConsoleViewModel(service);
        vm.IsVisible.ShouldBeFalse();

        service.Log(LogLevel.Error, "auto expand me");

        vm.IsVisible.ShouldBeTrue();
    }

    [Fact]
    public void ViewModel_DoesNotAutoExpandOnInfo()
    {
        var service = new ErrorConsoleService();
        var vm = new ErrorConsoleViewModel(service);

        service.Log(LogLevel.Info, "just info");

        vm.IsVisible.ShouldBeFalse();
    }

    [Fact]
    public void ViewModel_ClearCommand_RemovesDisplayEntries()
    {
        var service = new ErrorConsoleService();
        var vm = new ErrorConsoleViewModel(service);
        service.Log(LogLevel.Error, "err");
        service.Log(LogLevel.Warn, "warn");

        vm.ClearCommand.Execute(null);

        vm.DisplayEntries.Count.ShouldBe(0);
        vm.ErrorCount.ShouldBe(0);
        vm.WarningCount.ShouldBe(0);
        vm.EntryCount.ShouldBe(0);
    }

    [Fact]
    public void ViewModel_ToggleCommand_FlipsIsVisible()
    {
        var service = new ErrorConsoleService();
        var vm = new ErrorConsoleViewModel(service);

        vm.IsVisible.ShouldBeFalse();
        vm.ToggleCommand.Execute(null);
        vm.IsVisible.ShouldBeTrue();
        vm.ToggleCommand.Execute(null);
        vm.IsVisible.ShouldBeFalse();
    }

    [Fact]
    public async Task ViewModel_CopyAll_InvokesClipboardCallback()
    {
        var service = new ErrorConsoleService();
        var vm = new ErrorConsoleViewModel(service);
        service.Log(LogLevel.Error, "err1");
        service.Log(LogLevel.Warn, "warn1");

        string? copiedText = null;
        vm.CopyToClipboard = text => { copiedText = text; return Task.CompletedTask; };

        await vm.CopyAllCommand.ExecuteAsync(null);

        copiedText.ShouldNotBeNullOrEmpty();
        copiedText!.ShouldContain("err1");
        copiedText.ShouldContain("warn1");
    }

    [Fact]
    public void LogDisplayEntry_ErrorColor_IsRed()
    {
        var log = new Log { Level = LogLevel.Error, Message = "x", TimeStamp = DateTime.Now, ClassName = "" };
        var entry = new LogDisplayEntry(log);
        entry.Color.ShouldBe("#FF6B6B");
    }

    [Fact]
    public void LogDisplayEntry_WarnColor_IsYellow()
    {
        var log = new Log { Level = LogLevel.Warn, Message = "x", TimeStamp = DateTime.Now, ClassName = "" };
        var entry = new LogDisplayEntry(log);
        entry.Color.ShouldBe("#FFD93D");
    }

    [Fact]
    public void LogDisplayEntry_InfoColor_IsGray()
    {
        var log = new Log { Level = LogLevel.Info, Message = "x", TimeStamp = DateTime.Now, ClassName = "" };
        var entry = new LogDisplayEntry(log);
        entry.Color.ShouldBe("#A0A0A0");
    }
}
