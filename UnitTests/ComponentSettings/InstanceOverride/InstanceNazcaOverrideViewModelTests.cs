using CAP.Avalonia.ViewModels.ComponentSettings;
using CAP_DataAccess.Persistence.PIR;
using Shouldly;
using UnitTests;
using Xunit;

namespace UnitTests.ComponentSettings.InstanceOverride;

/// <summary>
/// Tests for <see cref="InstanceNazcaOverrideViewModel"/>.
/// Verifies save, reset, live-component application, and status messages.
/// </summary>
public class InstanceNazcaOverrideViewModelTests
{
    private const string Key = "mmi_1";
    private const string TemplateFuncName = "ebeam_mmi1x2";
    private const string TemplateParams = "width=2.0";
    private const string TemplateModule = "siepic";

    private static InstanceNazcaOverrideViewModel NewVm(
        Dictionary<string, NazcaCodeOverride>? store = null,
        string templateFunctionName = TemplateFuncName,
        string templateParams = TemplateParams,
        string? templateModule = TemplateModule,
        Action? onChanged = null)
    {
        store ??= new Dictionary<string, NazcaCodeOverride>();
        return new InstanceNazcaOverrideViewModel(
            Key, store, liveComponent: null,
            templateFunctionName, templateParams, templateModule, onChanged);
    }

    [Fact]
    public void Constructor_WithNoStoredOverride_ShowsTemplateValues()
    {
        var vm = NewVm();

        vm.FunctionName.ShouldBe(TemplateFuncName);
        vm.FunctionParameters.ShouldBe(TemplateParams);
        vm.ModuleName.ShouldBe(TemplateModule);
        vm.HasOverride.ShouldBeFalse();
        vm.StatusText.ShouldBeEmpty();
    }

    [Fact]
    public void Constructor_WithExistingStoredOverride_ShowsOverrideValues()
    {
        var store = new Dictionary<string, NazcaCodeOverride>
        {
            [Key] = new NazcaCodeOverride
            {
                FunctionName = "custom_mmi",
                FunctionParameters = "width=3.5",
                TemplateFunctionName = TemplateFuncName,
                TemplateFunctionParameters = TemplateParams
            }
        };

        var vm = NewVm(store);

        vm.FunctionName.ShouldBe("custom_mmi");
        vm.FunctionParameters.ShouldBe("width=3.5");
        vm.HasOverride.ShouldBeTrue();
    }

    [Fact]
    public void SaveOverride_StoresInDictionaryWithTemplateReference()
    {
        var store = new Dictionary<string, NazcaCodeOverride>();
        var vm = NewVm(store);
        vm.FunctionName = "my_custom_mmi";
        vm.FunctionParameters = "length=4.0";

        vm.SaveOverrideCommand.Execute(null);

        store.ShouldContainKey(Key);
        var saved = store[Key];
        saved.FunctionName.ShouldBe("my_custom_mmi");
        saved.FunctionParameters.ShouldBe("length=4.0");
        saved.TemplateFunctionName.ShouldBe(TemplateFuncName);
        saved.TemplateFunctionParameters.ShouldBe(TemplateParams);
    }

    [Fact]
    public void SaveOverride_SetsHasOverrideTrue()
    {
        var vm = NewVm();
        vm.FunctionName = "custom_func";

        vm.SaveOverrideCommand.Execute(null);

        vm.HasOverride.ShouldBeTrue();
    }

    [Fact]
    public void SaveOverride_EmptyFunctionName_SetsErrorStatus()
    {
        var store = new Dictionary<string, NazcaCodeOverride>();
        var vm = NewVm(store);
        vm.FunctionName = "  "; // whitespace only

        vm.SaveOverrideCommand.Execute(null);

        store.ShouldBeEmpty();
        vm.StatusText.ShouldNotBeEmpty();
        vm.HasOverride.ShouldBeFalse();
    }

    [Fact]
    public void SaveOverride_InvokesOnChanged()
    {
        int callCount = 0;
        var vm = NewVm(onChanged: () => callCount++);
        vm.FunctionName = "custom_func";

        vm.SaveOverrideCommand.Execute(null);

        callCount.ShouldBe(1);
    }

    [Fact]
    public void ResetToTemplate_RemovesFromStore()
    {
        var store = new Dictionary<string, NazcaCodeOverride>
        {
            [Key] = new NazcaCodeOverride { FunctionName = "custom_func", TemplateFunctionName = TemplateFuncName, TemplateFunctionParameters = TemplateParams }
        };
        var vm = NewVm(store);

        vm.ResetToTemplateCommand.Execute(null);

        store.ShouldNotContainKey(Key);
    }

    [Fact]
    public void ResetToTemplate_RestoresDisplayedValuesToTemplate()
    {
        var store = new Dictionary<string, NazcaCodeOverride>
        {
            [Key] = new NazcaCodeOverride { FunctionName = "custom_func", TemplateFunctionName = TemplateFuncName, TemplateFunctionParameters = TemplateParams }
        };
        var vm = NewVm(store);

        vm.ResetToTemplateCommand.Execute(null);

        vm.FunctionName.ShouldBe(TemplateFuncName);
        vm.FunctionParameters.ShouldBe(TemplateParams);
        vm.HasOverride.ShouldBeFalse();
    }

    [Fact]
    public void ResetToTemplate_InvokesOnChanged()
    {
        int callCount = 0;
        var store = new Dictionary<string, NazcaCodeOverride>
        {
            [Key] = new NazcaCodeOverride { FunctionName = "custom_func", TemplateFunctionName = TemplateFuncName, TemplateFunctionParameters = TemplateParams }
        };
        var vm = NewVm(store, onChanged: () => callCount++);

        vm.ResetToTemplateCommand.Execute(null);

        callCount.ShouldBe(1);
    }

    [Fact]
    public void SaveOverride_AppliesImmediatelyToLiveComponent()
    {
        var component = TestComponentFactory.CreateStraightWaveGuide();
        component.Identifier = Key;
        component.NazcaFunctionName = TemplateFuncName;
        component.NazcaFunctionParameters = TemplateParams;

        var store = new Dictionary<string, NazcaCodeOverride>();
        var vm = new InstanceNazcaOverrideViewModel(
            Key, store, component, TemplateFuncName, TemplateParams);
        vm.FunctionName = "custom_mmi";
        vm.FunctionParameters = "width=3.5";

        vm.SaveOverrideCommand.Execute(null);

        component.NazcaFunctionName.ShouldBe("custom_mmi");
        component.NazcaFunctionParameters.ShouldBe("width=3.5");
    }

    [Fact]
    public void ResetToTemplate_RevertsLiveComponent()
    {
        var component = TestComponentFactory.CreateStraightWaveGuide();
        component.Identifier = Key;
        component.NazcaFunctionName = "custom_mmi"; // already overridden

        var store = new Dictionary<string, NazcaCodeOverride>
        {
            [Key] = new NazcaCodeOverride
            {
                FunctionName = "custom_mmi",
                TemplateFunctionName = TemplateFuncName,
                TemplateFunctionParameters = TemplateParams
            }
        };
        var vm = new InstanceNazcaOverrideViewModel(
            Key, store, component, TemplateFuncName, TemplateParams);

        vm.ResetToTemplateCommand.Execute(null);

        component.NazcaFunctionName.ShouldBe(TemplateFuncName);
    }
}
