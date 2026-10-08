#include "view.h"
#include <sddl.h>
#include <memory>
#include <string>
#include <vector>

namespace {
struct CloseHandleDeleter {
    void operator()(void* handle) const { if (handle) CloseHandle(handle); }
};
struct LocalFreeDeleter {
    void operator()(void* allocation) const { if (allocation) LocalFree(allocation); }
};
using Handle = std::unique_ptr<void, CloseHandleDeleter>;
using LocalAllocation = std::unique_ptr<void, LocalFreeDeleter>;

HANDLE CreateUserPipe(DWORD* error) {
    HANDLE raw_token = nullptr;
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &raw_token)) {
        *error = GetLastError();
        return INVALID_HANDLE_VALUE;
    }
    const Handle token(raw_token);
    DWORD size = 0;
    GetTokenInformation(token.get(), TokenUser, nullptr, 0, &size);
    if (!size) {
        *error = GetLastError();
        return INVALID_HANDLE_VALUE;
    }
    std::vector<BYTE> user(size);
    if (!GetTokenInformation(token.get(), TokenUser, user.data(), size, &size)) {
        *error = GetLastError();
        return INVALID_HANDLE_VALUE;
    }
    LPWSTR raw_sid = nullptr;
    if (!ConvertSidToStringSidW(reinterpret_cast<TOKEN_USER*>(user.data())->User.Sid, &raw_sid)) {
        *error = GetLastError();
        return INVALID_HANDLE_VALUE;
    }
    const LocalAllocation sid(raw_sid);
    // Protected DACL: no inherited entries and no access for other users.
    const auto sddl = L"O:" + std::wstring(raw_sid) + L"D:P(A;;GA;;;" + raw_sid + L")";
    PSECURITY_DESCRIPTOR raw_descriptor = nullptr;
    if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl.c_str(), SDDL_REVISION_1, &raw_descriptor, nullptr)) {
        *error = GetLastError();
        return INVALID_HANDLE_VALUE;
    }
    const LocalAllocation descriptor(raw_descriptor);
    SECURITY_ATTRIBUTES security{sizeof(SECURITY_ATTRIBUTES), descriptor.get(), FALSE};
    const auto name = L"\\\\.\\pipe\\auroraview-unity-" + std::to_wstring(GetCurrentProcessId());
    const auto pipe = CreateNamedPipeW(name.c_str(),
        PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED | FILE_FLAG_FIRST_PIPE_INSTANCE,
        PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT | PIPE_REJECT_REMOTE_CLIENTS,
        1, 65536, 65536, 0, &security);
    *error = pipe == INVALID_HANDLE_VALUE ? GetLastError() : ERROR_SUCCESS;
    return pipe;
}
}

HANDLE __cdecl av_pipe_create(DWORD* error) {
    if (!error) return INVALID_HANDLE_VALUE;
    try { return CreateUserPipe(error); }
    catch (const std::bad_alloc&) { *error = ERROR_NOT_ENOUGH_MEMORY; }
    catch (...) { *error = ERROR_INVALID_DATA; }
    return INVALID_HANDLE_VALUE;
}
