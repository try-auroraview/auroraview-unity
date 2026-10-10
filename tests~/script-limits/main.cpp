#include "script_limits.h"

#ifdef NDEBUG
#undef NDEBUG
#endif
#include <cassert>
#include <iostream>
#include <limits>

int main() {
    using namespace auroraview;
    static_assert(kMessageLimit == 65536, "Inbound messages retain their 64K limit.");
    static_assert(kScriptLimit == 1024 * 1024, "Outbound scripts allow 1Mi UTF-16 characters.");
    static_assert(kQueueLimit == 256, "Queue count remains bounded.");
    static_assert(kScriptQueueCharLimit == 65536 * 256, "Queued characters retain the old budget.");

    assert(CanQueueScript(65536, 0, 0));
    assert(CanQueueScript(65537, 0, 0));
    assert(CanQueueScript(768 * 1024, 0, 0));
    assert(CanQueueScript(768 * 1024 + 128, 0, 0));
    assert(CanQueueScript(1024 * 1024, 0, 0));
    assert(!CanQueueScript(1024 * 1024 + 1, 0, 0));

    assert(CanQueueScript(1, 255, 0));
    assert(!CanQueueScript(1, 256, 0));
    assert(!CanQueueScript(0, 256, 0));

    assert(CanQueueScript(1024 * 1024, 15, 15 * 1024 * 1024));
    assert(!CanQueueScript(1, 16, 16 * 1024 * 1024));
    assert(CanQueueScript(1, 0, kScriptQueueCharLimit - 1));
    assert(!CanQueueScript(2, 0, kScriptQueueCharLimit - 1));
    assert(!CanQueueScript(0, 0, kScriptQueueCharLimit + 1));
    assert(!CanQueueScript(1, 0, std::numeric_limits<std::size_t>::max()));
    assert(!CanQueueScript(std::numeric_limits<std::size_t>::max(), 0, 0));

    std::size_t count = 0, chars = 0;
    for (int i = 0; i < 16; ++i) {
        assert(CanQueueScript(kScriptLimit, count, chars));
        ++count;
        chars += kScriptLimit;
    }
    assert(!CanQueueScript(1, count, chars));
    assert(CanQueueScript(kScriptLimit, 0, 0));
    std::cout << "Script limits passed: 64K, 768KiB, 1Mi, 256 entries and total character budget.\n";
}
