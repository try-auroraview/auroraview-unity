using System;
using System.Text;
using UnityEngine;

namespace AuroraView.Unity
{
    internal static class ContextResponse
    {
        // The transport admits 64 KiB including its newline; reserve envelope headroom.
        internal const int MaxResponseBytes = 60 * 1024;
        internal const int MaxEntries = 32;
        internal const int MaxTextBytes = 256;

        internal static string Serialize(string id, HostResult source)
        {
            var entries = MaxEntries;
            var textBytes = MaxTextBytes;
            while (true)
            {
                var projected = Project(source, entries, textBytes);
                var json = JsonUtility.ToJson(new CallSuccess { id = id, result = projected });
                if (Encoding.UTF8.GetByteCount(json) <= MaxResponseBytes) return json;
                if (entries != 0) entries /= 2;
                else if (textBytes != 0) textBytes /= 2;
                else throw new InvalidOperationException("Context identity exceeds the response byte budget.");
            }
        }

        internal static string Failure(string id, string code, string message)
        {
            var clipped = false;
            // Invalid overlong ids have no accepted correlation identity.
            if (id != null && id.Length > 128) id = null;
            return JsonUtility.ToJson(new CallFailure { id = id,
                error = new CallError { code = code, message = Text(message, 2048, ref clipped) } });
        }

        private static HostResult Project(HostResult source, int entries, int textBytes)
        {
            var incomplete = source.contextIncomplete;
            var selected = source.selection ?? new ObjectInfo[0];
            var selection = new ObjectInfo[Math.Min(entries, selected.Length)];
            if (selection.Length != source.selectionTotal) incomplete = true;
            for (var index = 0; index < selection.Length; index++)
                selection[index] = Object(selected[index], textBytes, ref incomplete);
            var result = new HostResult
            {
                unityVersion = source.unityVersion,
                sessionId = source.sessionId,
                scene = Text(source.scene, textBytes, ref incomplete),
                processId = source.processId,
                mainThreadId = source.mainThreadId,
                selection = selection,
                selectionTotal = source.selectionTotal,
                created = source.created == null ? null : Object(source.created, textBytes, ref incomplete),
                editorOwner = source.editorOwner,
                exitRequested = source.exitRequested
            };
            if (source.editorStatus != null)
            {
                result.editorStatus = Status(source.editorStatus, entries, textBytes);
                incomplete |= result.editorStatus.incomplete;
            }
            result.contextIncomplete = incomplete;
            return result;
        }

        private static ObjectInfo Object(ObjectInfo source, int textBytes, ref bool incomplete)
        {
            return new ObjectInfo { objectId = source.objectId, name = Text(source.name, textBytes, ref incomplete),
                x = source.x, y = source.y, z = source.z };
        }

        private static EditorStatus Status(EditorStatus source, int entries, int textBytes)
        {
            var incomplete = source.incomplete;
            var scenes = new DirtySceneInfo[Math.Min(entries, source.dirtyScenes.Length)];
            var assets = new DirtyAssetInfo[Math.Min(entries, source.dirtyPersistentAssets.Length)];
            var windows = new UnsavedWindowInfo[Math.Min(entries, source.unsavedWindows.Length)];
            incomplete |= scenes.Length != source.dirtyScenesTotal ||
                assets.Length != source.dirtyPersistentAssetsTotal || windows.Length != source.unsavedWindowsTotal;
            for (var index = 0; index < scenes.Length; index++)
            {
                var item = source.dirtyScenes[index];
                scenes[index] = new DirtySceneInfo { name = Text(item.name, textBytes, ref incomplete),
                    path = Text(item.path, textBytes, ref incomplete), handle = item.handle };
            }
            for (var index = 0; index < assets.Length; index++)
            {
                var item = source.dirtyPersistentAssets[index];
                assets[index] = new DirtyAssetInfo { instanceId = item.instanceId,
                    path = Text(item.path, textBytes, ref incomplete), type = Text(item.type, textBytes, ref incomplete),
                    name = Text(item.name, textBytes, ref incomplete) };
            }
            for (var index = 0; index < windows.Length; index++)
            {
                var item = source.unsavedWindows[index];
                windows[index] = new UnsavedWindowInfo { instanceId = item.instanceId,
                    type = Text(item.type, textBytes, ref incomplete) };
            }
            var stage = new PrefabStageInfo { isOpen = source.prefabStage.isOpen,
                assetPath = Text(source.prefabStage.assetPath, textBytes, ref incomplete),
                scenePath = Text(source.prefabStage.scenePath, textBytes, ref incomplete),
                rootInstanceId = source.prefabStage.rootInstanceId };
            return new EditorStatus
            {
                sampledAtUtc = source.sampledAtUtc,
                isCompiling = source.isCompiling,
                isUpdating = source.isUpdating,
                isPlayingOrWillChangePlaymode = source.isPlayingOrWillChangePlaymode,
                dirtyScenes = scenes, dirtyScenesTotal = source.dirtyScenesTotal,
                dirtyPersistentAssets = assets, dirtyPersistentAssetsTotal = source.dirtyPersistentAssetsTotal,
                unsavedWindows = windows, unsavedWindowsTotal = source.unsavedWindowsTotal,
                prefabStage = stage, incomplete = incomplete,
                editorOwner = source.editorOwner, sessionId = source.sessionId, mainThreadId = source.mainThreadId,
                firstResourceBlockerIsWindow = source.firstResourceBlockerIsWindow
            };
        }

        private static string Text(string value, int maxBytes, ref bool incomplete)
        {
            if (value == null || Encoding.UTF8.GetByteCount(value) <= maxBytes) return value;
            incomplete = true;
            var low = 0;
            var high = value.Length;
            while (low < high)
            {
                var middle = low + (high - low + 1) / 2;
                if (Encoding.UTF8.GetByteCount(value.Substring(0, middle)) <= maxBytes) low = middle;
                else high = middle - 1;
            }
            if (low > 0 && low < value.Length && char.IsHighSurrogate(value[low - 1]) && char.IsLowSurrogate(value[low]))
                low--;
            return value.Substring(0, low);
        }
    }
}
