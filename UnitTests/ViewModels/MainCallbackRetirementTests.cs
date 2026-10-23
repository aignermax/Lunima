using System.Runtime.CompilerServices;
using CAP.Avalonia.ViewModels;
using Shouldly;
using UnitTests.Helpers;

namespace UnitTests.ViewModels;

public sealed class MainCallbackRetirementTests
{
    [Fact]
    public void Disposal_PreservesAReplacementCallback()
    {
        var main = MainViewModelTestHelper.CreateMainViewModel();
        Action<string> replacement = _ => { };
        main.LeftPanel.UpdateStatus = replacement;
        main.Dispose();
        main.LeftPanel.UpdateStatus.ShouldBeSameAs(replacement);
    }

    [Fact]
    public void Disposal_ReleasesTheShellWhileSharedSourcesRemainAlive()
    {
        var (shell, sources) = CreateDisposed();
        for (var attempt = 0; attempt < 3 && shell.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        shell.IsAlive.ShouldBeFalse();
        GC.KeepAlive(sources);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Shell, object[] Sources) CreateDisposed()
    {
        var main = MainViewModelTestHelper.CreateMainViewModel();
        var result = (new WeakReference(main), new object[] { main.Canvas, main.LeftPanel, main.CommandManager });
        main.Dispose();
        return result;
    }
}
