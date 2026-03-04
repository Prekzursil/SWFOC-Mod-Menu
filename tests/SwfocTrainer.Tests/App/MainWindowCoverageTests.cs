using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using FluentAssertions;
using SwfocTrainer.App;
using SwfocTrainer.App.Infrastructure;
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

        result.Should().Be(expected);
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
