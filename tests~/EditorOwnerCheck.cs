using System;
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

    private static void Main()
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
        Console.WriteLine("Editor owner and unsaved-work guards passed; no Editor started.");
    }
}
