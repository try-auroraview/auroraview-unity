#include "view.h"
#include "bridge.h"
#include <objbase.h>
#include <unknwn.h>
#include <wrl.h>
#include <WebView2.h>
#include <algorithm>
#include <atomic>
#include <deque>
#include <filesystem>
#include <memory>
#include <mutex>
#include <string>
#include <thread>
#include <unordered_map>

using Microsoft::WRL::Callback;
using Microsoft::WRL::ComPtr;
namespace {
constexpr size_t kMessageLimit = 65536;
constexpr size_t kQueueLimit = 256;
struct Placement { HWND parent = nullptr; int x = 0, y = 0, width = 1, height = 1; bool visible = false; };
bool OwnsWindow(HWND window) {
    DWORD pid = 0;
    return IsWindow(window) && GetWindowThreadProcessId(window, &pid) && pid == GetCurrentProcessId();
}
std::wstring Document(std::wstring uri) {
    const auto hash = uri.find(L'#');
    if (hash != std::wstring::npos) uri.resize(hash);
    return uri;
}
class View : public std::enable_shared_from_this<View> {
public:
    View(HWND parent, std::wstring url, std::wstring directory) : url_(std::move(url)), directory_(std::move(directory)) {
        placement_.parent = parent;
    }
    void Start() { worker_ = std::thread([self = shared_from_this()] { self->Run(); }); }
    void Stop() {
        closing_.store(true);
        // Closing a child HWND can synchronously notify Unity's main-thread parent.
        // Dispatch sent messages while waiting, so assembly reload cannot deadlock.
        while (!stopped_.load() && worker_.joinable()) {
            MSG message;
            PeekMessageW(&message, nullptr, 0, 0, PM_NOREMOVE);
            MsgWaitForMultipleObjects(0, nullptr, FALSE, 10, QS_SENDMESSAGE);
        }
        if (worker_.joinable()) worker_.join();
    }
    int State() const { return state_.load(); }
    void Place(Placement placement) { std::lock_guard<std::mutex> guard(mutex_); placement_ = placement; }
    bool Eval(const wchar_t* script) {
        if (!script || wcslen(script) > kMessageLimit || closing_.load()) return false;
        std::lock_guard<std::mutex> guard(mutex_);
        if (scripts_.size() >= kQueueLimit) return false;
        scripts_.emplace_back(script); return true;
    }
    int Poll(wchar_t* output, int capacity) {
        if (!output || capacity < 1) return 0;
        std::lock_guard<std::mutex> guard(mutex_);
        if (messages_.empty()) { output[0] = 0; return 0; }
        const auto& message = messages_.front();
        if (message.size() >= static_cast<size_t>(capacity)) return -static_cast<int>(message.size() + 1);
        const auto length = static_cast<int>(message.size());
        std::copy(message.begin(), message.end(), output); output[length] = 0;
        messages_.pop_front(); return length;
    }
    int Error(wchar_t* output, int capacity) {
        if (!output || capacity < 1) return 0;
        std::lock_guard<std::mutex> guard(mutex_);
        const auto length = (std::min)(error_.size(), static_cast<size_t>(capacity - 1));
        std::copy_n(error_.data(), length, output); output[length] = 0; return static_cast<int>(length);
    }
private:
    void Fail(HRESULT result, const wchar_t* operation) {
        if (SUCCEEDED(result)) return;
        std::lock_guard<std::mutex> guard(mutex_);
        error_ = std::wstring(operation) + L" failed (HRESULT " + std::to_wstring(static_cast<long>(result)) + L").";
        state_.store(-1);
    }
    HRESULT InitializeController(ICoreWebView2Controller* controller) {
        if (closing_.load()) return S_OK;
        controller_ = controller;
        auto result = controller_->get_CoreWebView2(&webview_);
        if (FAILED(result)) return result;
        ComPtr<ICoreWebView2Settings> settings;
        if (SUCCEEDED(webview_->get_Settings(&settings))) {
            settings->put_AreDevToolsEnabled(FALSE);
            settings->put_AreDefaultContextMenusEnabled(FALSE);
            settings->put_IsStatusBarEnabled(FALSE);
        }
        const auto weak = weak_from_this();
        result = webview_->add_WebMessageReceived(Callback<ICoreWebView2WebMessageReceivedEventHandler>(
            [weak](ICoreWebView2*, ICoreWebView2WebMessageReceivedEventArgs* args) -> HRESULT {
                auto self = weak.lock();
                if (!self || self->closing_.load()) return S_OK;
                LPWSTR source = nullptr;
                if (FAILED(args->get_Source(&source))) return S_OK;
                const bool trusted = Document(source) == Document(self->url_);
                CoTaskMemFree(source);
                if (!trusted) return S_OK;
                LPWSTR value = nullptr;
                if (SUCCEEDED(args->TryGetWebMessageAsString(&value))) {
                    const size_t length = wcslen(value);
                    std::lock_guard<std::mutex> guard(self->mutex_);
                    if (length <= kMessageLimit && self->messages_.size() < kQueueLimit) self->messages_.emplace_back(value);
                    CoTaskMemFree(value);
                }
                return S_OK;
            }).Get(), &message_token_);
        if (FAILED(result)) return result;
        result = webview_->add_NavigationStarting(Callback<ICoreWebView2NavigationStartingEventHandler>(
            [weak](ICoreWebView2*, ICoreWebView2NavigationStartingEventArgs* args) -> HRESULT {
                auto self = weak.lock();
                LPWSTR uri = nullptr;
                if (!self || FAILED(args->get_Uri(&uri))) return S_OK;
                if (Document(uri) != Document(self->url_)) args->put_Cancel(TRUE);
                CoTaskMemFree(uri); return S_OK;
            }).Get(), &navigation_token_);
        if (FAILED(result)) return result;
        result = webview_->add_NewWindowRequested(Callback<ICoreWebView2NewWindowRequestedEventHandler>(
            [](ICoreWebView2*, ICoreWebView2NewWindowRequestedEventArgs* args) -> HRESULT {
                return args->put_Handled(TRUE);
            }).Get(), &new_window_token_);
        if (FAILED(result)) return result;
        const auto bridgeScript = BridgeScript();
        return webview_->AddScriptToExecuteOnDocumentCreated(bridgeScript.c_str(),
            Callback<ICoreWebView2AddScriptToExecuteOnDocumentCreatedCompletedHandler>(
                [weak](HRESULT status, LPCWSTR) -> HRESULT {
                    auto self = weak.lock();
                    if (!self || self->closing_.load()) return S_OK;
                    self->Fail(status, L"Bridge injection");
                    if (SUCCEEDED(status)) {
                        const auto navigation = self->webview_->Navigate(self->url_.c_str());
                        self->Fail(navigation, L"Navigate");
                        if (SUCCEEDED(navigation)) self->state_.store(1);
                    }
                    return S_OK;
                }).Get());
    }
    void Initialize() {
        const auto weak = weak_from_this();
        const auto environmentStatus = CreateCoreWebView2EnvironmentWithOptions(nullptr, directory_.c_str(), nullptr,
            Callback<ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler>(
                [weak](HRESULT environmentResult, ICoreWebView2Environment* environment) -> HRESULT {
                    auto self = weak.lock();
                    if (!self || self->closing_.load()) return S_OK;
                    self->Fail(environmentResult, L"WebView2 environment (install Microsoft Edge WebView2 Runtime)");
                    if (FAILED(environmentResult) || !environment) return S_OK;
                    const auto controllerStatus = environment->CreateCoreWebView2Controller(self->child_,
                        Callback<ICoreWebView2CreateCoreWebView2ControllerCompletedHandler>(
                            [weak](HRESULT controllerResult, ICoreWebView2Controller* controller) -> HRESULT {
                                auto target = weak.lock();
                                if (!target || target->closing_.load()) return S_OK;
                                target->Fail(controllerResult, L"WebView2 controller");
                                if (SUCCEEDED(controllerResult) && controller)
                                    target->Fail(target->InitializeController(controller), L"Configure WebView2");
                                return S_OK;
                            }).Get());
                    self->Fail(controllerStatus, L"Create controller"); return S_OK;
                }).Get());
        Fail(environmentStatus, L"Create environment");
    }
    void Apply() {
        Placement placement;
        std::deque<std::wstring> scripts;
        { std::lock_guard<std::mutex> guard(mutex_); placement = placement_; if (State() == 1) scripts.swap(scripts_); }
        if (!OwnsWindow(placement.parent)) { closing_.store(true); return; }
        if (GetParent(child_) != placement.parent) SetParent(child_, placement.parent);
        SetWindowPos(child_, HWND_TOP, placement.x, placement.y, (std::max)(1, placement.width), (std::max)(1, placement.height),
            SWP_NOACTIVATE | (placement.visible ? SWP_SHOWWINDOW : SWP_HIDEWINDOW));
        if (!controller_) return;
        RECT bounds{0, 0, (std::max)(1, placement.width), (std::max)(1, placement.height)};
        controller_->put_Bounds(bounds);
        controller_->put_IsVisible(placement.visible ? TRUE : FALSE);
        controller_->NotifyParentWindowPositionChanged();
        for (const auto& script : scripts) Fail(webview_->ExecuteScript(script.c_str(), nullptr), L"Execute script");
    }
    void Run() {
        const auto apartment = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
        if (FAILED(apartment)) { Fail(apartment, L"Initialize STA"); stopped_.store(true); return; }
        try {
            std::filesystem::create_directories(directory_);
            Placement placement;
            { std::lock_guard<std::mutex> guard(mutex_); placement = placement_; }
            child_ = CreateWindowExW(0, L"STATIC", L"AuroraView Unity WebView2", WS_CHILD | WS_CLIPCHILDREN | WS_CLIPSIBLINGS,
                0, 0, 1, 1, placement.parent, nullptr, GetModuleHandleW(nullptr), nullptr);
            if (!child_) Fail(HRESULT_FROM_WIN32(GetLastError()), L"Create child HWND");
            else {
                Initialize();
                while (!closing_.load()) {
                    MSG message;
                    while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) {
                        if (message.message == WM_QUIT) { closing_.store(true); break; }
                        TranslateMessage(&message); DispatchMessageW(&message);
                    }
                    Apply();
                    MsgWaitForMultipleObjects(0, nullptr, FALSE, 10, QS_ALLINPUT);
                }
            }
        } catch (const std::exception&) { Fail(E_FAIL, L"Native panel initialization"); }
        if (webview_) {
            webview_->remove_WebMessageReceived(message_token_);
            webview_->remove_NavigationStarting(navigation_token_);
            webview_->remove_NewWindowRequested(new_window_token_);
        }
        if (controller_) controller_->Close();
        webview_.Reset(); controller_.Reset();
        if (child_ && IsWindow(child_)) DestroyWindow(child_);
        CoUninitialize();
        if (State() >= 0) state_.store(2);
        stopped_.store(true);
    }
    std::wstring url_, directory_, error_;
    std::mutex mutex_;
    Placement placement_;
    std::deque<std::wstring> scripts_, messages_;
    std::atomic<int> state_{0};
    std::atomic<bool> closing_{false};
    std::atomic<bool> stopped_{false};
    std::thread worker_;
    HWND child_ = nullptr;
    ComPtr<ICoreWebView2Controller> controller_;
    ComPtr<ICoreWebView2> webview_;
    EventRegistrationToken message_token_{}, navigation_token_{}, new_window_token_{};
};
std::mutex registry_mutex;
std::unordered_map<uint32_t, std::shared_ptr<View>> views;
std::atomic<uint32_t> sequence{0};
std::shared_ptr<View> Find(uint32_t handle) {
    std::lock_guard<std::mutex> guard(registry_mutex);
    const auto found = views.find(handle); return found == views.end() ? nullptr : found->second;
}
} // namespace
uint32_t __cdecl av_create(HWND parent, const wchar_t* url, const wchar_t* directory) {
    if (!OwnsWindow(parent) || !url || wcsncmp(url, L"file:///", 8) != 0 || !directory || !*directory) return 0;
    try {
        auto view = std::make_shared<View>(parent, url, directory);
        auto handle = ++sequence;
        { std::lock_guard<std::mutex> guard(registry_mutex); views.emplace(handle, view); }
        view->Start(); return handle;
    } catch (...) { return 0; }
}
int __cdecl av_state(uint32_t handle) { auto view = Find(handle); return view ? view->State() : -1; }
void __cdecl av_place(uint32_t handle, HWND parent, int x, int y, int width, int height, int visible) {
    auto view = Find(handle);
    if (view && OwnsWindow(parent)) view->Place({parent, x, y, width, height, visible != 0});
}
int __cdecl av_poll(uint32_t handle, wchar_t* output, int capacity) { auto view = Find(handle); return view ? view->Poll(output, capacity) : 0; }
int __cdecl av_error(uint32_t handle, wchar_t* output, int capacity) { auto view = Find(handle); return view ? view->Error(output, capacity) : 0; }
int __cdecl av_eval(uint32_t handle, const wchar_t* script) { auto view = Find(handle); return view && view->Eval(script) ? 1 : 0; }
void __cdecl av_destroy(uint32_t handle) {
    std::shared_ptr<View> view;
    {
        std::lock_guard<std::mutex> guard(registry_mutex);
        const auto found = views.find(handle);
        if (found == views.end()) return;
        view = found->second; views.erase(found);
    }
    view->Stop();
}
