using System;
using System.IO;
using UnityEditor;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityEngine;

namespace AuroraView.Unity
{
    public sealed class AuroraViewWindow : EditorWindow
    {
        private NativeView view;
        private string error;
        private double lastPaint;
        private IntPtr parent;
        private Rect viewport;
        public bool NativeReady => view != null && view.State == 1;
        public bool BrowserRoundTrip { get; private set; }
        public int BrowserCreatedObjectId { get; private set; }
        public long ParentHwnd => parent.ToInt64();
        public string LastError => error ?? (view != null && view.State < 0 ? view.Error : null);

        [MenuItem("Window/AuroraView/Scene Tools")]
        public static AuroraViewWindow Open()
        {
            var window = GetWindow<AuroraViewWindow>("AuroraView", true, typeof(SceneView));
            window.titleContent = new GUIContent("AuroraView");
            window.minSize = new Vector2(360, 300);
            window.Show();
            return window;
        }
        private void OnEnable()
        {
            EditorApplication.update += Tick;
            AssemblyReloadEvents.beforeAssemblyReload += Release;
            Selection.selectionChanged += PublishContext;
        }
        private void OnDisable()
        {
            EditorApplication.update -= Tick;
            AssemblyReloadEvents.beforeAssemblyReload -= Release;
            Selection.selectionChanged -= PublishContext;
            Release();
        }
        private void Release() { view?.Dispose(); view = null; BrowserRoundTrip = false; BrowserCreatedObjectId = 0; }
        internal void RequestBrowserCube()
        {
            view.Evaluate("window.auroraview.call('scene.create_cube',{name:'AuroraView Acceptance Cube'}).then(r=>window.ipc.postMessage('auroraview:accept-created:'+r.created.objectId));");
        }
        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("AuroraView · Unity Editor", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Reload", EditorStyles.toolbarButton)) { error = null; Release(); }
            }
            if (Application.platform != RuntimePlatform.WindowsEditor)
            { EditorGUILayout.HelpBox("The native panel currently requires Windows Editor x64.", MessageType.Info); return; }
            if (!string.IsNullOrEmpty(LastError))
            { EditorGUILayout.HelpBox(LastError + "\nBuild/install the native plugin and restart the Editor.", MessageType.Error); return; }
            var area = GUILayoutUtility.GetRect(0, 100000, 0, 100000, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (Event.current.type != EventType.Repaint || area.width < 2 || area.height < 2) return;
            var origin = GUIUtility.GUIToScreenPoint(area.position);
            var scale = EditorGUIUtility.pixelsPerPoint;
            var screen = new Rect(origin.x * scale, origin.y * scale, area.width * scale, area.height * scale);
            var target = HostWindow.Find(screen);
            if (target == IntPtr.Zero) { error = "No visible native window in this Editor process contains the panel."; return; }
            parent = target;
            viewport = HostWindow.ToClient(parent, screen);
            lastPaint = EditorApplication.timeSinceStartup;
            try
            {
                if (view != null && view.State == 2) Release();
                if (view == null)
                {
                    var package = PackageInfo.FindForAssembly(typeof(AuroraViewWindow).Assembly);
                    var html = Path.Combine(package.resolvedPath, "Editor/WebAssets/index.html");
                    var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "AuroraView/Unity", System.Diagnostics.Process.GetCurrentProcess().Id.ToString());
                    view = new NativeView(parent, new Uri(html).AbsoluteUri, cache);
                }
                view.Place(parent, viewport, true);
            }
            catch (Exception exception) { error = exception.Message; Release(); }
        }
        private void Tick()
        {
            Repaint();
            if (view == null) return;
            // Inactive dock tabs stop repainting; never leave a child HWND covering another panel.
            view.Place(parent, viewport, EditorApplication.timeSinceStartup - lastPaint < 0.3);
            for (var index = 0; index < 32 && view.Poll(out var json); index++)
            {
                if (json == "auroraview:browser-ready") { BrowserRoundTrip = true; continue; }
                const string prefix = "auroraview:accept-created:";
                if (json.StartsWith(prefix, StringComparison.Ordinal))
                { if (int.TryParse(json.Substring(prefix.Length), out var objectId)) BrowserCreatedObjectId = objectId; continue; }
                // Upstream ready/events are not RPC calls and need no call-result response.
                try { if (JsonUtility.FromJson<CallRequest>(json)?.type != "call") continue; }
                catch (ArgumentException) { continue; }
                var response = SceneContracts.Dispatch(json);
                view.Evaluate("window.auroraview.trigger('__auroraview_call_result'," + response + ");");
            }
        }
        private void PublishContext()
        {
            if (!NativeReady) return;
            var response = JsonUtility.FromJson<CallSuccess>(SceneContracts.Dispatch("{\"type\":\"call\",\"id\":\"selection\",\"method\":\"scene.context\"}"));
            view.Evaluate("window.auroraview.trigger('scene.selection'," + JsonUtility.ToJson(response.result) + ");");
        }
    }
}
