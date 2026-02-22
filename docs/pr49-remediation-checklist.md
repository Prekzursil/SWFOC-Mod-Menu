# PR49 Remediation Checklist

_Generated from `tmp_codacy_pr49_issues.json` on 2026-02-22 10:51:47Z.

## Summary

- Total flagged issues: **134**
- Distinct files: **45**

| Rule | Count |
|---|---:|
| `markdownlint_MD032` | 34 |
| `cppcheck_missingIncludeSystem` | 28 |
| `SonarCSharp_S2360` | 27 |
| `psscriptanalyzer_psavoidusingwritehost` | 20 |
| `Lizard_ccn-medium` | 11 |
| `Lizard_nloc-medium` | 9 |
| `SonarCSharp_S3052` | 5 |

## Rule Buckets

### `markdownlint_MD032` (34)

- `README.md` (1)
- `TestResults/runs/20260218-213838/issue-19-evidence-template.md` (2)
- `TestResults/runs/20260218-213838/issue-34-evidence-template.md` (1)
- `docs/ARCHITECTURE.md` (1)
- `docs/EXTERNAL_TOOLS_SETUP.md` (7)
- `docs/LIVE_VALIDATION_RUNBOOK.md` (1)
- `docs/RESEARCH_GAME_WORKFLOW.md` (5)
- `profiles/default/sdk/maps/README.md` (1)
- `tools/research/build-fingerprint.md` (5)
- `tools/research/source-corpus.md` (10)

### `cppcheck_missingIncludeSystem` (28)

- `native/SwfocExtender.Bridge/include/swfoc_extender/bridge/NamedPipeBridgeServer.hpp` (4)
- `native/SwfocExtender.Bridge/src/BridgeHostMain.cpp` (9)
- `native/SwfocExtender.Bridge/src/NamedPipeBridgeServer.cpp` (5)
- `native/SwfocExtender.Core/include/swfoc_extender/core/CapabilityProbe.hpp` (2)
- `native/SwfocExtender.Core/include/swfoc_extender/core/HookLifecycleManager.hpp` (2)
- `native/SwfocExtender.Plugins/include/swfoc_extender/plugins/EconomyPlugin.hpp` (3)
- `native/SwfocExtender.Plugins/include/swfoc_extender/plugins/PluginContracts.hpp` (3)

### `SonarCSharp_S2360` (27)

- `src/SwfocTrainer.Core/Contracts/IBinaryFingerprintService.cs` (1)
- `src/SwfocTrainer.Core/Contracts/ICapabilityMapResolver.cs` (2)
- `src/SwfocTrainer.Core/Contracts/IExecutionBackend.cs` (2)
- `src/SwfocTrainer.Core/Contracts/IProfileVariantResolver.cs` (2)
- `src/SwfocTrainer.Core/Contracts/ISdkDiagnosticsSink.cs` (1)
- `src/SwfocTrainer.Core/Contracts/ISdkOperationRouter.cs` (1)
- `src/SwfocTrainer.Core/Contracts/ISdkRuntimeAdapter.cs` (1)
- `src/SwfocTrainer.Core/Models/BackendRoutingModels.cs` (1)
- `src/SwfocTrainer.Core/Services/NullSdkDiagnosticsSink.cs` (1)
- `src/SwfocTrainer.Core/Services/SdkOperationRouter.cs` (2)
- `src/SwfocTrainer.Runtime/Services/BinaryFingerprintService.cs` (1)
- `src/SwfocTrainer.Runtime/Services/CapabilityMapResolver.cs` (2)
- `src/SwfocTrainer.Runtime/Services/NamedPipeExtenderBackend.cs` (2)
- `src/SwfocTrainer.Runtime/Services/NoopSdkRuntimeAdapter.cs` (1)
- `src/SwfocTrainer.Runtime/Services/ProfileVariantResolver.cs` (6)
- `src/SwfocTrainer.Runtime/Services/RuntimeAdapter.cs` (1)

### `psscriptanalyzer_psavoidusingwritehost` (20)

- `tools/native/build-native.ps1` (6)
- `tools/native/resolve-cmake.ps1` (5)
- `tools/research/capture-binary-fingerprint.ps1` (1)
- `tools/research/extract-pe-metadata.ps1` (1)
- `tools/research/generate-signature-candidates.ps1` (1)
- `tools/research/normalize-signature-pack.ps1` (1)
- `tools/research/run-capability-intel.ps1` (4)
- `tools/run-live-validation.ps1` (1)

### `Lizard_ccn-medium` (11)

- `native/SwfocExtender.Bridge/src/BridgeHostMain.cpp` (2)
- `native/SwfocExtender.Bridge/src/NamedPipeBridgeServer.cpp` (1)
- `src/SwfocTrainer.Core/Services/SdkOperationRouter.cs` (3)
- `src/SwfocTrainer.Runtime/Services/BackendRouter.cs` (1)
- `src/SwfocTrainer.Runtime/Services/CapabilityMapResolver.cs` (1)
- `src/SwfocTrainer.Runtime/Services/ProfileVariantResolver.cs` (1)
- `src/SwfocTrainer.Runtime/Services/RuntimeModeProbeResolver.cs` (1)
- `tests/SwfocTrainer.Tests/Profiles/LiveRoeRuntimeHealthTests.cs` (1)

### `Lizard_nloc-medium` (9)

- `native/SwfocExtender.Bridge/src/BridgeHostMain.cpp` (1)
- `native/SwfocExtender.Bridge/src/NamedPipeBridgeServer.cpp` (1)
- `src/SwfocTrainer.Core/Services/SdkOperationRouter.cs` (1)
- `src/SwfocTrainer.Runtime/Services/BackendRouter.cs` (1)
- `src/SwfocTrainer.Runtime/Services/CapabilityMapResolver.cs` (1)
- `src/SwfocTrainer.Runtime/Services/NamedPipeExtenderBackend.cs` (1)
- `src/SwfocTrainer.Runtime/Services/ProfileVariantResolver.cs` (1)
- `src/SwfocTrainer.Runtime/Services/RuntimeModeProbeResolver.cs` (1)
- `tests/SwfocTrainer.Tests/Runtime/NamedPipeExtenderBackendTests.cs` (1)

### `SonarCSharp_S3052` (5)

- `src/SwfocTrainer.Runtime/Services/CapabilityMapResolver.cs` (5)

## Top Files by Issue Count

- `native/SwfocExtender.Bridge/src/BridgeHostMain.cpp`: 12
- `tools/research/source-corpus.md`: 10
- `src/SwfocTrainer.Runtime/Services/CapabilityMapResolver.cs`: 9
- `src/SwfocTrainer.Runtime/Services/ProfileVariantResolver.cs`: 8
- `native/SwfocExtender.Bridge/src/NamedPipeBridgeServer.cpp`: 7
- `docs/EXTERNAL_TOOLS_SETUP.md`: 7
- `src/SwfocTrainer.Core/Services/SdkOperationRouter.cs`: 6
- `tools/native/build-native.ps1`: 6
- `tools/native/resolve-cmake.ps1`: 5
- `docs/RESEARCH_GAME_WORKFLOW.md`: 5
- `tools/research/build-fingerprint.md`: 5
- `native/SwfocExtender.Bridge/include/swfoc_extender/bridge/NamedPipeBridgeServer.hpp`: 4
- `tools/research/run-capability-intel.ps1`: 4
- `native/SwfocExtender.Plugins/include/swfoc_extender/plugins/EconomyPlugin.hpp`: 3
- `native/SwfocExtender.Plugins/include/swfoc_extender/plugins/PluginContracts.hpp`: 3
- `src/SwfocTrainer.Runtime/Services/NamedPipeExtenderBackend.cs`: 3
- `src/SwfocTrainer.Core/Contracts/IProfileVariantResolver.cs`: 2
- `src/SwfocTrainer.Core/Contracts/IExecutionBackend.cs`: 2
- `src/SwfocTrainer.Core/Contracts/ICapabilityMapResolver.cs`: 2
- `native/SwfocExtender.Core/include/swfoc_extender/core/HookLifecycleManager.hpp`: 2
