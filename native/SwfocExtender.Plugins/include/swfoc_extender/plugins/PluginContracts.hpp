// cppcheck-suppress-file missingIncludeSystem
// cppcheck-suppress-file unusedStructMember
#pragma once

#include <cstdint>
#include <map>
#include <string>
#include <string_view>

/*
Cppcheck note (targeted): if cppcheck runs without STL include paths,
suppress only this header:
  --suppress=missingIncludeSystem:native/SwfocExtender.Plugins/include/swfoc_extender/plugins/PluginContracts.hpp
*/

namespace swfoc::extender::plugins {

using StringMap = StringMap;

struct PluginRequest {
    // --- Identity fields ---
    struct Identity {
        [[maybe_unused]] std::string featureId;
        [[maybe_unused]] std::string profileId;
        [[maybe_unused]] std::int32_t processId {0};

        Identity() = default;
        Identity(const Identity&) = default;
        Identity& operator=(const Identity&) = default;
        Identity(Identity&&) noexcept = default;
        Identity& operator=(Identity&&) noexcept = default;
    };

    // --- Scalar / toggle payload ---
    struct Payload {
        [[maybe_unused]] std::int32_t intValue {0};
        [[maybe_unused]] bool boolValue {false};
        [[maybe_unused]] bool enable {false};
        [[maybe_unused]] bool lockValue {false};
        [[maybe_unused]] bool allowCrossFaction {false};
        [[maybe_unused]] bool forceOverride {false};

        Payload() = default;
        Payload(const Payload&) = default;
        Payload& operator=(const Payload&) = default;
        Payload(Payload&&) noexcept = default;
        Payload& operator=(Payload&&) noexcept = default;
    };

    // --- Helper bridge metadata ---
    struct HelperBridge {
        [[maybe_unused]] std::string helperHookId {};
        [[maybe_unused]] std::string helperEntryPoint {};
        [[maybe_unused]] std::string helperScript {};
        [[maybe_unused]] std::string operationKind {};
        [[maybe_unused]] std::string operationToken {};
        [[maybe_unused]] std::string invocationContractVersion {};

        HelperBridge() = default;
        HelperBridge(const HelperBridge&) = default;
        HelperBridge& operator=(const HelperBridge&) = default;
        HelperBridge(HelperBridge&&) noexcept = default;
        HelperBridge& operator=(HelperBridge&&) noexcept = default;
    };

    // --- Entity context for spawn / placement operations ---
    struct EntityContext {
        [[maybe_unused]] std::string unitId {};
        [[maybe_unused]] std::string entityId {};
        [[maybe_unused]] std::string entryMarker {};
        [[maybe_unused]] std::string faction {};
        [[maybe_unused]] std::string targetFaction {};
        [[maybe_unused]] std::string sourceFaction {};
        [[maybe_unused]] std::string globalKey {};
        [[maybe_unused]] std::string populationPolicy {};
        [[maybe_unused]] std::string persistencePolicy {};
        [[maybe_unused]] std::string placementMode {};
        [[maybe_unused]] std::string worldPosition {};

        EntityContext() = default;
        EntityContext(const EntityContext&) = default;
        EntityContext& operator=(const EntityContext&) = default;
        EntityContext(EntityContext&&) noexcept = default;
        EntityContext& operator=(EntityContext&&) noexcept = default;
    };

    Identity identity {};
    Payload payload {};
    HelperBridge helperBridge {};
    EntityContext entityContext {};
    [[maybe_unused]] StringMap anchors {};

    // Convenience accessors — keep call-site code short
    [[nodiscard]] const std::string& featureId() const noexcept { return identity.featureId; }
    [[nodiscard]] const std::string& profileId() const noexcept { return identity.profileId; }
    [[nodiscard]] std::int32_t processId() const noexcept { return identity.processId; }

    [[nodiscard]] std::int32_t intValue() const noexcept { return payload.intValue; }
    [[nodiscard]] bool boolValue() const noexcept { return payload.boolValue; }
    [[nodiscard]] bool enable() const noexcept { return payload.enable; }
    [[nodiscard]] bool lockValue() const noexcept { return payload.lockValue; }
    [[nodiscard]] bool allowCrossFaction() const noexcept { return payload.allowCrossFaction; }
    [[nodiscard]] bool forceOverride() const noexcept { return payload.forceOverride; }

    [[nodiscard]] const std::string& helperHookId() const noexcept { return helperBridge.helperHookId; }
    [[nodiscard]] const std::string& helperEntryPoint() const noexcept { return helperBridge.helperEntryPoint; }
    [[nodiscard]] const std::string& helperScript() const noexcept { return helperBridge.helperScript; }
    [[nodiscard]] const std::string& operationKind() const noexcept { return helperBridge.operationKind; }
    [[nodiscard]] const std::string& operationToken() const noexcept { return helperBridge.operationToken; }
    [[nodiscard]] const std::string& invocationContractVersion() const noexcept { return helperBridge.invocationContractVersion; }

    [[nodiscard]] const std::string& unitId() const noexcept { return entityContext.unitId; }
    [[nodiscard]] const std::string& entityId() const noexcept { return entityContext.entityId; }
    [[nodiscard]] const std::string& entryMarker() const noexcept { return entityContext.entryMarker; }
    [[nodiscard]] const std::string& faction() const noexcept { return entityContext.faction; }
    [[nodiscard]] const std::string& targetFaction() const noexcept { return entityContext.targetFaction; }
    [[nodiscard]] const std::string& sourceFaction() const noexcept { return entityContext.sourceFaction; }
    [[nodiscard]] const std::string& globalKey() const noexcept { return entityContext.globalKey; }
    [[nodiscard]] const std::string& populationPolicy() const noexcept { return entityContext.populationPolicy; }
    [[nodiscard]] const std::string& persistencePolicy() const noexcept { return entityContext.persistencePolicy; }
    [[nodiscard]] const std::string& placementMode() const noexcept { return entityContext.placementMode; }
    [[nodiscard]] const std::string& worldPosition() const noexcept { return entityContext.worldPosition; }

    PluginRequest() = default;
    PluginRequest(const PluginRequest&) = default;
    PluginRequest& operator=(const PluginRequest&) = default;
    PluginRequest(PluginRequest&&) noexcept = default;
    PluginRequest& operator=(PluginRequest&&) noexcept = default;
};

struct CapabilityState {
    [[maybe_unused]] bool available {false};
    [[maybe_unused]] std::string state {"Unknown"};
    [[maybe_unused]] std::string reasonCode {"CAPABILITY_BACKEND_UNAVAILABLE"};
    [[maybe_unused]] StringMap diagnostics {};

    CapabilityState() = default;
    CapabilityState(const CapabilityState&) = default;
    CapabilityState& operator=(const CapabilityState&) = default;
    CapabilityState(CapabilityState&&) noexcept = default;
    CapabilityState& operator=(CapabilityState&&) noexcept = default;
};

struct CapabilitySnapshot {
    [[maybe_unused]] std::map<std::string, CapabilityState, std::less<>> features {};

    CapabilitySnapshot() = default;
    CapabilitySnapshot(const CapabilitySnapshot&) = default;
    CapabilitySnapshot& operator=(const CapabilitySnapshot&) = default;
    CapabilitySnapshot(CapabilitySnapshot&&) noexcept = default;
    CapabilitySnapshot& operator=(CapabilitySnapshot&&) noexcept = default;
};

struct PluginResult {
    [[maybe_unused]] bool succeeded {false};
    [[maybe_unused]] std::string reasonCode {"CAPABILITY_UNKNOWN"};
    [[maybe_unused]] std::string hookState {"none"};
    [[maybe_unused]] std::string message {};
    [[maybe_unused]] StringMap diagnostics {};

    PluginResult() = default;
    PluginResult(const PluginResult&) = default;
    PluginResult& operator=(const PluginResult&) = default;
    PluginResult(PluginResult&&) noexcept = default;
    PluginResult& operator=(PluginResult&&) noexcept = default;
};

class IPlugin {
public:
    virtual ~IPlugin() = default;
    virtual const char* id() const noexcept = 0;
    virtual PluginResult execute(const PluginRequest& request) = 0;
};

} // namespace swfoc::extender::plugins
