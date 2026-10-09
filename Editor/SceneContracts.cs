using System;
using System.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AuroraView.Unity
{
    [Serializable] public sealed class CallParameters { public string name; public int objectId; public EditorOwner owner; }
    [Serializable] public sealed class CallRequest
    {
        public string type;
        public string id;
        public string method;
        public string sessionId;
        public CallParameters @params;
    }
    [Serializable] public sealed class ObjectInfo
    {
        public int objectId;
        public string name;
        public float x;
        public float y;
        public float z;
    }
    [Serializable] public sealed class HostResult
    {
        public string unityVersion;
        public string sessionId;
        public string scene;
        public int processId;
        public int mainThreadId;
        public ObjectInfo[] selection;
        public ObjectInfo created;
        public EditorOwner editorOwner;
        public bool exitRequested;
    }
    [Serializable] public sealed class CallSuccess { public string id; public bool ok = true; public HostResult result; }
    [Serializable] public sealed class CallError { public string code; public string message; }
    [Serializable] public sealed class CallFailure { public string id; public bool ok; public CallError error; }

    // This allowlist is shared by the human Web UI and the explicitly enabled agent endpoint.
    [InitializeOnLoad]
    public static class SceneContracts
    {
        public static string SessionId { get; private set; } = Guid.NewGuid().ToString("N");
        public static readonly int MainThreadId = Thread.CurrentThread.ManagedThreadId;
        public static readonly string[] Methods = { "scene.context", "scene.create_cube", "scene.select", "editor.exit" };

        internal static void RenewSession()
        {
            if (Thread.CurrentThread.ManagedThreadId != MainThreadId)
                throw new InvalidOperationException("Unity sessions require the Editor main thread.");
            SessionId = Guid.NewGuid().ToString("N");
        }

        public static string Dispatch(string json) => Dispatch(json, null);

        internal static string Dispatch(string json, Action<CallRequest> requestExit)
        {
            if (Thread.CurrentThread.ManagedThreadId != MainThreadId)
                throw new InvalidOperationException("Unity contracts require the Editor main thread.");
            CallRequest request = null;
            try
            {
                if (json == null || json.Length > 65536) throw new ArgumentException("Request exceeds 64 KiB.");
                request = JsonUtility.FromJson<CallRequest>(json);
                if (request == null || request.type != "call" || string.IsNullOrEmpty(request.id) || request.id.Length > 128)
                    throw new ArgumentException("Expected a call with a bounded nonempty id.");
                if (!string.IsNullOrEmpty(request.sessionId) && request.sessionId != SessionId)
                    throw new InvalidOperationException("Unity session expired; explicitly attach the current Editor session.");
                if (request.method == "editor.exit")
                {
                    if (requestExit == null) throw new InvalidOperationException("Editor exit is available only through the opted-in agent endpoint.");
                    OwnedEditorExit.Validate(request);
                }
                var result = Execute(request.method, request.@params ?? new CallParameters());
                if (request.method == "editor.exit") result.exitRequested = true;
                var response = JsonUtility.ToJson(new CallSuccess { id = request.id, result = result });
                if (request.method == "editor.exit") requestExit(request);
                return response;
            }
            catch (Exception error)
            {
                return JsonUtility.ToJson(new CallFailure
                {
                    id = request != null ? request.id : null,
                    error = new CallError
                    {
                        code = error is MissingMethodException ? "METHOD_NOT_FOUND" : "INVALID_REQUEST",
                        message = error.Message
                    }
                });
            }
        }

        private static HostResult Execute(string method, CallParameters parameters)
        {
            ObjectInfo created = null;
            switch (method)
            {
                case "scene.context":
                case "editor.exit": break;
                case "scene.create_cube":
                    if (EditorApplication.isPlayingOrWillChangePlaymode)
                        throw new InvalidOperationException("Scene editing is disabled during Play mode.");
                    var name = string.IsNullOrEmpty(parameters.name) ? "AuroraView Cube" : parameters.name.Trim();
                    if (name.Length == 0 || name.Length > 80 || name.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
                        throw new ArgumentException("Cube name must contain 1–80 characters and no control lines.");
                    var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cube.name = name;
                    Undo.RegisterCreatedObjectUndo(cube, "Create AuroraView cube");
                    Selection.activeGameObject = cube;
                    EditorSceneManager.MarkSceneDirty(cube.scene);
                    created = Describe(cube);
                    break;
                case "scene.select":
                    var item = EditorUtility.InstanceIDToObject(parameters.objectId) as GameObject;
                    if (item == null || !item.scene.IsValid() || EditorUtility.IsPersistent(item))
                        throw new ArgumentException("objectId must identify a live scene GameObject.");
                    Selection.activeGameObject = item;
                    break;
                default: throw new MissingMethodException("Method is not registered: " + method);
            }
            var selection = Selection.gameObjects;
            var items = new ObjectInfo[selection.Length];
            for (var index = 0; index < selection.Length; index++) items[index] = Describe(selection[index]);
            return new HostResult
            {
                unityVersion = Application.unityVersion,
                sessionId = SessionId,
                scene = SceneManager.GetActiveScene().name,
                processId = System.Diagnostics.Process.GetCurrentProcess().Id,
                mainThreadId = MainThreadId,
                selection = items,
                created = created,
                editorOwner = OwnedEditorExit.Current()
            };
        }

        private static ObjectInfo Describe(GameObject item)
        {
            var position = item.transform.position;
            return new ObjectInfo { objectId = item.GetInstanceID(), name = item.name, x = position.x, y = position.y, z = position.z };
        }
    }
}
