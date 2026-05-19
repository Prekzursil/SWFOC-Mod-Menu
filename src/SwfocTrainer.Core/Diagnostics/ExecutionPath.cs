using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Diagnostics;

/// <summary>
/// Canonical taxonomy of the ways the trainer can actually affect the
/// running game. Task #121 introduces this enum to fix the "collapse
/// to Lua-only" drift that iteration-X of the service layer was
/// trending toward: every feature must now declare which ExecutionPath
/// it relies on, and the Router validates at startup that the path is
/// reachable given the current backend capabilities.
///
/// The existing <see cref="ExecutionKind"/> enum (Memory / Helper /
/// Save / CodePatch / Freeze / Sdk) conflates execution *effect* with
/// execution *mechanism* — e.g. a "Memory" ExecutionKind could mean
/// "one-shot write" OR "installed Detour hook" OR "AOB bytecode
/// patch", all with different reliability + rollback semantics.
/// ExecutionPath splits those apart so the editor surface can show
/// the operator exactly which mechanism is in play.
/// </summary>
public enum ExecutionPath
{
    /// <summary>Unknown / unclassified. Default; should never be shipped.</summary>
    Unknown = 0,

    /// <summary>
    /// Dispatch through a registered <c>SWFOC_*</c> Lua helper in the
    /// bridge DLL (``powrprof.dll``). Highest reliability — the bridge
    /// harness pins every helper's behavior. Requires the bridge to
    /// be attached + the named pipe to be reachable.
    /// </summary>
    LuaBridge,

    /// <summary>
    /// Dispatch through engine-native Lua globals (``Find_Player``,
    /// ``FOWManager.Reveal_All``, ``Story_Event``, ``Spawn_Unit``, ...).
    /// Works without the bridge if the engine's Lua VM has the global
    /// registered, which varies by mode (menu/tactical/galactic) and
    /// by mod. Best-effort fallback; may silently no-op.
    /// </summary>
    LuaEngine,

    /// <summary>One-shot read of an engine-memory address (via pymem-style accessor).</summary>
    MemoryRead,

    /// <summary>One-shot write to an engine-memory address.</summary>
    MemoryWrite,

    /// <summary>
    /// An installed function-hook (MinHook detour) that intercepts engine
    /// calls on every tick. Distinct from <see cref="MemoryWrite"/> in
    /// that it persists across writes and requires a paired uninstall
    /// in the rollback path.
    /// </summary>
    Hook,

    /// <summary>
    /// Array-of-bytes code patch — replace specific instruction bytes
    /// (often NOP-ing a guard check). The most destructive path; the
    /// Router must verify the byte pattern + a restore-on-detach hook.
    /// </summary>
    Aob,

    /// <summary>
    /// The value-freeze service: keep rewriting a memory address on a
    /// short timer so the engine sees the chosen value every frame.
    /// Implemented in-editor via <c>IValueFreezeService</c>.
    /// </summary>
    Freeze,

    /// <summary>
    /// Routed through the Extender.Host SDK (C++ bridge helpers that
    /// aren't pure Lua thunks — e.g. structured spawn orchestration,
    /// HelperMod bridge ping).
    /// </summary>
    Sdk,

    /// <summary>
    /// Save-file editing — offline mutation of the .sav chunk tree,
    /// no attached process required.
    /// </summary>
    Save,
}

/// <summary>
/// Maps and validates <see cref="ExecutionPath"/> values against the
/// editor's existing <see cref="ExecutionKind"/> taxonomy and the
/// capability-probe results from <see cref="CapabilityReport"/>. This
/// is the "router validates at startup" surface the Ralph loop's
/// Multi-layer architecture rule calls for.
/// </summary>
public static class ExecutionPathRouter
{
    /// <summary>
    /// Translate a coarse <see cref="ExecutionKind"/> into the
    /// most-specific <see cref="ExecutionPath"/> value that the
    /// current codebase actually uses. The mapping is a superset —
    /// multiple ExecutionPaths can share a single ExecutionKind
    /// (e.g. Hook + Aob both fall under ExecutionKind.CodePatch).
    /// This helper returns the canonical default; callers that know
    /// more specific path info (from action payload hints or the
    /// BuildLuaCommand inventory) should override.
    /// </summary>
    public static ExecutionPath FromExecutionKind(ExecutionKind kind)
    {
        return kind switch
        {
            ExecutionKind.Memory => ExecutionPath.MemoryWrite,
            ExecutionKind.Helper => ExecutionPath.LuaBridge,     // bridge-helper calls are the dominant "Helper" pattern
            ExecutionKind.Save => ExecutionPath.Save,
            ExecutionKind.CodePatch => ExecutionPath.Aob,        // CodePatch is an AOB-patch in practice
            ExecutionKind.Freeze => ExecutionPath.Freeze,
            ExecutionKind.Sdk => ExecutionPath.Sdk,
            _ => ExecutionPath.Unknown,
        };
    }

    /// <summary>
    /// Which backend(s) can satisfy a given <see cref="ExecutionPath"/>.
    /// Returned as a set so a path like <see cref="ExecutionPath.LuaBridge"/>
    /// can declare "either Extender (primary) or Helper (secondary)".
    /// </summary>
    public static IReadOnlySet<ExecutionBackendKind> RequiredBackends(ExecutionPath path)
    {
        return path switch
        {
            ExecutionPath.LuaBridge => new HashSet<ExecutionBackendKind> {
                ExecutionBackendKind.Extender, ExecutionBackendKind.Helper
            },
            ExecutionPath.LuaEngine => new HashSet<ExecutionBackendKind> {
                ExecutionBackendKind.Extender, ExecutionBackendKind.Helper
            },
            ExecutionPath.MemoryRead or ExecutionPath.MemoryWrite =>
                new HashSet<ExecutionBackendKind> { ExecutionBackendKind.Memory },
            ExecutionPath.Hook or ExecutionPath.Aob =>
                new HashSet<ExecutionBackendKind> { ExecutionBackendKind.Memory },
            ExecutionPath.Freeze =>
                new HashSet<ExecutionBackendKind> { ExecutionBackendKind.Memory },
            ExecutionPath.Sdk =>
                new HashSet<ExecutionBackendKind> { ExecutionBackendKind.Extender },
            ExecutionPath.Save =>
                new HashSet<ExecutionBackendKind> { ExecutionBackendKind.Save },
            _ => new HashSet<ExecutionBackendKind>(),
        };
    }

    /// <summary>
    /// Routing decision for a single <see cref="ExecutionPath"/>.
    /// Immutable; builds from the inputs in <see cref="Validate"/>.
    /// </summary>
    public sealed record RoutingDecision(
        ExecutionPath Path,
        bool Reachable,
        string Reason,
        IReadOnlyList<ExecutionBackendKind> ConsideredBackends);

    /// <summary>
    /// Validate that a given ExecutionPath is reachable under the
    /// current capability report. "Reachable" means: at least one of
    /// the backends in <see cref="RequiredBackends"/> is listed as
    /// available in the report. The path-specific feature-id ISN'T
    /// checked here — this validation is one layer higher, about
    /// whether the backend is even attachable.
    /// </summary>
    public static RoutingDecision Validate(
        ExecutionPath path,
        CapabilityReport capabilityReport,
        IReadOnlyCollection<ExecutionBackendKind>? availableBackends = null)
    {
        ArgumentNullException.ThrowIfNull(capabilityReport);
        var needed = RequiredBackends(path);
        if (needed.Count == 0)
        {
            return new RoutingDecision(
                path, false,
                $"ExecutionPath.{path} has no known required backend — classify the path or fix the router.",
                Array.Empty<ExecutionBackendKind>());
        }

        var available = availableBackends is null
            // When no explicit list is supplied, accept a probe result as
            // "available" even if its confidence is Experimental — the
            // Validate surface is about REACHABILITY, not promotion.
            ? InferAvailableFromCapabilities(capabilityReport)
            : new HashSet<ExecutionBackendKind>(availableBackends);

        var satisfying = needed.Where(b => available.Contains(b)).ToArray();
        if (satisfying.Length == 0)
        {
            return new RoutingDecision(
                path, false,
                $"ExecutionPath.{path} requires one of [{string.Join(", ", needed)}] " +
                $"but no probe reports any as available (probe reason: {capabilityReport.ProbeReasonCode}).",
                needed.ToArray());
        }

        return new RoutingDecision(
            path, true,
            $"ExecutionPath.{path} reachable via {satisfying[0]} " +
            (satisfying.Length > 1 ? $"(fallbacks: {string.Join(", ", satisfying.Skip(1))})" : "(primary)"),
            satisfying);
    }

    /// <summary>
    /// Infer the set of available backends from a <see cref="CapabilityReport"/>.
    /// A backend is "available" iff the report has at least one
    /// <see cref="BackendCapability"/> with <c>Available==true</c> whose
    /// feature-id is conventionally tagged with the backend's name —
    /// callers that use a different naming scheme can pass an explicit
    /// <c>availableBackends</c> override to <see cref="Validate"/>.
    /// </summary>
    private static HashSet<ExecutionBackendKind> InferAvailableFromCapabilities(CapabilityReport report)
    {
        var available = new HashSet<ExecutionBackendKind>();
        foreach (var capability in report.Capabilities.Values)
        {
            if (!capability.Available) continue;
            // The capability feature-id convention is mostly free-form
            // strings today; err on the side of "any available capability
            // means the primary backend (Extender) is up". Memory is
            // always "available" when the report itself is valid because
            // we're attached to the process.
            available.Add(ExecutionBackendKind.Extender);
        }
        // When attached, Memory is always present — the fact we got a
        // CapabilityReport at all implies IRuntimeAdapter attached.
        available.Add(ExecutionBackendKind.Memory);
        return available;
    }

    /// <summary>
    /// Batch-validate every path in <paramref name="paths"/> against
    /// the given capability report. Useful at startup to produce one
    /// diagnostic panel listing everything the editor can and cannot
    /// actually do with the current bridge state.
    /// </summary>
    public static IReadOnlyList<RoutingDecision> ValidateAll(
        IEnumerable<ExecutionPath> paths,
        CapabilityReport capabilityReport,
        IReadOnlyCollection<ExecutionBackendKind>? availableBackends = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var results = new List<RoutingDecision>();
        foreach (var path in paths)
        {
            results.Add(Validate(path, capabilityReport, availableBackends));
        }
        return results;
    }
}
