// cppcheck-suppress-file missingIncludeSystem
#include <cctype>
#include <map>
#include <sstream>
#include <stdexcept>
#include <string>
#include <string_view>

namespace swfoc::extender::bridge::host_json {
namespace {

bool TryFindValueStart(std::string_view payloadJson, std::string_view key, std::size_t& start) {
    auto quotedKey = std::string("\"");
    quotedKey.append(key);
    quotedKey.push_back('"');

    const auto keyPos = payloadJson.find(quotedKey);
    if (keyPos == std::string_view::npos) {
        return false;
    }

    const auto colonPos = payloadJson.find(':', keyPos + quotedKey.size());
    if (colonPos == std::string_view::npos) {
        return false;
    }

    start = payloadJson.find_first_not_of(" \t\r\n", colonPos + 1);
    return start != std::string_view::npos;
}

bool TryParseIntFromText(std::string_view valueText, int& value) {
    try {
        std::size_t consumed = 0;
        value = std::stoi(std::string(valueText), &consumed);
        return consumed != 0;
    } catch (const std::exception&) {
        return false;
    }
}

std::string ExtractObjectJson(std::string_view json, std::string_view key) {
    auto quotedKey = std::string("\"");
    quotedKey.append(key);
    quotedKey.push_back('"');

    const auto keyPos = json.find(quotedKey);
    if (keyPos == std::string_view::npos) {
        return "{}";
    }

    const auto colonPos = json.find(':', keyPos + quotedKey.length());
    if (colonPos == std::string_view::npos) {
        return "{}";
    }

    const auto openBrace = json.find('{', colonPos + 1);
    if (openBrace == std::string_view::npos) {
        return "{}";
    }

    auto depth = 0;
    for (auto i = openBrace; i < json.size(); ++i) {
        if (json[i] == '{') {
            ++depth;
        } else if (json[i] == '}') {
            --depth;
            if (depth == 0) {
                return std::string(json.substr(openBrace, i - openBrace + 1));
            }
        }
    }

    return "{}";
}

std::size_t FindUnescapedQuote(std::string_view value, std::size_t start) {
    auto escaped = false;
    for (auto i = start; i < value.size(); ++i) {
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

    return std::string_view::npos;
}

std::string TrimAsciiWhitespace(std::string_view value) {
    auto first = value.begin();
    while (first != value.end() && std::isspace(static_cast<unsigned char>(*first)) != 0) {
        ++first;
    }

    auto last = value.end();
    while (last != first && std::isspace(static_cast<unsigned char>(*(last - 1))) != 0) {
        --last;
    }

    return {first, last};
}

std::size_t SkipAsciiWhitespace(std::string_view value, std::size_t cursor) {
    return value.find_first_not_of(" \t\r\n", cursor);
}

bool TryParseFlatStringMapEntryKey(std::string_view objectJson, std::size_t& cursor, std::string& key);
bool TryParseFlatStringMapEntryValue(std::string_view objectJson, std::size_t& cursor, std::string& value);

bool TryParseFlatStringMapEntry(
    std::string_view objectJson,
    std::size_t& cursor,
    std::string& key,
    std::string& value) {
    cursor = SkipAsciiWhitespace(objectJson, cursor);
    if (cursor == std::string_view::npos || objectJson[cursor] == '}') {
        return false;
    }

    if (!TryParseFlatStringMapEntryKey(objectJson, cursor, key)) {
        return false;
    }

    if (!TryParseFlatStringMapEntryValue(objectJson, cursor, value)) {
        return false;
    }

    cursor = SkipAsciiWhitespace(objectJson, cursor);
    if (cursor != std::string_view::npos && objectJson[cursor] == ',') {
        ++cursor;
    }

    return true;
}

bool TryParseFlatStringMapEntryKey(std::string_view objectJson, std::size_t& cursor, std::string& key) {
    if (objectJson[cursor] != '"') {
        return false;
    }

    const auto keyEnd = FindUnescapedQuote(objectJson, cursor + 1);
    if (keyEnd == std::string_view::npos) {
        return false;
    }

    key = std::string(objectJson.substr(cursor + 1, keyEnd - cursor - 1));
    cursor = objectJson.find(':', keyEnd + 1);
    if (cursor == std::string_view::npos) {
        return false;
    }

    ++cursor;
    cursor = SkipAsciiWhitespace(objectJson, cursor);
    return cursor != std::string_view::npos;
}

bool TryParseFlatStringMapEntryValue(std::string_view objectJson, std::size_t& cursor, std::string& value) {
    if (objectJson[cursor] == '"') {
        const auto valueEnd = FindUnescapedQuote(objectJson, cursor + 1);
        if (valueEnd == std::string_view::npos) {
            return false;
        }

        value = std::string(objectJson.substr(cursor + 1, valueEnd - cursor - 1));
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

std::map<std::string, std::string, std::less<>> ParseFlatStringMapObject(std::string_view objectJson) {
    std::map<std::string, std::string, std::less<>> parsed;
    auto cursor = objectJson.find('{');
    if (cursor == std::string_view::npos) {
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

std::string EscapeJson(std::string_view value) {
    std::string escaped;
    escaped.reserve(value.size() + 8);
    for (const auto ch : value) {
        switch (ch) {
        case '\\':
            escaped += R"(\\)";
            break;
        case '"':
            escaped += R"(\")";
            break;
        case '\n':
            escaped += R"(\n)";
            break;
        case '\r':
            escaped += R"(\r)";
            break;
        case '\t':
            escaped += R"(\t)";
            break;
        default:
            escaped.push_back(ch);
            break;
        }
    }

    return escaped;
}

std::string ToDiagnosticsJson(const std::map<std::string, std::string, std::less<>>& values) {
    std::ostringstream out;
    out << '{';
    auto first = true;
    for (const auto& [key, value] : values) {
        if (!first) {
            out << ',';
        }
        first = false;
        out << '"' << EscapeJson(key) << R"(":")" << EscapeJson(value) << '"';
    }
    out << '}';
    return out.str();
}

bool TryReadBool(std::string_view payloadJson, std::string_view key, bool& value) {
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

bool TryReadInt(std::string_view payloadJson, std::string_view key, int& value) {
    std::size_t start = 0;
    if (!TryFindValueStart(payloadJson, key, start)) {
        return false;
    }

    if (payloadJson[start] == '+') {
        return false;
    }

    return TryParseIntFromText(payloadJson.substr(start), value);
}

std::string ExtractStringValue(std::string_view json, std::string_view key) {
    auto quotedKey = std::string("\"");
    quotedKey.append(key);
    quotedKey.push_back('"');

    const auto keyPos = json.find(quotedKey);
    if (keyPos == std::string_view::npos) {
        return {};
    }

    const auto colonPos = json.find(':', keyPos + quotedKey.length());
    if (colonPos == std::string_view::npos) {
        return {};
    }

    const auto firstQuote = json.find('"', colonPos + 1);
    if (firstQuote == std::string_view::npos) {
        return {};
    }

    const auto secondQuote = json.find('"', firstQuote + 1);
    if (secondQuote == std::string_view::npos || secondQuote <= firstQuote) {
        return {};
    }

    return std::string(json.substr(firstQuote + 1, secondQuote - firstQuote - 1));
}

std::map<std::string, std::string, std::less<>> ExtractStringMap(std::string_view json, std::string_view key) {
    return ParseFlatStringMapObject(ExtractObjectJson(json, key));
}

} // namespace swfoc::extender::bridge::host_json
