using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using CAP.Avalonia.Services.AgentBridge.Headless;
using CAP_Core.Export;
using Shouldly;

namespace UnitTests.AgentBridge.Headless.McpProcess;

/// <summary>
/// The built <c>CAP.Desktop</c> application — the executable users run as <c>lunima</c> — taken from
/// the same configuration and target framework as this test assembly, and started through the
/// <c>dotnet</c> host of the runtime that runs the tests (no PATH lookup, no globally installed SDK).
/// </summary>
/// <param name="AssemblyPath">Absolute path of <c>CAP.Desktop.dll</c>.</param>
/// <param name="DotnetHost">The <c>dotnet</c> muxer that starts it.</param>
internal sealed record LunimaDesktopBuild(string AssemblyPath, string DotnetHost)
{
    private const string DesktopProject = "CAP.Desktop";
    private const string SharedApplicationAssembly = "CAP.Avalonia.dll";

    /// <summary>Where this test configuration expects <c>CAP.Desktop.dll</c>, or null outside a repository checkout.</summary>
    public static string? ExpectedAssemblyPath()
    {
        var testOutput = new DirectoryInfo(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
        var configuration = testOutput.Parent?.Name;
        var repository = FindRepositoryRoot(testOutput);
        if (configuration == null || repository == null)
            return null;
        return Path.Combine(repository, DesktopProject, "bin", configuration, testOutput.Name, DesktopProject + ".dll");
    }

    /// <summary>The build, or null when CAP.Desktop has not been built for this configuration.</summary>
    public static LunimaDesktopBuild? Find()
    {
        var path = ExpectedAssemblyPath();
        return path != null && File.Exists(path) ? new LunimaDesktopBuild(path, FindDotnetHost()) : null;
    }

    /// <summary>
    /// Why this build cannot vouch for the code under test, or null when it ships the same
    /// <c>CAP.Avalonia</c> module this test run loaded (deterministic builds share the MVID).
    /// </summary>
    public string? StaleReason()
    {
        var shipped = Path.Combine(Path.GetDirectoryName(AssemblyPath)!, SharedApplicationAssembly);
        var tested = typeof(HeadlessAgentProgram).Assembly.ManifestModule.ModuleVersionId;
        if (File.Exists(shipped) && ReadModuleVersionId(shipped) == tested)
            return null;
        return $"{AssemblyPath} was built from different CAP.Avalonia sources than this test run; " +
               $"rebuild it with `dotnet build {DesktopProject}/{DesktopProject}.csproj`.";
    }

    /// <summary>A redirected start of <c>dotnet CAP.Desktop.dll ARGUMENTS</c> inside <paramref name="sandbox"/>.</summary>
    public ProcessStartInfo StartInfo(LunimaSandbox sandbox, params string[] arguments)
    {
        string[] command = [AssemblyPath, .. arguments];
        ProcessLaunchFactory.CreateDefault()
            .TryBuild(DotnetHost, command, sandbox.Root, sandbox.Environment, out var info, out var error)
            .ShouldBeTrue(error);
        info.RedirectStandardInput = true;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        return info;
    }

    private static string? FindRepositoryRoot(DirectoryInfo? directory)
    {
        for (; directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, DesktopProject, DesktopProject + ".csproj")))
                return directory.FullName;
        }
        return null;
    }

    /// <summary>The muxer beside <c>shared/Microsoft.NETCore.App/&lt;version&gt;/</c> of the running runtime.</summary>
    private static string FindDotnetHost()
    {
        var runtime = new DirectoryInfo(Path.TrimEndingDirectorySeparator(RuntimeEnvironment.GetRuntimeDirectory()));
        var root = runtime.Parent?.Parent?.Parent;
        var host = root == null ? null : Path.Combine(root.FullName, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        return host != null && File.Exists(host) ? host : "dotnet";
    }

    private static Guid ReadModuleVersionId(string path)
    {
        using var stream = File.OpenRead(path);
        using var image = new PEReader(stream);
        var metadata = image.GetMetadataReader();
        return metadata.GetGuid(metadata.GetModuleDefinition().Mvid);
    }
}
