// cppcheck-suppress-file missingIncludeSystem
#include <cctype>
#include <map>
#include <sstream>
#include <string>

namespace swfoc::extender::bridge::host_json {
namespace {

bool TryFindValueStart(const std::string& payloadJson, const std::string& key, std::size_t& start) {
    const auto quotedKey = "\"" + key + "\"";
    const auto keyPos = payloadJson.find(quotedKey);
    if (keyPos == std::string::npos) {
        return false;
    }

    const auto colonPos = payloadJson.find(':', keyPos + quotedKey.size());
    if (colonPos == std::string::npos) {
        return false;
    }

    start = payloadJson.find_first_not_of(" \t\r\n", colonPos + 1);
    return start != std::string::npos;
}

bool TryParseIntFromText(const std::string& valueText, int& value) {
    try {
        std::size_t consumed = 0;
        value = std::stoi(valueText, &consumed);
        return consumed != 0;
    } catch (...) {
        return false;
    }
}

std::string ExtractObjectJson(const std::string& json, const std::string& key) {
    const auto quotedKey = "\"" + key + "\"";
    auto keyPos = json.find(quotedKey);
    if (keyPos == std::string::npos) {
        return "{}";
    }

    auto colonPos = json.find(':', keyPos + quotedKey.length());
    if (colonPos == std::string::npos) {
        return "{}";
    }

    auto openBrace = json.find('{', colonPos + 1);
    if (openBrace == std::string::npos) {
        return "{}";
    }

    auto depth = 0;
    for (std::size_t i = openBrace; i < json.size(); ++i) {
        if (json[i] == '{') {
            ++depth;
        } else if (json[i] == '}') {
            --depth;
            if (depth == 0) {
                return json.substr(openBrace, i - openBrace + 1);
            }
        }
    }

    return "{}";
}

std::size_t FindUnescapedQuote(const std::string& value, std::size_t start) {
    auto escaped = false;
    for (std::size_t i = start; i < value.size(); ++i) {
        if (escaped) {
            escaped = false;
            continue;
        }

        if (value[i] == '\\') {
            escaped = true;
            continue;
        }

        if (value[i] == '"') {
            return i;
        }
    }

    return std::string::npos;
}

std::string TrimAsciiWhitespace(std::string value) {
    auto first = value.begin();
    while (first != value.end() && std::isspace(static_cast<unsigned char>(*first)) != 0) {
        ++first;
    }

    auto last = value.end();
    while (last != first && std::isspace(static_cast<unsigned char>(*(last - 1))) != 0) {
        --last;
    }

    return std::string(first, last);
}

std::size_t SkipAsciiWhitespace(const std::string& value, std::size_t cursor) {
    return value.find_first_not_of(" \t\r\n", cursor);
}

bool TryParseFlatStringMapEntryKey(const std::string& objectJson, std::size_t& cursor, std::string& key);
bool TryParseFlatStringMapEntryValue(const std::string& objectJson, std::size_t& cursor, std::string& value);

bool TryParseFlatStringMapEntry(
    const std::string& objectJson,
    std::size_t& cursor,
    std::string& key,
    std::string& value) {
    cursor = SkipAsciiWhitespace(objectJson, cursor);
    if (cursor == std::string::npos || objectJson[cursor] == '}') {
        return false;
    }

    if (!TryParseFlatStringMapEntryKey(objectJson, cursor, key)) {
        return false;
    }

    if (!TryParseFlatStringMapEntryValue(objectJson, cursor, value)) {
        return false;
    }

    cursor = SkipAsciiWhitespace(objectJson, cursor);
    if (cursor != std::string::npos && objectJson[cursor] == ',') {
        ++cursor;
    }

    return true;
}

bool TryParseFlatStringMapEntryKey(const std::string& objectJson, std::size_t& cursor, std::string& key) {
    if (objectJson[cursor] != '"') {
        return false;
    }

    const auto keyEnd = FindUnescapedQuote(objectJson, cursor + 1);
    if (keyEnd == std::string::npos) {
        return false;
    }

    key = objectJson.substr(cursor + 1, keyEnd - cursor - 1);
    cursor = objectJson.find(':', keyEnd + 1);
    if (cursor == std::string::npos) {
        return false;
    }

    ++cursor;
    cursor = SkipAsciiWhitespace(objectJson, cursor);
    return cursor != std::string::npos;
}

bool TryParseFlatStringMapEntryValue(const std::string& objectJson, std::size_t& cursor, std::string& value) {
    if (objectJson[cursor] == '"') {
        const auto valueEnd = FindUnescapedQuote(objectJson, cursor + 1);
        if (valueEnd == std::string::npos) {
            return false;
        }

        value = objectJson.substr(cursor + 1, valueEnd - cursor - 1);
        cursor = valueEnd + 1;
    } else {
        auto tokenEnd = cursor;
        while (tokenEnd < objectJson.size() && objectJson[tokenEnd] != ',' && objectJson[tokenEnd] != '}') {
            ++tokenEnd;
        }

        value = TrimAsciiWhitespace(objectJson.substr(cursor, tokenEnd - cursor));
        cursor = tokenEnd;
    }

    return true;
}

std::map<std::string, std::string> ParseFlatStringMapObject(const std::string& objectJson) {
    std::map<std::string, std::string> parsed;
    auto cursor = objectJson.find('{');
    if (cursor == std::string::npos) {
        return parsed;
    }

    ++cursor;
    std::string key;
    std::string value;
    while (TryParseFlatStringMapEntry(objectJson, cursor, key, value)) {
        if (!key.empty()) {
            parsed[key] = value;
        }
    }

    return parsed;
}

} // namespace

std::string EscapeJson(const std::string& value) {
    std::string escaped;
    escaped.reserve(value.size() + 8);
    for (const auto ch : value) {
        switch (ch) {
        case '\\':
            escaped += "\\\\";
            break;
        case '"':
            escaped += "\\\"";
            break;
        case '\n':
            escaped += "\\n";
            break;
        case '\r':
            escaped += "\\r";
            break;
        case '\t':
            escaped += "\\t";
            break;
        default:
            escaped.push_back(ch);
            break;
        }
    }

    return escaped;
}

std::string ToDiagnosticsJson(const std::map<std::string, std::string>& values) {
    std::ostringstream out;
    out << '{';
    auto first = true;
    for (const auto& [key, value] : values) {
        if (!first) {
            out << ',';
        }
        first = false;
        out << '"' << EscapeJson(key) << "\":\"" << EscapeJson(value) << '"';
    }
    out << '}';
    return out.str();
}

bool TryReadBool(const std::string& payloadJson, const std::string& key, bool& value) {
    std::size_t start = 0;
    if (!TryFindValueStart(payloadJson, key, start)) {
        return false;
    }

    if (payloadJson.compare(start, 4, "true") == 0) {
        value = true;
        return true;
    }

    if (payloadJson.compare(start, 5, "false") == 0) {
        value = false;
        return true;
    }

    return false;
}

bool TryReadInt(const std::string& payloadJson, const std::string& key, int& value) {
    std::size_t start = 0;
    if (!TryFindValueStart(payloadJson, key, start)) {
        return false;
    }

    if (payloadJson[start] == '+') {
        return false;
    }

    return TryParseIntFromText(payloadJson.substr(start), value);
}

std::string ExtractStringValue(const std::string& json, const std::string& key) {
    const auto quotedKey = "\"" + key + "\"";
    auto keyPos = json.find(quotedKey);
    if (keyPos == std::string::npos) {
        return {};
    }

    auto colonPos = json.find(':', keyPos + quotedKey.length());
    if (colonPos == std::string::npos) {
        return {};
    }

    auto firstQuote = json.find('"', colonPos + 1);
    if (firstQuote == std::string::npos) {
        return {};
    }

    auto secondQuote = json.find('"', firstQuote + 1);
    if (secondQuote == std::string::npos || secondQuote <= firstQuote) {
        return {};
    }

    return json.substr(firstQuote + 1, secondQuote - firstQuote - 1);
}

std::map<std::string, std::string> ExtractStringMap(const std::string& json, const std::string& key) {
    return ParseFlatStringMapObject(ExtractObjectJson(json, key));
}

} // namespace swfoc::extender::bridge::host_json
