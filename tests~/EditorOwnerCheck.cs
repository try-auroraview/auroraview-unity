using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Text;
using AuroraView.Unity;

internal static class EditorOwnerCheck
{
    private static EditorOwner Owner()
    {
        return new EditorOwner { processId = 42, processCreationFileTime = "134000000000000000",
            projectPath = Path.GetFullPath("owned-project"), runId = new string('a', 32) };
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Refuses(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new Exception("Unsafe Editor exit was accepted.");
    }

    private static void RefusesWith(Action action, string message)
    {
        try { action(); }
        catch (InvalidOperationException error)
        {
            if (error.Message == message) return;
            throw new Exception("Fixture was rejected by an unrelated guard.", error);
        }
        throw new Exception("Unsafe Editor exit was accepted.");
    }

    private static int Main()
    {
        try { Run(); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void Run()
    {
        var actual = Owner();
        EditorOwner.RequireMatch(Owner(), actual, "current", "current");
        var foreign = Owner(); foreign.processId++;
        Refuses(() => EditorOwner.RequireMatch(foreign, actual, "current", "current"));
        foreign = Owner(); foreign.processCreationFileTime = "134000000000000001";
        Refuses(() => EditorOwner.RequireMatch(foreign, actual, "current", "current"));
        foreign = Owner(); foreign.projectPath = Path.GetFullPath("other-project");
        Refuses(() => EditorOwner.RequireMatch(foreign, actual, "current", "current"));
        foreign = Owner(); foreign.projectPath = "relative-project";
        Refuses(() => EditorOwner.RequireMatch(foreign, actual, "current", "current"));
        foreign = Owner(); foreign.projectPath = Path.GetPathRoot(actual.projectPath).TrimEnd(Path.DirectorySeparatorChar) + "owned-project";
        Refuses(() => EditorOwner.RequireMatch(foreign, actual, "current", "current"));
        foreign = Owner(); foreign.runId = new string('b', 32);
        Refuses(() => EditorOwner.RequireMatch(foreign, actual, "current", "current"));
        foreign = Owner(); foreign.runId = null;
        Refuses(() => EditorOwner.RequireMatch(foreign, actual, "current", "current"));
        foreign = Owner(); foreign.processCreationFileTime = "1e17";
        Refuses(() => EditorOwner.RequireMatch(foreign, actual, "current", "current"));
        Refuses(() => EditorOwner.RequireMatch(Owner(), actual, "stale", "current"));
        Refuses(() => EditorOwner.RequireMatch(Owner(), actual, null, "current"));
        Refuses(() => EditorOwner.RequireMatch(null, actual, "current", "current"));
        actual.runId = null;
        Refuses(() => EditorOwner.RequireMatch(Owner(), actual, "current", "current"));
        EditorOwner.RequireClean(false, false, false, false);
        Refuses(() => EditorOwner.RequireClean(true, false, false, false));
        Refuses(() => EditorOwner.RequireClean(false, true, false, false));
        Refuses(() => EditorOwner.RequireClean(false, false, true, false));
        Refuses(() => EditorOwner.RequireClean(false, false, false, true));
        CleanStatusIsBoundAndReadOnly();
        SnapshotReportsAllBlockers();
        EverySnapshotBlockerRefusesExit();
        UnsavedWindowIsRefusedBeforeAndAfterAcceptance();
        StatusDoesNotAuthorizeLaterExit();
        BackgroundThreadCannotCaptureStatus();
        InternalContextDoesNotScan();
        PublicContextFitsTransport();
        IdentityOverflowIsBoundedFailure();
        IncompleteSnapshotCannotAuthorizeExit();
        Console.WriteLine("Editor owner and shared status guards passed: clean snapshot, simultaneous blockers, signed IDs/empty paths, every blocker, unchanged errors, fresh Exit recheck and main-thread-only capture; controlled Unity doubles, no Editor started.");
    }

    private static CallRequest Request()
    {
        return new CallRequest { sessionId = SceneContracts.SessionId,
            @params = new CallParameters { owner = OwnedEditorExit.Current() } };
    }

    private static void Reset()
    {
        UnityEngine.Resources.Items = new UnityEngine.Object[0];
        UnityEngine.Resources.Reads = 0;
        UnityEngine.SceneManagement.SceneManager.Scenes = new UnityEngine.SceneManagement.Scene[0];
        UnityEditor.SceneManagement.PrefabStageUtility.Current = null;
        UnityEditor.EditorApplication.isCompiling = false;
        UnityEditor.EditorApplication.isUpdating = false;
        UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode = false;
        UnityEditor.EditorApplication.ResetExitCalls();
        UnityEditor.Selection.gameObjects = new UnityEngine.GameObject[0];
        UnityEditor.Selection.activeGameObject = null;
    }

    private static void CleanStatusIsBoundAndReadOnly()
    {
        Reset();
        var before = DateTime.UtcNow;
        var status = OwnedEditorExit.CaptureStatus();
        DateTime sampled;
        Check(DateTime.TryParseExact(status.sampledAtUtc, "o", CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out sampled) && sampled.Kind == DateTimeKind.Utc &&
            sampled >= before && sampled <= DateTime.UtcNow, "Status lacks an actual bounded UTC sample time.");
        Check(status.sessionId == SceneContracts.SessionId && status.mainThreadId == Thread.CurrentThread.ManagedThreadId,
            "Snapshot session/main-thread identity differs.");
        var owner = OwnedEditorExit.Current();
        EditorOwner.RequireMatch(status.editorOwner, owner, status.sessionId, SceneContracts.SessionId);
        Check(!status.isCompiling && !status.isUpdating && !status.isPlayingOrWillChangePlaymode &&
            status.dirtyScenes.Length == 0 && status.dirtyPersistentAssets.Length == 0 &&
            status.unsavedWindows.Length == 0 && !status.prefabStage.isOpen &&
            status.prefabStage.assetPath == "" && status.prefabStage.scenePath == "" && status.prefabStage.rootInstanceId == 0,
            "Clean snapshot contains a synthetic blocker.");
        EditorOwner.RequireClean(status);
        OwnedEditorExit.Validate(Request());
        Check(UnityEditor.EditorApplication.ExitCalls == 0, "Read-only snapshot/validation exited the Editor.");
        Refuses(() => EditorOwner.RequireClean((EditorStatus)null));
    }

    private static void SnapshotReportsAllBlockers()
    {
        Reset();
        UnityEditor.EditorApplication.isCompiling = true;
        UnityEditor.EditorApplication.isUpdating = true;
        UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode = true;
        var dirtyScene = new UnityEngine.SceneManagement.Scene { isDirty = true, name = "Untitled", path = "", handle = -3 };
        UnityEngine.SceneManagement.SceneManager.Scenes = new[] {
            dirtyScene, new UnityEngine.SceneManagement.Scene { name = "Clean", path = "Assets/Clean.unity", handle = 4 } };
        var asset = new UnityEngine.Object { InstanceId = -9, name = "Unsaved asset", Persistent = true, Dirty = true, AssetPath = "" };
        var otherAsset = new UnityEngine.Object { InstanceId = -11, name = "Second asset", Persistent = true, Dirty = true, AssetPath = "Assets/Second.asset" };
        var window = new UnityEditor.EditorWindow { InstanceId = -17, hasUnsavedChanges = true };
        UnityEngine.Resources.Items = new UnityEngine.Object[] { asset, window, otherAsset };
        var stage = new UnityEditor.SceneManagement.PrefabStage { assetPath = "Assets/Prefab.prefab",
            scene = new UnityEngine.SceneManagement.Scene { path = "", handle = -19 },
            prefabContentsRoot = new UnityEngine.Object { InstanceId = -23 } };
        UnityEditor.SceneManagement.PrefabStageUtility.Current = stage;
        var status = OwnedEditorExit.CaptureStatus();
        Check(status.isCompiling && status.isUpdating && status.isPlayingOrWillChangePlaymode, "Busy flags were collapsed.");
        Check(status.dirtyScenes.Length == 1 && status.dirtyScenes[0].handle == -3 &&
            status.dirtyScenes[0].name == "Untitled" && status.dirtyScenes[0].path == "", "Dirty scene metadata differs.");
        Check(status.dirtyPersistentAssets.Length == 2 && status.dirtyPersistentAssets[0].instanceId == -9 &&
            status.dirtyPersistentAssets[0].path == "" && status.dirtyPersistentAssets[0].name == asset.name &&
            status.dirtyPersistentAssets[0].type == typeof(UnityEngine.Object).FullName &&
            status.dirtyPersistentAssets[1].instanceId == -11, "Full dirty assets or signed IDs/empty paths were lost.");
        Check(status.unsavedWindows.Length == 1 && status.unsavedWindows[0].instanceId == -17 &&
            status.unsavedWindows[0].type == typeof(UnityEditor.EditorWindow).FullName, "Unsaved window metadata differs.");
        Check(status.prefabStage.isOpen && status.prefabStage.assetPath == stage.assetPath &&
            status.prefabStage.scenePath == "" && status.prefabStage.rootInstanceId == -23, "Prefab Stage metadata differs.");
        RefusesWith(() => EditorOwner.RequireClean(status),
            "Exit refused: Editor is busy or contains unsaved scene, asset or Prefab work.");
        UnityEngine.Resources.Items = new UnityEngine.Object[] { window, asset, otherAsset };
        RefusesWith(() => EditorOwner.RequireClean(OwnedEditorExit.CaptureStatus()),
            "Exit refused: an Editor window contains unsaved work.");
        Check(asset.Dirty && window.hasUnsavedChanges && UnityEngine.SceneManagement.SceneManager.Scenes[0].isDirty &&
            UnityEditor.SceneManagement.PrefabStageUtility.Current == stage && UnityEditor.EditorApplication.ExitCalls == 0,
            "Diagnostic capture mutated unsaved work.");
        Reset();
    }

    private static void EverySnapshotBlockerRefusesExit()
    {
        var blockers = new Action[] {
            () => UnityEditor.EditorApplication.isCompiling = true,
            () => UnityEditor.EditorApplication.isUpdating = true,
            () => UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode = true,
            () => UnityEngine.SceneManagement.SceneManager.Scenes = new[] { new UnityEngine.SceneManagement.Scene { isDirty = true } },
            () => UnityEngine.Resources.Items = new[] { new UnityEngine.Object { Persistent = true, Dirty = true } },
            () => UnityEditor.SceneManagement.PrefabStageUtility.Current = new UnityEditor.SceneManagement.PrefabStage()
        };
        foreach (var blocker in blockers)
        {
            Reset();
            blocker();
            RefusesWith(() => OwnedEditorExit.Validate(Request()),
                "Exit refused: Editor is busy or contains unsaved scene, asset or Prefab work.");
            RefusesWith(() => OwnedEditorExit.Exit(Request()),
                "Exit refused: Editor is busy or contains unsaved scene, asset or Prefab work.");
            Check(UnityEditor.EditorApplication.ExitCalls == 0, "A busy/dirty blocker reached EditorApplication.Exit.");
        }
        Reset();
    }

    private static void UnsavedWindowIsRefusedBeforeAndAfterAcceptance()
    {
        Reset();
        var window = new UnityEditor.EditorWindow();
        UnityEngine.Resources.Items = new UnityEngine.Object[] { window };
        Check(!UnityEditor.EditorUtility.IsPersistent(window) && !UnityEditor.EditorUtility.IsDirty(window),
            "The window fixture must bypass scene and persistent-asset dirtiness.");
        var request = Request();
        OwnedEditorExit.Validate(request);
        window.hasUnsavedChanges = true;
        RefusesWith(() => OwnedEditorExit.Validate(request), "Exit refused: an Editor window contains unsaved work.");
        RefusesWith(() => OwnedEditorExit.Exit(request), "Exit refused: an Editor window contains unsaved work.");
        Check(UnityEditor.EditorApplication.ExitCalls == 0, "Unsaved Editor window reached EditorApplication.Exit.");
        window.hasUnsavedChanges = false;
        OwnedEditorExit.Validate(request);
        Reset();
    }

    private static void StatusDoesNotAuthorizeLaterExit()
    {
        Reset();
        var request = Request();
        OwnedEditorExit.Validate(request);
        var clean = OwnedEditorExit.CaptureStatus();
        UnityEditor.EditorApplication.isUpdating = true;
        Refuses(() => OwnedEditorExit.Exit(request));
        Check(!clean.isUpdating && UnityEditor.EditorApplication.ExitCalls == 0, "A prior clean snapshot authorized a later dirty exit.");
        Reset();
    }

    private static void InternalContextDoesNotScan()
    {
        Reset();
        UnityEngine.Resources.Items = new[] { new UnityEngine.Object { Persistent = true, Dirty = true } };
        UnityEditor.Selection.gameObjects = new[] { new UnityEngine.GameObject { InstanceId = -41, name = "Selected" } };
        var light = SceneContracts.ReadContext();
        Check(UnityEngine.Resources.Reads == 0 && light.editorStatus == null && light.selectionTotal == 1 &&
            light.selection[0].objectId == -41, "Internal selection context triggered the full status scan.");
        var json = SceneContracts.Dispatch("{\"type\":\"call\",\"id\":\"public\",\"method\":\"scene.context\"}");
        var full = UnityEngine.JsonUtility.FromJson<CallSuccess>(json);
        Check(full.ok && full.result.editorStatus.dirtyPersistentAssetsTotal == 1 &&
            UnityEngine.Resources.Reads == 1, "Public context skipped or repeated its full status scan.");
        var source = File.ReadAllText("Editor/AuroraViewWindow.cs");
        var publish = source.Substring(source.IndexOf("private void PublishContext()", StringComparison.Ordinal));
        Check(publish.Contains("SceneContracts.ReadContext()") && !publish.Contains("SceneContracts.Dispatch("),
            "Window selection callback no longer uses the light context path.");
        Reset();
    }

    private static void PublicContextFitsTransport()
    {
        Reset();
        var text = new StringBuilder();
        for (var index = 0; index < 1000; index++) text.Append("\u0001\"\\\u4e2d");
        var large = text.ToString();
        var resources = new List<UnityEngine.Object>();
        var scenes = new UnityEngine.SceneManagement.Scene[100];
        var selected = new UnityEngine.GameObject[100];
        for (var index = 0; index < 100; index++)
        {
            resources.Add(new UnityEngine.Object { InstanceId = -1000 - index, Persistent = true, Dirty = true,
                name = large, AssetPath = large });
            resources.Add(new UnityEditor.EditorWindow { InstanceId = -2000 - index, hasUnsavedChanges = true });
            scenes[index] = new UnityEngine.SceneManagement.Scene { name = large, path = large, handle = -3000 - index, isDirty = true };
            selected[index] = new UnityEngine.GameObject { InstanceId = -4000 - index, name = large };
        }
        UnityEngine.Resources.Items = resources.ToArray();
        UnityEngine.SceneManagement.SceneManager.Scenes = scenes;
        UnityEditor.Selection.gameObjects = selected;
        var json = SceneContracts.Dispatch("{\"type\":\"call\",\"id\":\"bounded\",\"method\":\"scene.context\"}");
        Check(Encoding.UTF8.GetByteCount(json) <= ContextResponse.MaxResponseBytes &&
            Encoding.UTF8.GetByteCount(json + "\r\n") <= 65536, "Whole serialized response exceeds the transport byte budget.");
        var result = UnityEngine.JsonUtility.FromJson<CallSuccess>(json);
        Check(result.ok && result.result.contextIncomplete && result.result.selectionTotal == 100 &&
            result.result.selection.Length <= ContextResponse.MaxEntries, "Selection truncation or total was hidden.");
        var status = result.result.editorStatus;
        Check(status.incomplete && status.dirtyScenesTotal == 100 && status.dirtyPersistentAssetsTotal == 100 &&
            status.unsavedWindowsTotal == 100 && status.dirtyScenes.Length <= ContextResponse.MaxEntries &&
            status.dirtyPersistentAssets.Length <= ContextResponse.MaxEntries && status.unsavedWindows.Length <= ContextResponse.MaxEntries,
            "Diagnostic projection hid omitted blockers.");
        Check(result.result.editorOwner.processCreationFileTime == status.editorOwner.processCreationFileTime &&
            result.result.editorOwner.projectPath == status.editorOwner.projectPath &&
            result.result.sessionId == status.sessionId, "Diagnostic projection truncated identity.");
        foreach (var asset in status.dirtyPersistentAssets)
            Check(Encoding.UTF8.GetByteCount(asset.name) <= ContextResponse.MaxTextBytes &&
                Encoding.UTF8.GetByteCount(asset.path) <= ContextResponse.MaxTextBytes, "Display text exceeds its UTF-8 byte limit.");
        RefusesWith(() => EditorOwner.RequireClean(status), "Editor status is incomplete; exit refused.");
        var complete = OwnedEditorExit.CaptureStatus();
        Check(!complete.incomplete && complete.dirtyScenes.Length == 100 && complete.dirtyPersistentAssets.Length == 100 &&
            complete.unsavedWindows.Length == 100 && complete.dirtyPersistentAssets[0].name == large,
            "Projection polluted the full guard snapshot.");
        Refuses(() => EditorOwner.RequireClean(complete));
        Check(UnityEditor.EditorApplication.ExitCalls == 0, "Diagnostic projection authorized exit.");
        Reset();
        var shortContext = SceneContracts.ReadContext();
        shortContext.scene = new string('a', 255) + "\ud83d\ude00tail";
        var shortJson = ContextResponse.Serialize("unicode", shortContext);
        var shortResult = UnityEngine.JsonUtility.FromJson<CallSuccess>(shortJson).result;
        Check(shortResult.contextIncomplete && shortResult.scene.Length == 255 &&
            !char.IsHighSurrogate(shortResult.scene[shortResult.scene.Length - 1]), "Text projection split a surrogate pair.");
    }

    private static void IdentityOverflowIsBoundedFailure()
    {
        Reset();
        var source = SceneContracts.ReadContext();
        source.editorOwner.projectPath = new string('\u4e2d', 30000);
        RefusesWith(() => ContextResponse.Serialize("identity", source), "Context identity exceeds the response byte budget.");
        var failure = ContextResponse.Failure("identity", "INVALID_REQUEST", "Context identity exceeds the response byte budget.");
        Check(Encoding.UTF8.GetByteCount(failure + "\r\n") <= 65536 &&
            UnityEngine.JsonUtility.FromJson<CallFailure>(failure).id == "identity" &&
            !failure.Contains(source.editorOwner.projectPath), "Oversize identity leaked into an unbounded failure.");
        var request = new CallRequest { type = "call", id = "unknown", method = new string('\u0001', 10000) };
        var invalid = SceneContracts.Dispatch(UnityEngine.JsonUtility.ToJson(request));
        Check(Encoding.UTF8.GetByteCount(invalid + "\r\n") <= 65536 &&
            !UnityEngine.JsonUtility.FromJson<CallFailure>(invalid).ok, "Error response exceeds the transport budget.");
    }

    private static void IncompleteSnapshotCannotAuthorizeExit()
    {
        Reset();
        var status = OwnedEditorExit.CaptureStatus();
        status.incomplete = true;
        RefusesWith(() => EditorOwner.RequireClean(status), "Editor status is incomplete; exit refused.");
        status = OwnedEditorExit.CaptureStatus();
        status.dirtyScenesTotal = 1;
        RefusesWith(() => EditorOwner.RequireClean(status), "Editor status is incomplete; exit refused.");
        var json = SceneContracts.Dispatch("{\"type\":\"call\",\"id\":\"clean\",\"method\":\"scene.context\"}");
        var clean = UnityEngine.JsonUtility.FromJson<CallSuccess>(json);
        Check(clean.ok && !clean.result.contextIncomplete && !clean.result.editorStatus.incomplete &&
            clean.result.selectionTotal == 0 && clean.result.editorStatus.dirtyScenesTotal == 0, "Clean context falsely reports truncation.");
    }
    private static void BackgroundThreadCannotCaptureStatus()
    {
        Reset();
        Exception caught = null;
        var worker = new Thread(() => { try { OwnedEditorExit.CaptureStatus(); } catch (Exception error) { caught = error; } });
        worker.Start(); worker.Join();
        Check(caught is InvalidOperationException && UnityEngine.Resources.Reads == 0,
            "Background capture accessed Unity state.");
    }
}

// Unity ignores tests~. Controlled API doubles compile the production snapshot
// and exit guard without loading an Editor or accessing any real project state.
namespace UnityEngine
{
    public class Object
    {
        public int InstanceId;
        public string name = "";
        public bool Persistent;
        public bool Dirty;
        public string AssetPath = "";
        public int GetInstanceID() { return InstanceId; }
    }
    public enum PrimitiveType { Cube }
    public struct Vector3 { public float x; public float y; public float z; }
    public sealed class Transform { public Vector3 position; }
    public sealed class GameObject : Object
    {
        public Transform transform = new Transform();
        public UnityEngine.SceneManagement.Scene scene = new UnityEngine.SceneManagement.Scene();
        public static GameObject CreatePrimitive(PrimitiveType type) { return new GameObject(); }
    }
    public static class JsonUtility
    {
        // A serializer double: executable budget tests do not prove Unity JsonUtility behavior.
        private static System.Web.Script.Serialization.JavaScriptSerializer Serializer()
        { return new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue }; }
        public static string ToJson(object value) { return Serializer().Serialize(value); }
        public static T FromJson<T>(string json) { return Serializer().Deserialize<T>(json); }
    }
    public static class Application
    {
        public static string unityVersion = "Controlled Unity double";
        public static string dataPath { get { return Path.GetFullPath("owned-project/Assets"); } }
    }
    public static class Resources
    {
        public static Object[] Items = new Object[0];
        public static int Reads;
        public static T[] FindObjectsOfTypeAll<T>() where T : Object
        {
            Reads++;
            var result = new List<T>();
            foreach (var item in Items) if (item is T) result.Add((T)item);
            return result.ToArray();
        }
    }
}
namespace UnityEditor
{
    public sealed class InitializeOnLoadAttribute : Attribute { }
    public static class Selection
    {
        public static UnityEngine.GameObject[] gameObjects = new UnityEngine.GameObject[0];
        public static UnityEngine.GameObject activeGameObject;
    }
    public static class Undo { public static void RegisterCreatedObjectUndo(UnityEngine.Object item, string name) { } }
    public sealed class EditorWindow : UnityEngine.Object { public bool hasUnsavedChanges { get; set; } }
    public static class EditorUtility
    {
        public static UnityEngine.Object InstanceIDToObject(int id) { return null; }
        public static bool IsPersistent(UnityEngine.Object item) { return item.Persistent; }
        public static bool IsDirty(UnityEngine.Object item) { return item.Dirty; }
    }
    public static class AssetDatabase
    {
        public static string GetAssetPath(UnityEngine.Object item) { return item.AssetPath; }
    }
    public static class EditorApplication
    {
        public static bool isCompiling;
        public static bool isUpdating;
        public static bool isPlayingOrWillChangePlaymode;
        public static int ExitCalls { get; private set; }
        public static void ResetExitCalls() { ExitCalls = 0; }
        public static void Exit(int code) { ExitCalls++; }
    }
}
namespace UnityEditor.SceneManagement
{
    public static class EditorSceneManager { public static void MarkSceneDirty(UnityEngine.SceneManagement.Scene scene) { } }
    public sealed class PrefabStage
    {
        public string assetPath = "";
        public UnityEngine.SceneManagement.Scene scene;
        public UnityEngine.Object prefabContentsRoot;
    }
    public static class PrefabStageUtility
    {
        public static PrefabStage Current;
        public static PrefabStage GetCurrentPrefabStage() { return Current; }
    }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene
    {
        public bool isDirty;
        public bool IsValid() { return true; }
        public string name;
        public string path;
        public int handle;
    }
    public static class SceneManager
    {
        public static Scene[] Scenes = new Scene[0];
        public static int sceneCount { get { return Scenes.Length; } }
        public static Scene GetSceneAt(int index) { return Scenes[index]; }
        public static Scene GetActiveScene() { return Scenes.Length == 0 ? new Scene { name = "Clean" } : Scenes[0]; }
    }
}
