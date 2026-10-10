#pragma once

#include <cstddef>

namespace auroraview {
inline constexpr std::size_t kMessageLimit = 65536;
inline constexpr std::size_t kScriptLimit = 1024 * 1024;
inline constexpr std::size_t kQueueLimit = 256;
inline constexpr std::size_t kScriptQueueCharLimit = kMessageLimit * kQueueLimit;

inline bool CanQueueScript(std::size_t scriptChars, std::size_t queuedCount,
                           std::size_t queuedChars) noexcept {
    return scriptChars <= kScriptLimit && queuedCount < kQueueLimit &&
           queuedChars <= kScriptQueueCharLimit &&
           scriptChars <= kScriptQueueCharLimit - queuedChars;
}
} // namespace auroraview
