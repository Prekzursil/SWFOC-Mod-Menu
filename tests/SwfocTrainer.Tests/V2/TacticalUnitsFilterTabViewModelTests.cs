using FluentAssertions;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Ux;
using SwfocTrainer.Core.V2Vm;
using Xunit;

namespace SwfocTrainer.Tests.V2;

/// <summary>
/// Phase 1 thread A — tests for the App-side INPC wrapper around
/// <see cref="TacticalUnitsFilterTabState"/>. The wrapper's job is to
/// project the Core state's filtered rows into an
/// <c>ObservableCollection</c> the WPF DataGrid can bind, and to fire
/// PropertyChanged on each filter input.
/// </summary>
public sealed class TacticalUnitsFilterTabViewModelTests
{
    private sealed class StubDispatcher : ITacticalUnitsListDispatcher
    {
        public IReadOnlyList<TacticalUnitRow> NextRows { get; set; } =
            Array.Empty<TacticalUnitRow>();
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<TacticalUnitRow>> ListTacticalUnitsAsync(
            CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(NextRows);
        }
    }

    private static TacticalUnitsFilterTabViewModel BuildVm(StubDispatcher dispatcher)
        => new(dispatcher, new RecordingFeedbackSink());

    private static TacticalUnitRow Row(long addr, int slot, bool selected = false)
        => new(addr, slot, Hull: 100f, InvulnFlag: 0, PreventDeath: 0,
               IsLocal: false, IsSelected: selected);

    [Fact]
    public async Task RefreshCommand_PopulatesFilteredRowsFromDispatcher()
    {
        var dispatcher = new StubDispatcher
        {
            NextRows = new[] { Row(0x1000, 0), Row(0x2000, 1) }
        };
        var vm = BuildVm(dispatcher);

        // Drive the command body directly — exercising AsyncRelayCommand requires
        // a WPF dispatcher loop, but the underlying RefreshAsyncCore is reachable
        // via Refresh + recompute path.
        vm.RefreshCommand.CanExecute(null).Should().BeTrue();

        // Drive Refresh through the public state's RefreshAsync via a re-entry
        // path — calling the command's Execute synchronously fires-and-forgets
        // through Task.Run; tests use the recording-stub-friendly path.
        await SwfocTrainer.Tests.V2.AsyncCommandPump.PumpAsync(vm.RefreshCommand);

        vm.FilteredRows.Should().HaveCount(2);
        vm.TotalRowCount.Should().Be(2);
        vm.FilteredRowCount.Should().Be(2);
        dispatcher.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task FactionSlotFilterText_FiltersByOwnerSlot()
    {
        var dispatcher = new StubDispatcher
        {
            NextRows = new[] { Row(0x1000, 0), Row(0x2000, 1), Row(0x3000, 1) }
        };
        var vm = BuildVm(dispatcher);
        await SwfocTrainer.Tests.V2.AsyncCommandPump.PumpAsync(vm.RefreshCommand);

        vm.FactionSlotFilterText = "1";
        vm.FilteredRows.Select(r => r.OwnerSlot).Should().BeEquivalentTo(new[] { 1, 1 });
    }

    [Fact]
    public async Task FactionSlotFilterText_BlankResetsToAllRows()
    {
        var dispatcher = new StubDispatcher
        {
            NextRows = new[] { Row(0x1000, 0), Row(0x2000, 1) }
        };
        var vm = BuildVm(dispatcher);
        await SwfocTrainer.Tests.V2.AsyncCommandPump.PumpAsync(vm.RefreshCommand);

        vm.FactionSlotFilterText = "1";
        vm.FilteredRows.Should().HaveCount(1);

        vm.FactionSlotFilterText = string.Empty;
        vm.FilteredRows.Should().HaveCount(2);
    }

    [Fact]
    public async Task TextFilter_MatchesObjAddrHexCaseInsensitive()
    {
        var dispatcher = new StubDispatcher
        {
            NextRows = new[] { Row(0x1ABC, 0), Row(0x2000, 1) }
        };
        var vm = BuildVm(dispatcher);
        await SwfocTrainer.Tests.V2.AsyncCommandPump.PumpAsync(vm.RefreshCommand);

        vm.TextFilter = "abc";
        vm.FilteredRows.Should().ContainSingle();
        vm.FilteredRows[0].ObjAddr.Should().Be(0x1ABCL);
    }

    [Fact]
    public async Task SelectedOnlyFilter_HidesUnselectedRows()
    {
        var dispatcher = new StubDispatcher
        {
            NextRows = new[]
            {
                Row(0x1000, 0, selected: true),
                Row(0x2000, 1, selected: false),
            }
        };
        var vm = BuildVm(dispatcher);
        await SwfocTrainer.Tests.V2.AsyncCommandPump.PumpAsync(vm.RefreshCommand);

        vm.SelectedOnlyFilter = true;
        vm.FilteredRows.Should().ContainSingle();
        vm.FilteredRows[0].IsSelected.Should().BeTrue();
    }

    [Fact]
    public void FilterPropertySetters_FirePropertyChanged()
    {
        var vm = BuildVm(new StubDispatcher());
        var changes = new List<string>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName ?? string.Empty);

        vm.FactionSlotFilterText = "2";
        vm.TextFilter = "abc";
        vm.SelectedOnlyFilter = true;

        changes.Should().Contain(new[]
        {
            nameof(TacticalUnitsFilterTabViewModel.FactionSlotFilterText),
            nameof(TacticalUnitsFilterTabViewModel.TextFilter),
            nameof(TacticalUnitsFilterTabViewModel.SelectedOnlyFilter),
        });
    }
}
