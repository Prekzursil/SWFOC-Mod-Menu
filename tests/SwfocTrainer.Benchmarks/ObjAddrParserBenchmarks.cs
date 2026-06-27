using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using SwfocTrainer.Core.Validation;

namespace SwfocTrainer.Benchmarks;

/// <summary>
/// Hot-path microbenchmarks for ObjAddrParser.
/// Inputs chosen to span the common operator-paste shapes seen in the editor:
///   - 16-char hex with 0x prefix (the most common; Inspector "Copy obj_addr (hex)" output)
///   - 19-char decimal (typical engine pointer in base-10)
///   - 16-char hex without prefix (less common; Tactical Units grid)
///   - whitespace-padded variants (operator paste from system clipboard)
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
public class ObjAddrParserBenchmarks
{
    private const string HexWithPrefix    = "0x14012A3B0";       // engine RVA + 0x140000000 base
    private const string HexNoPrefix      = "14012A3B0";
    private const string DecimalAddr      = "5369217456";        // same address in base 10
    private const string PaddedHex        = "   0x14012A3B0   ";
    private const string InvalidInput     = "garbage-not-an-address";

    [Benchmark(Baseline = true)]
    public long ParseHexWithPrefix()
    {
        var r = ObjAddrParser.TryParse(HexWithPrefix);
        return r.Addr;
    }

    [Benchmark]
    public long ParseHexNoPrefix()
    {
        var r = ObjAddrParser.TryParse(HexNoPrefix);
        return r.Addr;
    }

    [Benchmark]
    public long ParseDecimal()
    {
        var r = ObjAddrParser.TryParse(DecimalAddr);
        return r.Addr;
    }

    [Benchmark]
    public long ParsePaddedHex()
    {
        var r = ObjAddrParser.TryParse(PaddedHex);
        return r.Addr;
    }

    [Benchmark]
    public bool ParseInvalid_FastFail()
    {
        var r = ObjAddrParser.TryParse(InvalidInput);
        return r.Success;
    }
}
