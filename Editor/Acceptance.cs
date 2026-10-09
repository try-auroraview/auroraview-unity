using System;
using System.IO;
using UnityEditor;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityEngine;

namespace AuroraView.Unity
{
    public static class Acceptance
    {
        [Serializable] private sealed class Receipt
        {
            public string unityVersion;
            public int processId;
            public int mainThreadId;
            public long parentHwnd;
            public bool nativeReady;
            public bool browserRoundTrip;
            public bool browserCreateRoundTrip;
            public bool sceneMutation;
            public bool undoVerified;
            public string error;
        }
        private static AuroraViewWindow window;
        private static double deadline;
        private static bool createRequested;
        public static void Run()
        {
            window = AuroraViewWindow.Open();
            deadline = EditorApplication.timeSinceStartup + 60;
            createRequested = false;
            EditorApplication.update += Verify;
        }
        private static void Verify()
        {
            if (string.IsNullOrEmpty(window.LastError) && EditorApplication.timeSinceStartup < deadline)
            {
                if (!window.BrowserRoundTrip) return;
                if (!createRequested) { window.RequestBrowserCube(); createRequested = true; return; }
                if (window.BrowserCreatedObjectId == 0) return;
            }
            EditorApplication.update -= Verify;
            var receipt = new Receipt
            {
                unityVersion = Application.unityVersion,
                processId = System.Diagnostics.Process.GetCurrentProcess().Id,
                mainThreadId = SceneContracts.MainThreadId,
                parentHwnd = window.ParentHwnd,
                nativeReady = window.NativeReady,
                browserRoundTrip = window.BrowserRoundTrip,
                browserCreateRoundTrip = window.BrowserCreatedObjectId != 0,
                error = window.LastError
            };
            if (receipt.browserRoundTrip)
            {
                var objectId = window.BrowserCreatedObjectId;
                var item = EditorUtility.InstanceIDToObject(objectId) as GameObject;
                if (item != null)
                {
                    receipt.sceneMutation = item.name == "AuroraView Acceptance Cube" && Selection.activeGameObject == item;
                    Undo.PerformUndo();
                    receipt.undoVerified = EditorUtility.InstanceIDToObject(objectId) == null;
                }
            }
            var package = PackageInfo.FindForAssembly(typeof(Acceptance).Assembly);
            var output = Path.Combine(package.resolvedPath, "build~/evidence");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "unity-acceptance.json"), JsonUtility.ToJson(receipt, true));
            window.Close();
            EditorApplication.Exit(receipt.browserRoundTrip && receipt.browserCreateRoundTrip && receipt.sceneMutation && receipt.undoVerified ? 0 : 1);
        }
    }
}
