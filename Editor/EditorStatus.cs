using System;

namespace AuroraView.Unity
{
    [Serializable] public sealed class DirtySceneInfo
    {
        public string name;
        public string path;
        public int handle;
    }
    [Serializable] public sealed class DirtyAssetInfo
    {
        public int instanceId;
        public string path;
        public string type;
        public string name;
    }
    [Serializable] public sealed class PrefabStageInfo
    {
        public bool isOpen;
        public string assetPath = "";
        public string scenePath = "";
        public int rootInstanceId;
    }
    [Serializable] public sealed class UnsavedWindowInfo
    {
        public int instanceId;
        public string type;
    }
    [Serializable] public sealed class EditorStatus
    {
        public string sampledAtUtc;
        public bool isCompiling;
        public bool isUpdating;
        public bool isPlayingOrWillChangePlaymode;
        public DirtySceneInfo[] dirtyScenes = new DirtySceneInfo[0];
        public int dirtyScenesTotal;
        public DirtyAssetInfo[] dirtyPersistentAssets = new DirtyAssetInfo[0];
        public int dirtyPersistentAssetsTotal;
        public PrefabStageInfo prefabStage = new PrefabStageInfo();
        public UnsavedWindowInfo[] unsavedWindows = new UnsavedWindowInfo[0];
        public int unsavedWindowsTotal;
        public bool incomplete;
        public EditorOwner editorOwner;
        public string sessionId;
        public int mainThreadId;

        // Preserve the existing error priority while reporting every blocker.
        internal bool firstResourceBlockerIsWindow;
    }
}
