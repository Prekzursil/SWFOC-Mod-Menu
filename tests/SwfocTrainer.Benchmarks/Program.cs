using BenchmarkDotNet.Running;

namespace SwfocTrainer.Benchmarks;

internal static class Program
{
    public static void Main(string[] args)
    {
        // BenchmarkSwitcher reads --filter / --exporters / --artifacts from args.
        // The perf-benchmarker hat invokes:
        //   dotnet run -c Release -- --filter '*' --exporters json --artifacts <path>
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
