using System;
using System.IO;
using AuroraView.Unity;
using DccMcp.Unity;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

public static class DccMcpSceneTools
{
    [MenuItem("Window/AuroraView/DCC-MCP Scene Tools")]
    public static void Open()
    {
        var package = PackageInfo.FindForAssembly(typeof(AuroraViewWindow).Assembly);
        AuroraViewWindow.Open("DCC-MCP Scene Tools",
            Path.Combine(package.resolvedPath, "Editor/WebAssets/dcc-mcp.html"), Dispatch);
    }

    public static string Dispatch(string json)
    {
        JToken id = null;
        try
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            var request = JObject.Parse(json);
            id = request["id"];
            if (json.Length > 16384) throw new ArgumentException("Call exceeds the panel request limit.");
            if ((string)request["type"] != "call" || id?.Type != JTokenType.String || string.IsNullOrEmpty((string)id))
                throw new ArgumentException("Expected an AuroraView call with a string id.");
            var method = (string)request["method"];
            switch (method)
            {
                case "project.inspect":
                case "editor.read_console":
                case "editor.inspect_dirty_assets":
                case "scene.create_game_object":
                case "scene.inspect_game_object":
                    break;
                default: throw new ArgumentException("This panel does not expose the requested DCC-MCP command.");
            }
            var parameters = request["params"];
            if (parameters != null && parameters.Type != JTokenType.Object)
                throw new ArgumentException("params must be an object.");
            var result = DccMcpCommands.Execute(method, (JObject)parameters ?? new JObject());
            return new JObject { ["id"] = id, ["ok"] = true, ["result"] = result }.ToString(Formatting.None);
        }
        catch (Exception exception)
        {
            return new JObject { ["id"] = id, ["ok"] = false,
                ["error"] = new JObject { ["code"] = "dcc_mcp_error", ["message"] = exception.Message } }.ToString(Formatting.None);
        }
    }
}
