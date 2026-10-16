using System.Reflection;
using Avalonia;
using Avalonia.Threading;

namespace UnitTests.UI;

/// <summary>Bridges the pre-platform dispatcher into Avalonia 11.3.13 shared headless setup.</summary>
internal static class HeadlessDispatcherBootstrap
{
    internal static AppBuilder WithFallbackDispatcherReset(this AppBuilder builder)
    {
        var initialize = builder.WindowingSubsystemInitializer
            ?? throw new InvalidOperationException("Configure UseHeadless before the dispatcher bootstrap.");
        return builder.UseWindowingSubsystem(() =>
        {
            initialize();
            // A plain Fact's background timer can recreate the fallback during
            // platform initialization. Clear it only after headless services are
            // registered, so the next dispatcher lookup uses the controlled impl.
            ResetPrePlatformFallback();
            if (!Dispatcher.UIThread.SupportsRunLoops)
                throw new InvalidOperationException("Headless setup did not install a controlled UI dispatcher.");
        }, builder.WindowingSubsystemName!);
    }

    internal static void ResetPrePlatformFallback()
    {
        // Ordinary Facts can create DispatcherTimer before the first AvaloniaFact.
        // PerAssembly setup does not reset that cached NullDispatcherImpl, unlike
        // PerTest. Reset only before an application exists, never a live renderer.
        if (Application.Current != null || Dispatcher.UIThread.SupportsRunLoops) return;
        var version = typeof(Dispatcher).Assembly.GetName().Version;
        if (version != new Version(11, 3, 13, 0))
            throw new InvalidOperationException($"Review the headless dispatcher bootstrap for Avalonia {version}.");
        var reset = typeof(Dispatcher).GetMethod("ResetBeforeUnitTests",
            BindingFlags.Static | BindingFlags.NonPublic, null, Type.EmptyTypes, null)
            ?? throw new MissingMethodException(typeof(Dispatcher).FullName, "ResetBeforeUnitTests");
        reset.Invoke(null, null);
    }
}
