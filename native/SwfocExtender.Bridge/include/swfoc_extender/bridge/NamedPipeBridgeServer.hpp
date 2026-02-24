// cppcheck-suppress-file missingIncludeSystem
// cppcheck-suppress-file unusedStructMember
#pragma once

#include <atomic>
#include <cstdint>
#include <functional>
#include <map>
#include <string>
#include <thread>

/*
Cppcheck note (targeted): if cppcheck runs without STL include paths,
suppress only this header:
  --suppress=missingIncludeSystem:native/SwfocExtender.Bridge/include/swfoc_extender/bridge/NamedPipeBridgeServer.hpp
*/

namespace swfoc::extender::bridge {

struct BridgeCommand {
    [[maybe_unused]] std::string commandId;
    [[maybe_unused]] std::string featureId;
    [[maybe_unused]] std::string profileId;
    [[maybe_unused]] std::string mode;
    [[maybe_unused]] std::string requestedBy;
    [[maybe_unused]] std::string timestampUtc;
    [[maybe_unused]] std::string payloadJson;
    [[maybe_unused]] std::int32_t processId {0};
    [[maybe_unused]] std::string processName;
    [[maybe_unused]] std::map<std::string, std::string> resolvedAnchors {};
};

struct BridgeResult {
    [[maybe_unused]] std::string commandId;
    [[maybe_unused]] bool succeeded {false};
    [[maybe_unused]] std::string reasonCode {"CAPABILITY_BACKEND_UNAVAILABLE"};
    [[maybe_unused]] std::string backend {"extender"};
    [[maybe_unused]] std::string hookState {"uninitialized"};
    [[maybe_unused]] std::string message {"Bridge not started."};
    [[maybe_unused]] std::string diagnosticsJson {"{}"};
};

class NamedPipeBridgeServer {
public:
    using Handler = std::function<BridgeResult(const BridgeCommand&)>;

    explicit NamedPipeBridgeServer(std::string pipeName);

    void setHandler(Handler handler);
    bool start();
    void stop();
    bool running() const noexcept;

private:
    void runLoop();
    BridgeResult handleRawCommand(const std::string& jsonLine) const;

    [[maybe_unused]] std::string pipeName_;
    Handler handler_;
    std::atomic<bool> running_ {false};
    std::thread worker_;
};

} // namespace swfoc::extender::bridge
