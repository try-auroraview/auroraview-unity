using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace AuroraView.Unity
{
    [Serializable]
    public sealed class EditorOwner
    {
        public int processId;
        public string processCreationFileTime;
        public string projectPath;
        public string runId;

        public static void RequireMatch(EditorOwner expected, EditorOwner actual, string session, string currentSession)
        {
            long birth;
            if (expected == null || actual == null || expected.processId < 1 ||
                !long.TryParse(expected.processCreationFileTime, NumberStyles.None, CultureInfo.InvariantCulture, out birth) ||
                birth <= 0 || string.IsNullOrEmpty(expected.runId) || expected.runId.Length != 32 ||
                !Regex.IsMatch(expected.runId, "^[a-f0-9]{32}$") ||
                string.IsNullOrEmpty(expected.projectPath) || !Path.IsPathRooted(expected.projectPath) ||
                !string.Equals(Path.GetFullPath(expected.projectPath).TrimEnd(Path.DirectorySeparatorChar),
                    expected.projectPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrEmpty(session) || session != currentSession ||
                expected.processId != actual.processId ||
                expected.processCreationFileTime != actual.processCreationFileTime || expected.runId != actual.runId ||
                !string.Equals(Path.GetFullPath(expected.projectPath).TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetFullPath(actual.projectPath).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Exit requires the current session and exact owned test Editor PID, birth, project and run.");
        }

        public static void RequireClean(EditorStatus status)
        {
            if (status == null || status.dirtyScenes == null || status.dirtyPersistentAssets == null ||
                status.unsavedWindows == null || status.prefabStage == null || status.incomplete ||
                status.dirtyScenesTotal != status.dirtyScenes.Length ||
                status.dirtyPersistentAssetsTotal != status.dirtyPersistentAssets.Length ||
                status.unsavedWindowsTotal != status.unsavedWindows.Length)
                throw new InvalidOperationException("Editor status is incomplete; exit refused.");
            if (status.unsavedWindows.Length != 0 &&
                (status.dirtyPersistentAssets.Length == 0 || status.firstResourceBlockerIsWindow))
                throw new InvalidOperationException("Exit refused: an Editor window contains unsaved work.");
            RequireClean(status.isCompiling || status.isUpdating || status.isPlayingOrWillChangePlaymode,
                status.dirtyScenes.Length != 0, status.dirtyPersistentAssets.Length != 0, status.prefabStage.isOpen);
        }
        public static void RequireClean(bool busy, bool dirtyScene, bool dirtyAsset, bool prefabStage)
        {
            if (busy || dirtyScene || dirtyAsset || prefabStage)
                throw new InvalidOperationException("Exit refused: Editor is busy or contains unsaved scene, asset or Prefab work.");
        }
    }
}
