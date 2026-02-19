# SWFOC Native Extender (vNext Skeleton)

This tree hosts the in-process native extender layers for the vNext architecture:

- `SwfocExtender.Core`: hook lifecycle manager + capability probe model.
- `SwfocExtender.Bridge`: named-pipe command bridge contract surface.
- `SwfocExtender.Overlay`: in-game overlay state model.
- `SwfocExtender.Plugins`: feature plugin contracts and first economy plugin skeleton.

## Build (Windows, CMake)

```powershell
cmake -S native -B native/build -DCMAKE_BUILD_TYPE=Release
cmake --build native/build --config Release
```

This is a contract-first skeleton for Phase 1/2 enablement. Runtime bridge and hook installers are intentionally conservative and fail-closed by default.

## Host Process

`SwfocExtender.Host` is the bridge host executable. It listens on pipe `SwfocExtenderBridge` by default and currently handles:

- `health`
- `probe_capabilities`
- `set_credits` (one-shot / lock semantics with `lockCredits` and legacy `forcePatchHook` payload alias)

Override pipe name with:

```powershell
$env:SWFOC_EXTENDER_PIPE_NAME = "SwfocExtenderBridge"
```
