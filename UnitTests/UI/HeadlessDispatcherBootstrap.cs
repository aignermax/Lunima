using System.Reflection;
using Avalonia;
using Avalonia.Threading;
using Avalonia.Controls.Platform;

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
            ManagedDispatcherImpl? seeded = null;
            if (Application.Current == null && !Dispatcher.UIThread.SupportsRunLoops)
            {
                // Headless.Initialize constructs its compositor immediately after binding
                // platform services. Seed a controlled dispatcher before that constructor
                // captures UIThread/MediaContext; never reset them afterward.
                VerifyPinnedVersion();
                seeded = new ManagedDispatcherImpl(null);
                BindDispatcher(seeded);
                ResetPrePlatformFallback();
                _ = Dispatcher.UIThread;
            }
            initialize();
            if (seeded != null)
                BindDispatcher(seeded);
            if (!Dispatcher.UIThread.SupportsRunLoops)
                throw new InvalidOperationException("Headless setup did not install a controlled UI dispatcher.");
        }, builder.WindowingSubsystemName!);
    }

    // CurrentMutable is omitted from Avalonia's reference assembly, but available in
    // the pinned runtime. Keep this bridge confined to the test application's setup.
    private static void BindDispatcher(ManagedDispatcherImpl dispatcher)
    {
        var locatorProperty = typeof(AvaloniaLocator).GetProperty("CurrentMutable", BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingMemberException(typeof(AvaloniaLocator).FullName, "CurrentMutable");
        var locator = locatorProperty.GetValue(null)!;
        var bind = locator.GetType().GetMethod("Bind")
            ?? throw new MissingMethodException(locator.GetType().FullName, "Bind");
        var binding = bind.MakeGenericMethod(typeof(IDispatcherImpl)).Invoke(locator, null)!;
        var toConstant = binding.GetType().GetMethod("ToConstant")
            ?? throw new MissingMethodException(binding.GetType().FullName, "ToConstant");
        toConstant.MakeGenericMethod(typeof(ManagedDispatcherImpl)).Invoke(binding, new object[] { dispatcher });
    }

    internal static void ResetPrePlatformFallback()
    {
        // Ordinary Facts can create DispatcherTimer before the first AvaloniaFact.
        // PerAssembly setup does not reset that cached NullDispatcherImpl, unlike
        // PerTest. Reset only before an application exists, never a live renderer.
        if (Application.Current != null || Dispatcher.UIThread.SupportsRunLoops) return;
        VerifyPinnedVersion();
        var reset = typeof(Dispatcher).GetMethod("ResetBeforeUnitTests",
            BindingFlags.Static | BindingFlags.NonPublic, null, Type.EmptyTypes, null)
            ?? throw new MissingMethodException(typeof(Dispatcher).FullName, "ResetBeforeUnitTests");
        reset.Invoke(null, null);
    }

    private static void VerifyPinnedVersion()
    {
        var version = typeof(Dispatcher).Assembly.GetName().Version;
        if (version != new Version(11, 3, 13, 0))
            throw new InvalidOperationException($"Review the headless dispatcher bootstrap for Avalonia {version}.");
    }

}
