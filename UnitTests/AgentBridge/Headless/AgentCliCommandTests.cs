using CAP.Avalonia.Services.AgentBridge.Headless;
using Shouldly;
using Xunit;

namespace UnitTests.AgentBridge.Headless;

/// <summary>Command-line parsing of the headless agent interface (<c>lunima mcp</c> / <c>lunima agent …</c>).</summary>
public class AgentCliCommandTests
{
    private static AgentCliCommand Parse(params string[] args)
    {
        var command = AgentCliCommand.Parse(args, out var error);
        error.ShouldBeNull();
        return command!;
    }

    private static string ParseError(params string[] args)
    {
        AgentCliCommand.Parse(args, out var error).ShouldBeNull();
        return error!;
    }

    [Theory]
    [InlineData(new[] { "mcp" }, true)]
    [InlineData(new[] { "agent", "tools" }, true)]
    [InlineData(new[] { "design.lun" }, false)]
    [InlineData(new[] { "--register-claude-code" }, false)]
    [InlineData(new string[0], false)]
    public void IsAgentCommand_OnlyForMcpAndAgent(string[] args, bool expected) =>
        AgentCliCommand.IsAgentCommand(args).ShouldBe(expected);

    [Fact]
    public void Mcp_WithDesign()
    {
        var command = Parse("mcp", "--design", "a.lun");

        command.Verb.ShouldBe(AgentCliVerb.Mcp);
        command.DesignPath.ShouldBe("a.lun");
        command.SavePath.ShouldBeNull();
    }

    [Fact]
    public void Call_WithArguments_DesignAndSaveBack()
    {
        var command = Parse("agent", "call", "move_component", """{"id":"x","dx":5}""", "--design", "a.lun", "--save");

        command.Verb.ShouldBe(AgentCliVerb.Call);
        command.ToolName.ShouldBe("move_component");
        command.ArgumentsJson.ShouldBe("""{"id":"x","dx":5}""");
        command.SavePath.ShouldBe(string.Empty, "an empty save path means: back to the --design file");
    }

    [Fact]
    public void Call_WithoutArguments_DefaultsToEmptyObject() =>
        Parse("agent", "call", "get_design_summary").ArgumentsJson.ShouldBe("{}");

    [Fact]
    public void Run_SaveToOtherFile_ImagesAndKeepGoing()
    {
        var command = Parse("agent", "run", "-", "--save", "out.lun", "--images", "shots", "--keep-going");

        command.Verb.ShouldBe(AgentCliVerb.Run);
        command.ScriptPath.ShouldBe("-");
        command.SavePath.ShouldBe("out.lun");
        command.ImageDirectory.ShouldBe("shots");
        command.KeepGoing.ShouldBeTrue();
    }

    [Theory]
    [InlineData("agent")]
    [InlineData("agent", "help")]
    [InlineData("agent", "--help")]
    public void Help_Variants(params string[] args) => Parse(args).Verb.ShouldBe(AgentCliVerb.Help);

    [Fact]
    public void Install_WithRegister()
    {
        var command = Parse("agent", "install", "--register");

        command.Verb.ShouldBe(AgentCliVerb.Install);
        command.Register.ShouldBeTrue();
    }

    [Fact]
    public void SaveBack_WithoutDesign_IsRejected() =>
        ParseError("agent", "call", "undo", "--save").ShouldContain("--design");

    [Fact]
    public void UnknownOption_IsRejected() =>
        ParseError("agent", "tools", "--verbose").ShouldContain("--verbose");

    [Fact]
    public void MissingOptionValue_IsRejected() =>
        ParseError("agent", "tools", "--design").ShouldContain("needs a value");

    [Fact]
    public void UnknownVerb_IsRejected() =>
        ParseError("agent", "frobnicate").ShouldContain("frobnicate");

    [Fact]
    public void Mcp_RejectsPositionalArguments() =>
        ParseError("mcp", "extra").ShouldContain("extra");
}
