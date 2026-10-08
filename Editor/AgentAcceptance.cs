using System;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace AuroraView.Unity
{
    public static class AgentAcceptance
    {
        [Serializable] private sealed class Receipt
        {
            public int processId;
            public int mainThreadId;
            public string unityVersion;
            public int objectId;
            public bool mcpContext;
            public bool mcpCreate;
            public bool mcpSelect;
            public bool selectionReadback;
            public bool unityObjectReadback;
            public bool undoVerified;
        }
        private static string output;
        private static double deadline;
        private static bool readyWritten;
        public static void Run()
        {
            var package = PackageInfo.FindForAssembly(typeof(AgentAcceptance).Assembly);
            output = Path.Combine(package.resolvedPath, "build/evidence");
            Directory.CreateDirectory(output);
            AgentEndpoint.Enable();
            deadline = EditorApplication.timeSinceStartup + 90;
            readyWritten = false;
            EditorApplication.update += Verify;
        }
        private static void Verify()
        {
            if (!readyWritten && AgentEndpoint.IsListening)
            {
                File.WriteAllText(Path.Combine(output, "unity-agent-ready.json"), JsonUtility.ToJson(new Receipt
                {
                    processId = System.Diagnostics.Process.GetCurrentProcess().Id,
                    mainThreadId = SceneContracts.MainThreadId,
                    unityVersion = Application.unityVersion
                }, true));
                readyWritten = true;
            }
            var file = Path.Combine(output, "mcp-live-result.json");
            if (!File.Exists(file))
            {
                if (EditorApplication.timeSinceStartup > deadline) { EditorApplication.update -= Verify; AgentEndpoint.Disable(); EditorApplication.Exit(1); }
                return;
            }
            EditorApplication.update -= Verify;
            var receipt = JsonUtility.FromJson<Receipt>(File.ReadAllText(file));
            var item = EditorUtility.InstanceIDToObject(receipt.objectId) as GameObject;
            receipt.unityObjectReadback = item != null && item.name == "AuroraView MCP 验收立方体" &&
                Selection.activeGameObject == item && receipt.mainThreadId == SceneContracts.MainThreadId;
            Undo.PerformUndo();
            receipt.undoVerified = EditorUtility.InstanceIDToObject(receipt.objectId) == null;
            File.WriteAllText(Path.Combine(output, "unity-agent-acceptance.json"), JsonUtility.ToJson(receipt, true));
            AgentEndpoint.Disable();
            EditorApplication.Exit(receipt.mcpContext && receipt.mcpCreate && receipt.mcpSelect && receipt.selectionReadback && receipt.unityObjectReadback && receipt.undoVerified ? 0 : 1);
        }
    }
}
