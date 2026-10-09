"""Real Editor Core acceptance; run only with the isolated AgentAcceptance host."""

import argparse
import importlib.metadata
import json
from pathlib import Path
from urllib.parse import urlparse
from urllib.request import ProxyHandler, Request, build_opener

from core_server import CoreService


def rpc(opener, url, method, params=None):
    parsed = urlparse(url)
    if parsed.scheme != "http" or parsed.hostname != "127.0.0.1":
        raise RuntimeError("Acceptance requires a numeric-loopback Core endpoint")
    request = Request(
        url,
        data=json.dumps(
            {"jsonrpc": "2.0", "id": 1, "method": method, "params": params or {}}
        ).encode(),
        headers={
            "Content-Type": "application/json",
            "Accept": "application/json, text/event-stream",
        },
    )
    with opener.open(request, timeout=25) as response:
        text = response.read().decode()
        if response.headers.get("Content-Type", "").startswith("text/event-stream"):
            text = next(
                line[6:] for line in text.splitlines() if line.startswith("data: ")
            )
        result = json.loads(text)
    if "error" in result:
        raise RuntimeError(str(result["error"]))
    return result["result"]


def value(result):
    if result.get("isError", False):
        raise RuntimeError(str(result))
    if "structuredContent" in result:
        return result["structuredContent"]
    return json.loads(result["content"][0]["text"])


def accept(pid, state_dir, output, node):
    opener = build_opener(ProxyHandler({}))
    with CoreService(pid, state_dir, node=node, gateway_port=0) as service:
        url = service.handle.mcp_url()
        initialized = rpc(
            opener,
            url,
            "initialize",
            {
                "protocolVersion": "2025-03-26",
                "capabilities": {},
                "clientInfo": {
                    "name": "auroraview-unity-acceptance",
                    "version": "0.1.0",
                },
            },
        )
        if "serverInfo" not in initialized:
            raise RuntimeError("Core MCP initialization failed")
        names, cursor = [], None
        for _ in range(100):
            result = rpc(
                opener, url, "tools/list", {"cursor": cursor} if cursor else {}
            )
            names.extend(item["name"] for item in result["tools"])
            cursor = result.get("nextCursor")
            if not cursor:
                break
        if not set(service.binding.tool_names).issubset(names):
            raise RuntimeError("Core did not discover the Unity scene tools")

        def call(method, params=None):
            return value(
                rpc(
                    opener,
                    url,
                    "tools/call",
                    {
                        "name": service.binding.method_names[method],
                        "arguments": {"params": params or {}},
                    },
                )
            )

        context = call("scene.context")
        if (
            context["processId"] != pid
            or context["sessionId"] != service.tools.session_id
        ):
            raise RuntimeError("Core context is not the bound Editor session")
        created = call("scene.create_cube", {"name": "AuroraView MCP 验收立方体"})
        object_id = created["created"]["objectId"]
        selected = call("scene.select", {"objectId": object_id})
        readback = call("scene.context")
        for result in (selected, readback):
            if not any(
                item["objectId"] == object_id
                and item["name"] == "AuroraView MCP 验收立方体"
                for item in result["selection"]
            ):
                raise RuntimeError("Unity selection readback failed")
        versions = {
            name: importlib.metadata.version(name)
            for name in (
                "auroraview-dcc-mcp",
                "dcc-mcp-core",
                "dcc-mcp-server",
                "jsonschema",
            )
        }
        receipt = {
            "processId": pid,
            "mainThreadId": context["mainThreadId"],
            "unityVersion": context["unityVersion"],
            "objectId": object_id,
            "mcpContext": True,
            "mcpCreate": True,
            "mcpSelect": True,
            "selectionReadback": True,
        }
        consumer = {
            "versions": versions,
            "sessionId": service.tools.session_id,
            "methods": service.binding.method_names,
            "mcpDiscovery": True,
            "sceneReadback": True,
        }
    consumer.update(
        {
            "ownedServerStopped": not service.server.is_running,
            "ownedDriverStopped": not service.driver.is_running,
        }
    )
    output.parent.mkdir(parents=True, exist_ok=True)
    output.with_name("core-consumer.json").write_text(
        json.dumps(consumer, indent=2), encoding="utf-8"
    )
    # Publish last: the isolated Unity harness now verifies native state, Undo and endpoint stop.
    pending = output.with_suffix(".pending")
    pending.write_text(json.dumps(receipt, indent=2), encoding="utf-8")
    pending.replace(output)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--pid", type=int, required=True)
    parser.add_argument("--state-dir", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--node", default="node")
    args = parser.parse_args()
    accept(args.pid, args.state_dir, args.output, args.node)


if __name__ == "__main__":
    main()
