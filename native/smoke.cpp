#define AURORAVIEW_IMPORT
#include "view.h"
#include <aclapi.h>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <string>
#include <vector>

bool CheckPipeAcl() {
    DWORD error = ERROR_SUCCESS;
    const auto pipe = av_pipe_create(&error);
    if (pipe == INVALID_HANDLE_VALUE || error != ERROR_SUCCESS) return false;
    PSID owner = nullptr;
    PACL dacl = nullptr;
    PSECURITY_DESCRIPTOR descriptor = nullptr;
    const auto security_error = GetSecurityInfo(pipe, SE_KERNEL_OBJECT,
        OWNER_SECURITY_INFORMATION | DACL_SECURITY_INFORMATION,
        &owner, nullptr, &dacl, nullptr, &descriptor);
    SECURITY_DESCRIPTOR_CONTROL control = 0;
    DWORD revision = 0;
    void* raw_ace = nullptr;
    HANDLE token = nullptr;
    DWORD size = 0;
    bool valid = security_error == ERROR_SUCCESS && descriptor && dacl && dacl->AceCount == 1 &&
        GetSecurityDescriptorControl(descriptor, &control, &revision) && (control & SE_DACL_PROTECTED) &&
        GetAce(dacl, 0, &raw_ace) && OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token);
    if (valid) {
        GetTokenInformation(token, TokenUser, nullptr, 0, &size);
        std::vector<BYTE> user(size);
        auto* ace = static_cast<ACCESS_ALLOWED_ACE*>(raw_ace);
        valid = size && GetTokenInformation(token, TokenUser, user.data(), size, &size) &&
            ace->Header.AceType == ACCESS_ALLOWED_ACE_TYPE && ace->Header.AceFlags == 0 &&
            ((ace->Mask & GENERIC_ALL) || (ace->Mask & FILE_ALL_ACCESS) == FILE_ALL_ACCESS) &&
            EqualSid(owner, reinterpret_cast<TOKEN_USER*>(user.data())->User.Sid) &&
            EqualSid(&ace->SidStart, owner);
    }
    if (token) CloseHandle(token);
    if (descriptor) LocalFree(descriptor);
    const auto duplicate = av_pipe_create(&error);
    valid = valid && duplicate == INVALID_HANDLE_VALUE && error != ERROR_SUCCESS;
    if (duplicate != INVALID_HANDLE_VALUE) CloseHandle(duplicate);
    CloseHandle(pipe);
    const auto reopened = av_pipe_create(&error);
    valid = valid && reopened != INVALID_HANDLE_VALUE && error == ERROR_SUCCESS;
    if (reopened != INVALID_HANDLE_VALUE) CloseHandle(reopened);
    if (!valid) std::cerr << "FAIL: current-user pipe DACL or close/reopen\n";
    return valid;
}

int wmain() {
    if (!CheckPipeAcl()) return 1;
    const auto directory = std::filesystem::temp_directory_path() / (L"auroraview-unity-test-" + std::to_wstring(GetCurrentProcessId()));
    std::filesystem::create_directories(directory);
    const auto file = directory / L"index.html";
    std::ofstream(file) << "<!doctype html><script>window.chrome.webview.postMessage('native-ready')</script>";
    auto path = file.generic_wstring();
    const auto url = L"file:///" + path;
    auto parent = CreateWindowExW(0, L"STATIC", L"AuroraView native test", WS_OVERLAPPEDWINDOW, 0, 0, 640, 480, nullptr, nullptr, nullptr, nullptr);
    if (!parent || av_create(nullptr, url.c_str(), directory.c_str()) != 0 || av_create(GetShellWindow(), url.c_str(), directory.c_str()) != 0) return 1;
    const auto view = av_create(parent, url.c_str(), (directory / L"cache").c_str());
    av_place(view, parent, 0, 0, 600, 400, 1);
    const auto deadline = GetTickCount64() + 30000;
    bool received = false, evaluated = false;
    wchar_t output[65537]{};
    while (view && GetTickCount64() < deadline) {
        MSG message;
        while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) { TranslateMessage(&message); DispatchMessageW(&message); }
        if (av_state(view) < 0) { av_error(view, output, 65537); std::wcerr << output << L'\n'; break; }
        if (av_poll(view, output, 65537) > 0) {
            if (std::wstring(output) == L"native-ready") {
                received = true;
                av_eval(view, L"window.auroraview.on('native-test',x=>chrome.webview.postMessage(x));window.auroraview.trigger('native-test','eval-ok')");
            } else if (std::wstring(output) == L"eval-ok") { evaluated = true; break; }
        }
        MsgWaitForMultipleObjects(0, nullptr, FALSE, 10, QS_ALLINPUT);
    }
    auto child = FindWindowExW(parent, nullptr, L"STATIC", L"AuroraView Unity WebView2");
    const bool embedded = child && GetParent(child) == parent && (GetWindowLongPtrW(child, GWL_STYLE) & WS_CHILD);
    av_destroy(view);
    const bool destroyed = !IsWindow(child);
    DestroyWindow(parent);
    if (!received || !evaluated || !embedded || !destroyed || av_state(view) != -1) return 1;
    std::cout << "PASS: current-user pipe DACL, exclusive creation, close/reopen, real WebView2 child HWND, upstream bridge, inbound/outbound IPC, STA shutdown\n";
    return 0;
}
