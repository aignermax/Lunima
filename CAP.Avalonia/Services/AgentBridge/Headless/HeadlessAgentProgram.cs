using Avalonia;
using Avalonia.Threading;
using CAP.Avalonia.Services.AgentBridge.Transport;

namespace CAP.Avalonia.Services.AgentBridge.Headless;

/// <summary>
/// Process entry for the headless agent interface (<c>lunima mcp</c>, <c>lunima agent …</c>).
/// Sets up Avalonia on a headless platform without a window, then runs the command inside the
/// UI dispatcher loop, so view-models and rendering (screenshots) behave as in the desktop app.
/// <para>
/// Standard output carries only protocol messages / results: everything else that writes to
/// <see cref="Console.Out"/> (loggers, diagnostics) is redirected to standard error first.
/// </para>
/// </summary>
public static class HeadlessAgentProgram
{
    /// <summary>Runs the command in <paramref name="args"/> and returns the process exit code.</summary>
    /// <param name="args">Command line starting with <c>mcp</c> or <c>agent</c>.</param>
    /// <param name="buildHeadlessApp">The host's <see cref="AppBuilder"/> configured for a headless platform.</param>
    public static int Run(IReadOnlyList<string> args, Func<AppBuilder> buildHeadlessApp)
    {
        var (input, output) = McpStdioServer.OpenStandardStreams();
        Console.SetOut(Console.Error);

        var command = AgentCliCommand.Parse(args, out var error);
        if (command == null || command.Verb == AgentCliVerb.Help)
        {
            if (error != null) Console.Error.WriteLine(error);
            Console.Error.WriteLine(AgentCliCommand.Usage);
            return error == null ? AgentCliRunner.Success : AgentCliRunner.UsageError;
        }

        if (command.Verb == AgentCliVerb.Install)
            return Install(command, output);

        App.HostsHeadlessAgent = true;
        buildHeadlessApp().SetupWithoutStarting();
        return RunInDispatcher(command, input, output);
    }

    private static int Install(AgentCliCommand command, TextWriter output)
    {
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("The path of this executable is unknown.");
        var launcher = AgentInstaller.InstallLauncher(executable);
        Contracts.ClaudeRegistrationResult? registration = null;
        if (command.Register)
            registration = AgentInstaller.RegisterAsync(
                new Registration.ClaudeCodeRegistrar(CAP_Core.Export.ProcessLaunchFactory.CreateDefault()), executable)
                .GetAwaiter().GetResult();

        output.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
        {
            launcher = launcher.LauncherPath,
            on_path = launcher.OnPath,
            hint = launcher.OnPath ? null : $"Add {Path.GetDirectoryName(launcher.LauncherPath)} to PATH.",
            claude_code = registration == null ? null : new
            {
                server = AgentInstaller.HeadlessServerName,
                registered = registration.Success,
                cli_found = registration.CliFound,
                output = registration.Output,
            },
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        output.Flush();
        return registration is { Success: false } ? AgentCliRunner.ToolFailed : AgentCliRunner.Success;
    }

    private static int RunInDispatcher(AgentCliCommand command, TextReader input, TextWriter output)
    {
        var exitCode = AgentCliRunner.ToolFailed;
        using var stop = new CancellationTokenSource();
        Dispatcher.UIThread.Post(async () =>
        {
            try { exitCode = await RunSessionAsync(command, input, output, stop.Token); }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"lunima: {ex.Message}");
                exitCode = AgentCliRunner.ToolFailed;
            }
            finally { stop.Cancel(); }
        });
        Dispatcher.UIThread.MainLoop(stop.Token);
        return exitCode;
    }

    private static async Task<int> RunSessionAsync(
        AgentCliCommand command, TextReader input, TextWriter output, CancellationToken token)
    {
        await using var session = HeadlessAgentSession.Create(Console.Error);
        return await new AgentCliRunner(session, input, output).RunAsync(command, token);
    }
}
