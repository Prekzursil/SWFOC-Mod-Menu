using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.ViewModels;

/// <summary>
/// 2026-05-07 (iter 310, Thread D arc post-finale): pin tests for the
/// SettingsTabViewModel IconsRoot UI extension. Validates the two-way
/// binding (TextBox ↔ V2Settings.IconsRoot) + the empty-string ↔ null
/// normalization + the IconsRootStatus badge shape (3 distinct states).
///
/// BrowseIconsRootCommand interaction is not tested here because that
/// would require a real WPF dispatcher + dialog interception. Operator
/// smoke-tests the dialog manually; the command's existence + invocability
/// are pinned via the property-binding tests.
/// </summary>
public sealed class Iter310SettingsIconsRootUiTests : IDisposable
{
    private readonly string _tmpDir;

    public Iter310SettingsIconsRootUiTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(),
            $"swfoc_settings_iconsroot_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tmpDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, recursive: true); } catch { }
    }

    [Fact]
    public void IconsRoot_DefaultsToEmptyString_NotNull_ForUiBinding()
    {
        var settings = new V2Settings();
        var vm = new SettingsTabViewModel(settings);
        // WPF TextBox bound to a string property: null would render literal "null"
        // in the textbox; empty string renders as a blank-but-present input.
        vm.IconsRoot.Should().Be(string.Empty,
            because: "VM property surfaces underlying null as empty string for WPF TextBox binding");
    }

    [Fact]
    public void IconsRoot_SetterPropagatesToSettings()
    {
        var settings = new V2Settings();
        var vm = new SettingsTabViewModel(settings);
        vm.IconsRoot = @"C:\Games\SWFOC\extracted";
        settings.IconsRoot.Should().Be(@"C:\Games\SWFOC\extracted",
            because: "VM is a thin wrapper — the setter must write through to the underlying settings record so SaveCommand persists it");
    }

    [Fact]
    public void IconsRoot_EmptyStringNormalizesToNullInStorage()
    {
        var settings = new V2Settings { IconsRoot = @"C:\some\path" };
        var vm = new SettingsTabViewModel(settings);
        vm.IconsRoot = string.Empty;
        settings.IconsRoot.Should().BeNull(
            because: "operator clearing the textbox should map to underlying null so the JSON serializer emits null (matches iter-309 ResolveIconsRoot precedence: whitespace = unset)");
    }

    [Fact]
    public void IconsRoot_WhitespaceNormalizesToNullInStorage()
    {
        var settings = new V2Settings { IconsRoot = @"C:\some\path" };
        var vm = new SettingsTabViewModel(settings);
        vm.IconsRoot = "   ";
        settings.IconsRoot.Should().BeNull(
            because: "whitespace-only values must normalize to null at the VM layer too — operator might paste a value with trailing whitespace by accident");
    }

    [Fact]
    public void IconsRootStatus_WhenUnset_ShowsActionableHint()
    {
        var settings = new V2Settings { IconsRoot = null };
        var vm = new SettingsTabViewModel(settings);
        vm.IconsRootStatus.Should().Contain("unset",
            because: "operators need a clear next-step pointer when the field is empty");
        vm.IconsRootStatus.Should().Contain("SWFOC_EXTRACTED_DDS_ROOT",
            because: "the env-var fallback (iter-309) should be discoverable from the badge text alone");
    }

    [Fact]
    public void IconsRootStatus_WhenDirectoryMissing_ShowsClearError()
    {
        var settings = new V2Settings { IconsRoot = @"Z:\does\not\exist\anywhere" };
        var vm = new SettingsTabViewModel(settings);
        vm.IconsRootStatus.Should().Contain("not found",
            because: "operator typo'd the path — badge must surface this without crashing");
    }

    [Fact]
    public void IconsRootStatus_WhenEmptyDirExists_ShowsExtractHint()
    {
        var settings = new V2Settings { IconsRoot = _tmpDir };
        var vm = new SettingsTabViewModel(settings);
        vm.IconsRootStatus.Should().Contain("no i_button_*.dds",
            because: "directory exists but no extracted icons — operator hasn't run the Python extract pipeline yet");
        vm.IconsRootStatus.Should().Contain("meg_parser.py",
            because: "pointing operator at the exact CLI command they need is more useful than a vague 'no icons' message");
    }

    [Fact]
    public void IconsRootStatus_WhenIconsPresent_ShowsCount()
    {
        var unitsDir = Path.Combine(_tmpDir, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(unitsDir);
        File.WriteAllBytes(Path.Combine(unitsDir, "i_button_Empire_AT_AT.dds"), new byte[] { 0xDD });
        File.WriteAllBytes(Path.Combine(unitsDir, "i_button_Rebel_X_Wing.dds"), new byte[] { 0xDD });
        File.WriteAllBytes(Path.Combine(unitsDir, "i_button_Underworld_F9TZ.dds"), new byte[] { 0xDD });
        // Add a non-i_button file to verify the glob filter excludes it.
        File.WriteAllBytes(Path.Combine(unitsDir, "splash_screen.dds"), new byte[] { 0xDD });

        var settings = new V2Settings { IconsRoot = _tmpDir };
        var vm = new SettingsTabViewModel(settings);
        vm.IconsRootStatus.Should().Contain("Found 3 icons",
            because: "badge counts i_button_*.dds files (3 here, splash_screen.dds excluded by the glob)");
        // iter-312 dropped the "restart editor" requirement via live hot-swap;
        // badge now confirms the Spawning tab updates immediately on edit.
        vm.IconsRootStatus.Should().Contain("updates live",
            because: "iter-312 hot-swap shipped — operator no longer needs to restart editor for IconsRoot changes to take effect");
    }

    [Fact]
    public void IconsRootStatus_CountsAcrossMultipleCandidatePaths()
    {
        // iter-308 UnitIconResolver walks 5 candidate relpaths. Operators may
        // extract DDS files into ANY of them (or even at the root). Status
        // count must aggregate across all 5 so the badge agrees with what the
        // resolver would actually surface.
        var primary = Path.Combine(_tmpDir, "Data", "Art", "Textures", "Units");
        var fallback = Path.Combine(_tmpDir, "Art", "Textures");
        Directory.CreateDirectory(primary);
        Directory.CreateDirectory(fallback);
        File.WriteAllBytes(Path.Combine(primary, "i_button_A.dds"), new byte[] { 0x01 });
        File.WriteAllBytes(Path.Combine(primary, "i_button_B.dds"), new byte[] { 0x01 });
        File.WriteAllBytes(Path.Combine(fallback, "i_button_C.dds"), new byte[] { 0x01 });

        var settings = new V2Settings { IconsRoot = _tmpDir };
        var vm = new SettingsTabViewModel(settings);
        vm.IconsRootStatus.Should().Contain("Found 3 icons",
            because: "status walks all 5 candidate-relpaths the iter-308 resolver checks; aggregated count matches what the editor surfaces");
    }

    [Fact]
    public void BrowseIconsRootCommand_Exists_AndIsExecutable()
    {
        var settings = new V2Settings();
        var vm = new SettingsTabViewModel(settings);
        vm.BrowseIconsRootCommand.Should().NotBeNull(
            because: "XAML binds the Browse button to this command; null would silently no-op the click");
        vm.BrowseIconsRootCommand.CanExecute(null).Should().BeTrue(
            because: "command is always executable — the dialog itself handles the cancel-no-op case");
    }

    [Fact]
    public void IconsRoot_PropertyChanged_FiresStatusChange()
    {
        var settings = new V2Settings();
        var vm = new SettingsTabViewModel(settings);
        var statusChanged = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsTabViewModel.IconsRootStatus))
            {
                statusChanged = true;
            }
        };
        vm.IconsRoot = @"C:\new\path";
        statusChanged.Should().BeTrue(
            because: "TextBlock bound to IconsRootStatus must refresh when IconsRoot changes — without this the badge stays stale until window resize");
    }

    [Fact]
    public void IconsRoot_NoChange_DoesNotFireExtraNotifications()
    {
        var settings = new V2Settings { IconsRoot = @"C:\same" };
        var vm = new SettingsTabViewModel(settings);
        var changeCount = 0;
        vm.PropertyChanged += (_, _) => changeCount++;
        vm.IconsRoot = @"C:\same"; // identical value
        changeCount.Should().Be(0,
            because: "setter early-outs on equality so WPF binding doesn't re-render unnecessarily");
    }
}
