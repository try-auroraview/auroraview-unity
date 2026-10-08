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
        [Test] public void OwnedPreviewEndpointCanStopAndReopen()
        {
            for (var iteration = 0; iteration < 3; iteration++)
            {
                try
                {
                    AgentEndpoint.Enable();
                    var deadline = DateTime.UtcNow.AddSeconds(3);
                    while (!AgentEndpoint.IsListening && DateTime.UtcNow < deadline) Thread.Sleep(10);
                    Assert.IsTrue(AgentEndpoint.IsListening, "The current-user named pipe did not start.");
                }
                finally { AgentEndpoint.Disable(); }
                Assert.IsFalse(AgentEndpoint.Enabled, "Owned transport worker did not exit.");
                Assert.IsFalse(AgentEndpoint.IsListening);
            }
        }
    }
}
