using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
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

        private static void RequireMainThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != SceneContracts.MainThreadId)
                throw new InvalidOperationException("Editor status requires the Editor main thread.");
        }

        internal static EditorStatus CaptureStatus()
        {
            RequireMainThread();
            return CaptureStatus(Current());
        }

        private static EditorStatus CaptureStatus(EditorOwner owner)
        {
            RequireMainThread();
            var status = new EditorStatus
            {
                sampledAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                editorOwner = owner,
                sessionId = SceneContracts.SessionId,
                mainThreadId = SceneContracts.MainThreadId,
                isCompiling = EditorApplication.isCompiling,
                isUpdating = EditorApplication.isUpdating,
                isPlayingOrWillChangePlaymode = EditorApplication.isPlayingOrWillChangePlaymode
            };
            var scenes = new List<DirtySceneInfo>();
            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                var scene = SceneManager.GetSceneAt(index);
                if (scene.isDirty)
                    scenes.Add(new DirtySceneInfo { name = scene.name, path = scene.path, handle = scene.handle });
            }
            var assets = new List<DirtyAssetInfo>();
            var windows = new List<UnsavedWindowInfo>();
            var resourceBlockerSeen = false;
            foreach (var item in Resources.FindObjectsOfTypeAll<UnityEngine.Object>())
            {
                if (item == null) continue;
                var window = item as EditorWindow;
                if (window != null && window.hasUnsavedChanges)
                {
                    windows.Add(new UnsavedWindowInfo { instanceId = window.GetInstanceID(), type = window.GetType().FullName });
                    if (!resourceBlockerSeen) status.firstResourceBlockerIsWindow = true;
                    resourceBlockerSeen = true;
                }
                if (EditorUtility.IsPersistent(item) && EditorUtility.IsDirty(item))
                {
                    assets.Add(new DirtyAssetInfo { instanceId = item.GetInstanceID(), path = AssetDatabase.GetAssetPath(item),
                        type = item.GetType().FullName, name = item.name });
                    resourceBlockerSeen = true;
                }
            }
            status.dirtyScenes = scenes.ToArray();
            status.dirtyPersistentAssets = assets.ToArray();
            status.unsavedWindows = windows.ToArray();
            status.dirtyScenesTotal = scenes.Count;
            status.dirtyPersistentAssetsTotal = assets.Count;
            status.unsavedWindowsTotal = windows.Count;
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null)
                status.prefabStage = new PrefabStageInfo { isOpen = true, assetPath = stage.assetPath,
                    scenePath = stage.scene.path, rootInstanceId = stage.prefabContentsRoot == null ? 0 : stage.prefabContentsRoot.GetInstanceID() };
            return status;
        }

        internal static void Validate(CallRequest request)
        {
            RequireMainThread();
            var actual = Current();
            EditorOwner.RequireMatch(request.@params == null ? null : request.@params.owner, actual, request.sessionId, SceneContracts.SessionId);
            var project = Argument("-projectPath");
            if (string.IsNullOrEmpty(project) || !Path.IsPathRooted(project) ||
                !string.Equals(Path.GetFullPath(project).TrimEnd(Path.DirectorySeparatorChar),
                    actual.projectPath.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Exit requires an explicit matching launch project.");
            EditorOwner.RequireClean(CaptureStatus(actual));
        }

        internal static void Exit(CallRequest request)
        {
            // Recheck on the main thread after the pipe ACK; never save or discard work.
            Validate(request);
            EditorApplication.Exit(0);
        }
    }
}
