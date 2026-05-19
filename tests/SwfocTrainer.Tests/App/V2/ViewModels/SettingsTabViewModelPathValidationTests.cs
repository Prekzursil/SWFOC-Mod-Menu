using System;
using System.IO;
using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.ViewModels;

/// <summary>
/// Tests for the 2026-04-27 path-validation badges on
/// <see cref="SettingsTabViewModel"/> (<c>GamePathStatus</c> /
/// <c>LogPathStatus</c>). Empty string for valid paths; short reason
/// otherwise.
/// </summary>
/// <remarks>
/// The Settings VM holds a reference to a <see cref="V2Settings"/>; we
/// build one with synthetic paths and assert the computed badges.
/// </remarks>
public sealed class SettingsTabViewModelPathValidationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _existingFile;
    private readonly string _missingFile;
    private readonly string _missingDir;

    public SettingsTabViewModelPathValidationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"swfoc-settings-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _existingFile = Path.Combine(_tempDir, "fake-StarWarsG.exe");
        File.WriteAllText(_existingFile, "test stub");
        _missingFile = Path.Combine(_tempDir, "definitely-missing.exe");
        _missingDir = Path.Combine(_tempDir, "no-such-subdir", "missing.exe");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public void GamePathStatus_Empty_RendersEmptyMarker()
    {
        var settings = new V2Settings { GamePath = string.Empty };
        var vm = new SettingsTabViewModel(settings);
        vm.GamePathStatus.Should().Be("(empty)");
    }

    [Fact]
    public void GamePathStatus_ValidExistingFile_RendersEmptyString()
    {
        var settings = new V2Settings { GamePath = _existingFile };
        var vm = new SettingsTabViewModel(settings);
        vm.GamePathStatus.Should().BeEmpty(
            "an existing file path is valid; the badge should collapse");
    }

    [Fact]
    public void GamePathStatus_MissingFile_FlagsFileNotFoundInExpectedDirectory()
    {
        // Parent directory exists, but the file doesn't.
        var settings = new V2Settings { GamePath = _missingFile };
        var vm = new SettingsTabViewModel(settings);
        vm.GamePathStatus.Should()
            .Contain("file not found", "directory exists but file does not");
    }

    [Fact]
    public void GamePathStatus_MissingDirectory_FlagsDirectoryMissing()
    {
        var settings = new V2Settings { GamePath = _missingDir };
        var vm = new SettingsTabViewModel(settings);
        vm.GamePathStatus.Should().Be("(directory missing)");
    }

    [Fact]
    public void LogPathStatus_Empty_RendersEmptyMarker()
    {
        var settings = new V2Settings { LogPath = string.Empty };
        var vm = new SettingsTabViewModel(settings);
        vm.LogPathStatus.Should().Be("(empty)");
    }

    [Fact]
    public void LogPathStatus_ValidParentDirectory_RendersEmptyString()
    {
        // Log path doesn't have to exist (bridge creates it on first probe);
        // we just need the parent directory to exist.
        var logFile = Path.Combine(_tempDir, "bridge-not-yet-created.log");
        var settings = new V2Settings { LogPath = logFile };
        var vm = new SettingsTabViewModel(settings);
        vm.LogPathStatus.Should().BeEmpty();
    }

    [Fact]
    public void LogPathStatus_MissingParentDirectory_FlagsParentMissing()
    {
        var settings = new V2Settings { LogPath = _missingDir };
        var vm = new SettingsTabViewModel(settings);
        vm.LogPathStatus.Should().Be("(parent directory missing)");
    }
}
