using System;
using System.Collections.Generic;
using System.IO;
using AuroraView.Unity;

internal static class EditorOwnerCheck
{
    private static EditorOwner Owner()
    {
        return new EditorOwner { processId = 42, processCreationFileTime = "134000000000000000",
            projectPath = Path.GetFullPath("owned-project"), runId = new string('a', 32) };
    }

    private static void Refuses(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
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
        UnsavedWindowIsRefusedBeforeAndAfterAcceptance();
        Console.WriteLine("Editor owner and unsaved-work guards passed, including real Validate/Exit window branches against controlled Unity doubles; no Editor started.");
    }

    private static void UnsavedWindowIsRefusedBeforeAndAfterAcceptance()
    {
        var window = new UnityEditor.EditorWindow();
        UnityEngine.Resources.Items = new UnityEngine.Object[] { window };
        if (UnityEditor.EditorUtility.IsPersistent(window) || UnityEditor.EditorUtility.IsDirty(window))
            throw new Exception("The window fixture must bypass scene and persistent-asset dirtiness.");
        var request = new CallRequest { sessionId = SceneContracts.SessionId,
            @params = new CallParameters { owner = OwnedEditorExit.Current() } };
        OwnedEditorExit.Validate(request);
        window.hasUnsavedChanges = true;
        RefusesUnsavedWindow(() => OwnedEditorExit.Validate(request));
        RefusesUnsavedWindow(() => OwnedEditorExit.Exit(request));
        if (UnityEditor.EditorApplication.ExitCalls != 0)
            throw new Exception("Unsaved Editor window reached EditorApplication.Exit.");
        window.hasUnsavedChanges = false;
        OwnedEditorExit.Validate(request);
    }

    private static void RefusesUnsavedWindow(Action action)
    {
        try { action(); }
        catch (InvalidOperationException error)
        {
            if (error.Message.IndexOf("window", StringComparison.OrdinalIgnoreCase) >= 0 &&
                error.Message.IndexOf("unsaved", StringComparison.OrdinalIgnoreCase) >= 0) return;
            throw new Exception("Window fixture was rejected by an unrelated guard.", error);
        }
        throw new Exception("Unsaved Editor window was allowed to exit.");
    }
}

// Unity ignores this tests~ directory. These controlled API doubles compile the
// production OwnedEditorExit and execute its guards without loading an Editor.
namespace UnityEngine
{
    public class Object { }
    public static class Application
    {
        public static string dataPath { get { return Path.GetFullPath("owned-project/Assets"); } }
    }
    public static class Resources
    {
        public static Object[] Items = new Object[0];
        public static T[] FindObjectsOfTypeAll<T>() where T : Object
        {
            var result = new List<T>();
            foreach (var item in Items) if (item is T) result.Add((T)item);
            return result.ToArray();
        }
    }
}
namespace UnityEditor
{
    public sealed class EditorWindow : UnityEngine.Object { public bool hasUnsavedChanges { get; set; } }
    public static class EditorUtility
    {
        public static bool IsPersistent(UnityEngine.Object item) { return false; }
        public static bool IsDirty(UnityEngine.Object item) { return false; }
    }
    public static class EditorApplication
    {
        public static bool isCompiling { get { return false; } }
        public static bool isUpdating { get { return false; } }
        public static bool isPlayingOrWillChangePlaymode { get { return false; } }
        public static int ExitCalls { get; private set; }
        public static void Exit(int code) { ExitCalls++; }
    }
}
namespace UnityEditor.SceneManagement
{
    public static class PrefabStageUtility { public static object GetCurrentPrefabStage() { return null; } }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene { public bool isDirty { get { return false; } } }
    public static class SceneManager
    {
        public static int sceneCount { get { return 0; } }
        public static Scene GetSceneAt(int index) { return new Scene(); }
    }
}
namespace AuroraView.Unity
{
    public sealed class CallParameters { public EditorOwner owner { get; set; } }
    public sealed class CallRequest
    {
        public CallParameters @params { get; set; }
        public string sessionId { get; set; }
    }
    public static class SceneContracts { public static string SessionId { get { return new string('a', 32); } } }
}
