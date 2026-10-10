using System;
using System.Threading;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AuroraView.Unity.Tests
{
    public sealed class SceneContractsTests
    {
        [Test] public void ContextReportsMainThreadAndHost()
        {
            var result = JsonUtility.FromJson<CallSuccess>(SceneContracts.Dispatch("{\"type\":\"call\",\"id\":\"test\",\"method\":\"scene.context\"}"));
            Assert.IsTrue(result.ok);
            Assert.AreEqual(Application.unityVersion, result.result.unityVersion);
            Assert.AreEqual(Thread.CurrentThread.ManagedThreadId, result.result.mainThreadId);
            Assert.AreEqual(SceneContracts.SessionId, result.result.sessionId);
            Assert.AreEqual(32, result.result.sessionId.Length);
            Assert.IsNotNull(result.result.editorStatus);
            Assert.AreEqual(result.result.sessionId, result.result.editorStatus.sessionId);
            Assert.AreEqual(result.result.mainThreadId, result.result.editorStatus.mainThreadId);
            Assert.AreEqual(result.result.editorOwner.processId, result.result.editorStatus.editorOwner.processId);
            Assert.AreEqual(result.result.editorOwner.processCreationFileTime, result.result.editorStatus.editorOwner.processCreationFileTime);
            Assert.AreEqual(result.result.editorOwner.projectPath, result.result.editorStatus.editorOwner.projectPath);
            Assert.AreEqual(result.result.editorOwner.runId, result.result.editorStatus.editorOwner.runId);
            Assert.IsNotNull(result.result.editorStatus.dirtyScenes);
            Assert.IsNotNull(result.result.editorStatus.dirtyPersistentAssets);
            Assert.IsNotNull(result.result.editorStatus.unsavedWindows);
            Assert.IsNotNull(result.result.editorStatus.prefabStage);
            DateTime sampled;
            Assert.IsTrue(DateTime.TryParse(result.result.editorStatus.sampledAtUtc, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out sampled));
            Assert.AreEqual(DateTimeKind.Utc, sampled.Kind);
            Assert.AreEqual(EditorApplication.isCompiling, result.result.editorStatus.isCompiling);
            Assert.AreEqual(EditorApplication.isUpdating, result.result.editorStatus.isUpdating);
            Assert.AreEqual(EditorApplication.isPlayingOrWillChangePlaymode, result.result.editorStatus.isPlayingOrWillChangePlaymode);
        }
        [Test] public void PublicContextReservesTransportNewlineBudget()
        {
            var json = SceneContracts.Dispatch("{\"type\":\"call\",\"id\":\"budget\",\"method\":\"scene.context\"}");
            Assert.LessOrEqual(System.Text.Encoding.UTF8.GetByteCount(json), 60 * 1024);
            Assert.LessOrEqual(System.Text.Encoding.UTF8.GetByteCount(json + "\r\n"), 65536);
            var result = JsonUtility.FromJson<CallSuccess>(json);
            Assert.IsTrue(result.ok);
            var status = result.result.editorStatus;
            Assert.LessOrEqual(result.result.selection.Length, 32);
            Assert.LessOrEqual(status.dirtyScenes.Length, 32);
            Assert.LessOrEqual(status.dirtyPersistentAssets.Length, 32);
            Assert.LessOrEqual(status.unsavedWindows.Length, 32);
            Assert.GreaterOrEqual(status.dirtyScenesTotal, status.dirtyScenes.Length);
            Assert.GreaterOrEqual(status.dirtyPersistentAssetsTotal, status.dirtyPersistentAssets.Length);
            Assert.GreaterOrEqual(status.unsavedWindowsTotal, status.unsavedWindows.Length);
        }
        [Test] public void ExpiredSessionCannotMutateTheScene()
        {
            var name = "AuroraView stale session test";
            var request = new CallRequest { type = "call", id = "stale", method = "scene.create_cube",
                sessionId = "expired", @params = new CallParameters { name = name } };
            var result = JsonUtility.FromJson<CallFailure>(SceneContracts.Dispatch(JsonUtility.ToJson(request)));
            Assert.IsFalse(result.ok);
            StringAssert.Contains("session expired", result.error.message);
            Assert.IsNull(GameObject.Find(name));
        }
        [Test] public void CurrentSessionCanReadContext()
        {
            var request = new CallRequest { type = "call", id = "bound", method = "scene.context",
                sessionId = SceneContracts.SessionId };
            var result = JsonUtility.FromJson<CallSuccess>(SceneContracts.Dispatch(JsonUtility.ToJson(request)));
            Assert.IsTrue(result.ok);
            Assert.AreEqual(SceneContracts.SessionId, result.result.sessionId);
        }
        [Test] public void CubeCreationCanBeSelectedAndUndone()
        {
            var result = JsonUtility.FromJson<CallSuccess>(SceneContracts.Dispatch("{\"type\":\"call\",\"id\":\"create\",\"method\":\"scene.create_cube\",\"params\":{\"name\":\"Contract test cube\"}}"));
            Assert.IsTrue(result.ok);
            var id = result.result.created.objectId;
            try
            {
                Assert.AreEqual("Contract test cube", Selection.activeGameObject.name);
                var selected = JsonUtility.FromJson<CallSuccess>(SceneContracts.Dispatch("{\"type\":\"call\",\"id\":\"select\",\"method\":\"scene.select\",\"params\":{\"objectId\":" + id + "}}"));
                Assert.IsTrue(selected.ok);
                Undo.PerformUndo();
                Assert.IsNull(EditorUtility.InstanceIDToObject(id));
            }
            finally { var item = EditorUtility.InstanceIDToObject(id); if (item != null) UnityEngine.Object.DestroyImmediate(item); }
        }
        [TestCase("execute_code")][TestCase("scene.delete_all")]
        public void UnregisteredMethodsAreRefused(string method)
        {
            var result = JsonUtility.FromJson<CallFailure>(SceneContracts.Dispatch("{\"type\":\"call\",\"id\":\"test\",\"method\":\"" + method + "\"}"));
            Assert.IsFalse(result.ok);
            Assert.AreEqual("METHOD_NOT_FOUND", result.error.code);
        }
        [Test] public void BackgroundThreadCannotTouchTheScene()
        {
            Exception caught = null;
            var worker = new Thread(() => { try { SceneContracts.Dispatch("{}"); } catch (Exception error) { caught = error; } });
            worker.Start(); worker.Join();
            Assert.IsInstanceOf<InvalidOperationException>(caught);
        }
        [Test] public void InvalidObjectIsRefused()
        {
            var result = JsonUtility.FromJson<CallFailure>(SceneContracts.Dispatch("{\"type\":\"call\",\"id\":\"test\",\"method\":\"scene.select\",\"params\":{\"objectId\":0}}"));
            Assert.IsFalse(result.ok);
        }
        [Test] public void BrowserDispatcherCannotRequestEditorExit()
        {
            var request = new CallRequest { type = "call", id = "exit", method = "editor.exit",
                sessionId = SceneContracts.SessionId };
            var result = JsonUtility.FromJson<CallFailure>(SceneContracts.Dispatch(JsonUtility.ToJson(request)));
            Assert.IsFalse(result.ok);
            StringAssert.Contains("agent endpoint", result.error.message);
        }
        [Test] public void OwnedPreviewEndpointCanStopAndReopen()
        {
            for (var iteration = 0; iteration < 3; iteration++)
            {
                var previousSession = SceneContracts.SessionId;
                try
                {
                    AgentEndpoint.Enable();
                    Assert.AreNotEqual(previousSession, SceneContracts.SessionId);
                    var deadline = DateTime.UtcNow.AddSeconds(3);
                    while (!AgentEndpoint.IsListening && DateTime.UtcNow < deadline) Thread.Sleep(10);
                    Assert.IsTrue(AgentEndpoint.IsListening, "The current-user named pipe did not start: " + AgentEndpoint.LastError);
                }
                finally { AgentEndpoint.Disable(); }
                Assert.IsFalse(AgentEndpoint.Enabled, "Owned transport worker did not exit.");
                Assert.IsFalse(AgentEndpoint.IsListening);
            }
        }
    }
}
