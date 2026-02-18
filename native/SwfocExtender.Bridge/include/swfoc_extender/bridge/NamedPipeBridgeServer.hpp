#pragma once

#include <atomic>
#include <functional>
#include <string>
#include <thread>

namespace swfoc::extender::bridge {

struct BridgeCommand {
    std::string commandId;
    std::string featureId;
    std::string profileId;
    std::string mode;
    std::string requestedBy;
    std::string timestampUtc;
    std::string payloadJson;
};

struct BridgeResult {
    std::string commandId;
    bool succeeded {false};
    std::string reasonCode {"CAPABILITY_BACKEND_UNAVAILABLE"};
    std::string backend {"extender"};
    std::string hookState {"uninitialized"};
    std::string message {"Bridge not started."};
    std::string diagnosticsJson {"{}"};
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

    std::string pipeName_;
    Handler handler_;
    std::atomic<bool> running_ {false};
    std::thread worker_;
};

} // namespace swfoc::extender::bridge
