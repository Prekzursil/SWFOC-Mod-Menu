namespace SwfocTrainer.Core.V2Vm;

/// <summary>
/// 2026-05-07 (iter 308, Thread D arc FINALE) — row model used by the
/// Spawning tab ListBox to render unit-type entries with optional in-game
/// icon thumbnails. Mirrors the existing <c>FilteredTypes</c> string list
/// 1:1 — when a row's <see cref="IconPath"/> is null, the WPF Image control
/// bound to it stays hidden via standard null-binding behavior.
///
/// The split between TypeId (string) and IconPath (string?) keeps icon
/// lookup orthogonal to the existing filter / search / domain logic, which
/// stays string-keyed inside <c>SpawningTabState</c>. iter-308 added the
/// row collection as a parallel UI-only projection — Core/state code didn't
/// change.
/// </summary>
public sealed record UnitTypeRow(string TypeId, string? IconPath);
