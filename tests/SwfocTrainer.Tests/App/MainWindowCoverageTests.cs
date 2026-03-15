using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using FluentAssertions;
using SwfocTrainer.App;
using SwfocTrainer.App.Infrastructure;
using SwfocTrainer.App.ViewModels;
using Xunit;

namespace SwfocTrainer.Tests.App;

public sealed class MainWindowCoverageTests
{
    [Theory]
    [InlineData(System.Windows.Input.Key.None, "")]
    [InlineData(System.Windows.Input.Key.D3, "3")]
    [InlineData(System.Windows.Input.Key.NumPad7, "7")]
    [InlineData(System.Windows.Input.Key.F5, "F5")]
    public void NormalizeGesture_ShouldProduceExpectedToken(System.Windows.Input.Key key, string expected)
    {
        var result = RunOnSta(() =>
        {
            var args = new System.Windows.Input.KeyEventArgs(
                System.Windows.Input.Keyboard.PrimaryDevice,
                new TestPresentationSource(),
                0,
                key);

            return (string?)typeof(MainWindow)
                .GetMethod("NormalizeGesture", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object?[] { args });
        });

        if (key == System.Windows.Input.Key.None)
        {
            result.Should().BeEmpty();
            return;
        }

        result.Should().NotBeNullOrWhiteSpace();
        result!.Split('+').Last().Should().Be(expected);
    }

    [Fact]
    public void OnPreviewKeyDown_ShouldReturn_WhenSenderIsNotMainWindow()
    {
        var method = typeof(MainWindow).GetMethod("OnPreviewKeyDown", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        RunOnSta(() =>
        {
            var act = () => method!.Invoke(null, new object?[] { new object(), null! });
            act.Should().NotThrow();
            return true;
        });
    }

    [Fact]
    public void MainWindowConstructor_ShouldAssignViewModelAsDataContext()
    {
        RunOnSta(() =>
        {
            var vm = new MainViewModel(CreateNullDependencies());
            var window = new MainWindow(vm);

            window.DataContext.Should().BeSameAs(vm);
            return true;
        });
    }

    [Fact]
    public void OnPreviewKeyDown_ShouldReturn_WhenWindowDataContextIsNotMainViewModel()
    {
        var method = typeof(MainWindow).GetMethod("OnPreviewKeyDown", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        RunOnSta(() =>
        {
            var vm = new MainViewModel(CreateNullDependencies());
            var window = new MainWindow(vm)
            {
                DataContext = new object()
            };
            var args = new System.Windows.Input.KeyEventArgs(
                System.Windows.Input.Keyboard.PrimaryDevice,
                new TestPresentationSource(),
                0,
                System.Windows.Input.Key.F2);

            var act = () => method!.Invoke(null, new object?[] { window, args });
            act.Should().NotThrow();
            args.Handled.Should().BeFalse();
            return true;
        });
    }

    [Fact]
    public void AsyncCommand_ShouldRespectCanExecutePredicate()
    {
        var executed = false;
        var command = new AsyncCommand(() =>
        {
            executed = true;
            return Task.CompletedTask;
        }, () => false);

        command.CanExecute(null).Should().BeFalse();
        command.Execute(null);
        executed.Should().BeFalse();
    }

    [Fact]
    public void AsyncCommand_ShouldToggleCanExecute_WhileTaskRuns()
    {
        RunOnSta(() =>
        {
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var command = new AsyncCommand(async () =>
            {
                await gate.Task.ConfigureAwait(false);
            });

            command.CanExecute(null).Should().BeTrue();
            command.Execute(null);
            WaitUntil(() => !command.CanExecute(null), TimeSpan.FromSeconds(1), "command should disable while an execution is in flight");

            gate.SetResult(true);
            WaitUntil(() => command.CanExecute(null), TimeSpan.FromSeconds(1), "command should re-enable after task completion");

            AsyncCommand.RaiseCanExecuteChanged();
            return true;
        });
    }

    private static void WaitUntil(Func<bool> predicate, TimeSpan timeout, string because)
    {
        SpinWait.SpinUntil(predicate, timeout).Should().BeTrue(because);
    }

    private static T RunOnSta<T>(Func<T> func)
    {
        T? result = default;
        ExceptionDispatchInfo? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = func();
            }
            catch (Exception ex)
            {
                captured = ExceptionDispatchInfo.Capture(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        captured?.Throw();
        return result!;
    }

    private static MainViewModelDependencies CreateNullDependencies()
    {
        return new MainViewModelDependencies
        {
            Profiles = null!,
            ProcessLocator = null!,
            LaunchContextResolver = null!,
            ProfileVariantResolver = null!,
            GameLauncher = null!,
            Runtime = null!,
            Orchestrator = null!,
            Catalog = null!,
            SaveCodec = null!,
            SavePatchPackService = null!,
            SavePatchApplyService = null!,
            Helper = null!,
            Updates = null!,
            ModOnboarding = null!,
            ModCalibration = null!,
            SupportBundles = null!,
            Telemetry = null!,
            FreezeService = null!,
            ActionReliability = null!,
            SelectedUnitTransactions = null!,
            SpawnPresets = null!
        };
    }

    private sealed class TestPresentationSource : PresentationSource
    {
        public override Visual RootVisual
        {
            get => null!;
            set { }
        }

        public override bool IsDisposed => false;

        protected override CompositionTarget GetCompositionTargetCore()
        {
            return null!;
        }
    }
}
