using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.AI;
using Moq;
using Shouldly;

namespace UnitTests.AI;

/// <summary>
/// Unit tests for <see cref="AiAssistantViewModel"/>.
/// </summary>
public class AiAssistantViewModelTests
{
    private readonly Mock<IAiService> _mockAiService;
    private readonly UserPreferencesService _preferencesService;

    public AiAssistantViewModelTests()
    {
        _mockAiService = new Mock<IAiService>();
        _mockAiService.Setup(s => s.IsConfigured).Returns(false);

        // Use an isolated temp file so tests don't touch real user preferences
        var tempFile = Path.Combine(Path.GetTempPath(), $"cap-test-prefs-{Guid.NewGuid()}.json");
        _preferencesService = new UserPreferencesService(tempFile);
    }

    [Fact]
    public void Constructor_ShouldAddWelcomeMessage()
    {
        var vm = CreateViewModel();

        vm.Messages.Count.ShouldBe(1);
        vm.Messages[0].Role.ShouldBe(AiChatRole.Assistant);
        vm.Messages[0].Content.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void Constructor_ShouldLoadSavedApiKey()
    {
        _preferencesService.SetAiApiKey("test-api-key");
        _mockAiService.Setup(s => s.IsConfigured).Returns(true);

        var vm = CreateViewModel();

        vm.ApiKey.ShouldBe("test-api-key");
        _mockAiService.Verify(s => s.SetApiKey("test-api-key"), Times.Once);
    }

    [Fact]
    public void Constructor_WhenNoApiKey_ShouldNotCallSetApiKey()
    {
        var vm = CreateViewModel();

        _mockAiService.Verify(s => s.SetApiKey(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SendMessage_ShouldAddUserAndAssistantMessages()
    {
        const string aiResponse = "I'll help you design a 1x2 splitter using an MMI coupler.";
        _mockAiService
            .Setup(s => s.SendMessageAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<(string, string)>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);

        var vm = CreateViewModel();
        vm.UserInput = "Create a 1x2 splitter";

        await vm.SendMessageCommand.ExecuteAsync(null);

        vm.Messages.Count.ShouldBe(3); // welcome + user + assistant
        vm.Messages[1].Role.ShouldBe(AiChatRole.User);
        vm.Messages[1].Content.ShouldBe("Create a 1x2 splitter");
        vm.Messages[2].Role.ShouldBe(AiChatRole.Assistant);
        vm.Messages[2].Content.ShouldBe(aiResponse);
    }

    [Fact]
    public async Task SendMessage_ShouldClearUserInputAfterSend()
    {
        _mockAiService
            .Setup(s => s.SendMessageAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<(string, string)>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("response");

        var vm = CreateViewModel();
        vm.UserInput = "Create a ring resonator";

        await vm.SendMessageCommand.ExecuteAsync(null);

        vm.UserInput.ShouldBe("");
    }

    [Fact]
    public async Task SendMessage_WhenInputIsWhitespace_ShouldNotSendMessage()
    {
        var vm = CreateViewModel();
        vm.UserInput = "   ";
        var initialCount = vm.Messages.Count;

        await vm.SendMessageCommand.ExecuteAsync(null);

        _mockAiService.Verify(
            s => s.SendMessageAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<(string, string)>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        vm.Messages.Count.ShouldBe(initialCount);
    }

    [Fact]
    public void ClearHistory_ShouldResetToSingleAssistantMessage()
    {
        var vm = CreateViewModel();
        vm.Messages.Add(new AiChatMessage { Role = AiChatRole.User, Content = "Test" });
        vm.Messages.Add(new AiChatMessage { Role = AiChatRole.Assistant, Content = "Reply" });

        vm.ClearHistoryCommand.Execute(null);

        vm.Messages.Count.ShouldBe(1);
        vm.Messages[0].Role.ShouldBe(AiChatRole.Assistant);
    }

    [Fact]
    public void ApiKeySetter_ShouldCallSetApiKeyOnService()
    {
        // Auto-persist: OnApiKeyChanged propagates to the AI service on every
        // edit, so no explicit Save call is required. The SaveApiKeyCommand
        // test below still exists for the legacy flow but is now a status-only
        // command.
        var vm = CreateViewModel();

        vm.ApiKey = "sk-ant-new-key";

        _mockAiService.Verify(s => s.SetApiKey("sk-ant-new-key"), Times.Once);
    }

    [Fact]
    public void ApiKeySetter_ShouldPersistKeyToPreferences()
    {
        var vm = CreateViewModel();

        vm.ApiKey = "sk-ant-persist-key";

        _preferencesService.GetAiApiKey().ShouldBe("sk-ant-persist-key");
    }

    [Fact]
    public void ApiKeySetter_ShouldTrimWhitespaceBeforePersisting()
    {
        // Guards the OnApiKeyChanged contract: the raw UI text may have
        // trailing whitespace from a paste, but what lands in prefs and
        // reaches the service must be trimmed — anything else would send
        // "sk-ant-xxx\n" to Anthropic and get a cryptic 401.
        var vm = CreateViewModel();

        vm.ApiKey = "  sk-ant-padded  ";

        _mockAiService.Verify(s => s.SetApiKey("sk-ant-padded"), Times.Once);
        _preferencesService.GetAiApiKey().ShouldBe("sk-ant-padded");
    }

    [Fact]
    public void SaveApiKey_LegacyCommand_StillRuns()
    {
        // Legacy command retained for the old chat-panel save-button flow.
        // It no longer writes to the service/prefs (OnApiKeyChanged already
        // did on the preceding ApiKey assignment); it only resets the
        // expander and updates status text.
        var vm = CreateViewModel();
        vm.ApiKey = "sk-ant-legacy";

        vm.SaveApiKeyCommand.Execute(null);

        _preferencesService.GetAiApiKey().ShouldBe("sk-ant-legacy");
        vm.StatusText.ShouldContain("saved");
    }

    [Fact]
    public void SaveApiKey_ShouldCollapseSettings()
    {
        var vm = CreateViewModel();
        vm.IsSettingsExpanded = true;
        vm.ApiKey = "key";

        vm.SaveApiKeyCommand.Execute(null);

        vm.IsSettingsExpanded.ShouldBeFalse();
    }

    [Fact]
    public async Task SendMessage_IsTyping_ShouldBeTrueDuringRequest()
    {
        var tcs = new TaskCompletionSource<string>();
        _mockAiService
            .Setup(s => s.SendMessageAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<(string, string)>>(), It.IsAny<CancellationToken>()))
            .Returns(tcs.Task);

        var vm = CreateViewModel();
        vm.UserInput = "Test";

        var sendTask = vm.SendMessageCommand.ExecuteAsync(null);

        vm.IsTyping.ShouldBeTrue();

        tcs.SetResult("done");
        await sendTask;

        vm.IsTyping.ShouldBeFalse();
    }

    // NOTE: Tests for `BuildGridToolDefinitions` were removed. That static method
    // was replaced by the DI-based IAiTool / AiToolRegistry pattern — each tool
    // is now its own IAiTool class (PlaceComponentTool, ClearGridTool, ...) and
    // has its own test (see UnitTests/AI/*ToolTests.cs + AiToolRegistryTests.cs).

    private AiAssistantViewModel CreateViewModel()
        => new AiAssistantViewModel(_mockAiService.Object, _preferencesService);
}
