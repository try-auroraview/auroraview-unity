#define AURORAVIEW_IMPORT
#include "view.h"
#include <filesystem>
#include <fstream>
#include <iostream>
#include <string>

int wmain() {
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
    std::cout << "PASS: real WebView2 child HWND, upstream bridge, inbound/outbound IPC, STA shutdown\n";
    return 0;
}
