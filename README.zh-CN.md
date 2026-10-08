# AuroraView for Unity

在 Unity Editor 内运行现代 Web 工具，并把同一组明确注册的场景功能提供给人和 MCP 客户端。

Scene Tools 示例可以创建真实立方体、读取场景与选择、选择已有对象。所有场景操作都由 Unity 主线程执行，创建支持 Undo。Microsoft WebView2 是 EditorWindow 内的原生子 HWND；不会用外部浏览器窗口冒充嵌入。

[English](README.md) · [架构边界](docs/architecture.md) · [验收状态](docs/validation.md)

## 运行示例

要求 Windows x64、Unity 2022.3 LTS 或更新版本，以及 [WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/)。初始本地验证目标为 Unity 2022.3.62f3c1。当前没有实现 macOS/Linux、Unity Player 或渲染到纹理。

1. 从 [Releases](https://github.com/try-auroraview/auroraview-unity/releases) 下载包含原生 DLL 的包，解压后在 Package Manager 选择 **Add package from disk**，打开 `com.auroraview.unity/package.json`。若尚未发布资产，先按下方源码命令构建。
2. 打开 **Window → AuroraView → Scene Tools**，将窗口停靠在 Hierarchy 或 Inspector 旁。
3. 输入名称，点击 **Create cube**。Unity Hierarchy 中应出现并选中真实 GameObject；使用 Unity Undo 删除它。
4. 在 Unity 中选择其他对象，面板显示的选择与宿主上下文随之更新。

Git UPM 源码不包含生成的原生 DLL，不能把仅添加 Git URL 当成完整安装。

## 构建和验证

先准备 vx 与 Visual Studio 2022 C++ 桌面工作负载。执行：

```powershell
vx just build
vx just test
vx just test-native
vx just test-unity
vx just accept-unity
vx just accept-agent
vx just package
```

`build` 从微软官方 NuGet 下载固定版本 WebView2 SDK，并核对官方 catalog 的 SHA512；若官方 catalog leaf 失效，则必须通过 `vx dotnet nuget verify --all` 验证可信微软作者签名，不会以本地自算 hash 替代。`test-native` 创建真实 WebView2 并验证桥消息和 STA 生命周期。`test-unity` 在已安装、已授权的 Editor 中执行 EditMode 测试。`accept-unity` 使用非 batch Editor 验证原生父 HWND、浏览器往返、创建与 Undo。`accept-agent` 在真实 Editor 通过 MCP→命名管道创建与选择，再由 Unity 读回对象并 Undo。需要时通过 `UNITY_EDITOR` 指定 Unity.exe。最小项目位于 `Samples~/SceneTools`。

## 人与 Agent 共享显式契约

AuroraView 专注 Web 界面、渲染、原生停靠和前端桥接。本仓库的 Node stdio 与本机管道是可选的 **preview 示例**，尚未接入 DCC-MCP Core。正式接入应通过薄层复用既有 Core server、工具、Skill、宿主执行桥、调度与生命周期；共同 Core facade 由对应集成项目提供，本包不创造另一套 Core API。服务借用和自有资源的规则见[共享运行时边界](docs/shared-runtime.md)。

页面使用 AuroraView 的 `call/on/trigger`。MCP 适配器只注册三项工具：

| 场景契约 | MCP 工具 | 功能 |
|---|---|---|
| `scene.context` | `unity_scene_context` | 场景、选择、Unity 版本、PID、主线程 ID |
| `scene.create_cube` | `unity_create_cube` | 创建并选择立方体，支持 Undo，Play 模式拒绝修改 |
| `scene.select` | `unity_select_object` | 通过本会话 objectId 选择真实场景 GameObject |

Agent 端点默认关闭，每次会话须主动选择 **Window → AuroraView → Enable Agent Endpoint**。日志给出当前 Editor PID。将 MCP 客户端配置为 `vx node C:/path/to/agent/server.mjs --pid 12345`。PID 必须明确绑定；本机命名管道仅授予当前 Windows 用户访问。关闭端点、程序集重载或退出时释放连接，没有网络监听或任意脚本执行入口。

这是独立 MCP 适配器，尚未自动接入 DCC-MCP；接入时须显式注册上述契约和宿主身份。不是任意 UI 控件自动变成 Agent 工具。

## 实现与验收边界

Unity 主线程负责场景和 Undo；独立 STA 负责 WebView2 COM 与原生消息泵。C ABI 只传值，不跨线程传 COM 对象。官方 AuroraView JavaScript 桥原样复用并记录来源与校验值；当前没有链接 Rust core，因为主项目尚未提供通用稳定 C ABI。

停靠结构由 Unity EditorWindow 承载，原生子窗口追踪其位置、尺寸与可见性。高 DPI、跨显示器、Dock tab 切换、键盘输入与拖动的完整视觉验收须分别确认，不能由单测或原生构建代替。实际状态见[验收报告](docs/validation.md)。

MIT 许可证。第三方归属见 [THIRD_PARTY.md](THIRD_PARTY.md)。
