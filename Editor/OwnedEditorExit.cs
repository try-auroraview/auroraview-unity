using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AuroraView.Unity
{
    internal static class OwnedEditorExit
    {
        internal static EditorOwner Current()
        {
            using (var process = Process.GetCurrentProcess())
                return new EditorOwner
                {
                    processId = process.Id,
                    processCreationFileTime = process.StartTime.ToFileTimeUtc().ToString(CultureInfo.InvariantCulture),
                    projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                    runId = Argument("-auroraviewOwnedTest")
                };
        }

        private static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            string value = null;
            for (var index = 0; index < args.Length; index++)
            {
                if (!string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase)) continue;
                if (value != null || index + 1 == args.Length) throw new InvalidOperationException("Duplicate or incomplete owned Editor argument.");
                value = args[index + 1];
            }
            return value;
        }

        internal static void Validate(CallRequest request)
        {
            var actual = Current();
            EditorOwner.RequireMatch(request.@params == null ? null : request.@params.owner, actual, request.sessionId, SceneContracts.SessionId);
            var project = Argument("-projectPath");
            if (string.IsNullOrEmpty(project) || !Path.IsPathRooted(project) ||
                !string.Equals(Path.GetFullPath(project).TrimEnd(Path.DirectorySeparatorChar),
                    actual.projectPath.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Exit requires an explicit matching launch project.");
            var dirtyScene = false;
            for (var index = 0; index < SceneManager.sceneCount; index++)
                dirtyScene |= SceneManager.GetSceneAt(index).isDirty;
            var dirtyAsset = false;
            foreach (var item in Resources.FindObjectsOfTypeAll<UnityEngine.Object>())
            {
                var window = item as EditorWindow;
                if (window != null && window.hasUnsavedChanges)
                    throw new InvalidOperationException("Exit refused: an Editor window contains unsaved work.");
                if (EditorUtility.IsPersistent(item) && EditorUtility.IsDirty(item)) { dirtyAsset = true; break; }
            }
            EditorOwner.RequireClean(EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode, dirtyScene, dirtyAsset,
                PrefabStageUtility.GetCurrentPrefabStage() != null);
        }

        internal static void Exit(CallRequest request)
        {
            // Recheck on the main thread after the pipe ACK; never save or discard work.
            Validate(request);
            EditorApplication.Exit(0);
        }
    }
}
