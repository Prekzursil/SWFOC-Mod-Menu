#pragma once

#include <map>
#include <string>

namespace swfoc::extender::bridge::host_json {

std::string EscapeJson(const std::string& value);
std::string ToDiagnosticsJson(const std::map<std::string, std::string>& values);
bool TryReadBool(const std::string& payloadJson, const std::string& key, bool& value);
bool TryReadInt(const std::string& payloadJson, const std::string& key, int& value);
std::string ExtractStringValue(const std::string& json, const std::string& key);
std::map<std::string, std::string> ExtractStringMap(const std::string& json, const std::string& key);

} // namespace swfoc::extender::bridge::host_json
