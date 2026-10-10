"""Borrow a DCC-MCP service for the existing Unity scene contracts."""

import argparse
import contextlib
import functools
import json
import re
import subprocess
import uuid
from pathlib import Path

from auroraview_dcc_mcp import ContractError, Tool, ToolSet


class PipeTransport:
    """Reuse the preview's bounded pipe client without starting its MCP server."""

    def __init__(self, pid, node="node"):
        self.pid = pid
        self.node = node

    def __call__(self, request):
        data = json.dumps(request, ensure_ascii=False, allow_nan=False)
        if len(data.encode("utf-8")) > 65536:
            raise ContractError("Unity request exceeds 64 KiB")
        try:
            result = subprocess.run(
                [
                    self.node,
                    str(Path(__file__).with_name("pipe-call.mjs")),
                    "--pid",
                    str(self.pid),
                ],
                input=data,
                capture_output=True,
                encoding="utf-8",
                timeout=14,
                check=True,
                creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
            )
        except subprocess.TimeoutExpired as error:
            # subprocess.run kills and waits for its owned child on timeout.
            raise ContractError(
                "Unity pipe call timed out; its client was stopped"
            ) from error
        except (OSError, subprocess.CalledProcessError) as error:
            raise ContractError("Unity endpoint is unavailable") from error
        if len(result.stdout.encode("utf-8")) > 65536:
            raise ContractError("Unity response exceeds 64 KiB")
        try:
            return json.loads(result.stdout)
        except ValueError as error:
            raise ContractError("Unity returned invalid JSON") from error


class SceneTools:
    """One session-bound tool owner; it never starts or stops the borrowed server.

    Construct and attach on the borrowed service's Core execution lane. Unity
    work remains on EditorApplication.update through the current-user pipe.
    """

    def __init__(self, pid, transport=None, *, node="node", editor_owner=None):
        if type(pid) is not int or pid < 1:
            raise ContractError("An explicit positive Unity Editor PID is required")
        self.pid = pid
        self.transport = (
            transport if transport is not None else PipeTransport(pid, node)
        )
        self.context = self._request("scene.context", {})
        self.session_id = self.context.get("sessionId")
        if not isinstance(self.session_id, str) or not re.fullmatch(
            r"[a-f0-9]{32}", self.session_id
        ):
            raise ContractError(
                "Unity endpoint lacks a supported session identity; update the package"
            )
        tools = [
            Tool(
                "scene.context",
                "Read the bound Unity scene, selection and main-thread identity",
                {"type": "object", "properties": {}, "additionalProperties": False},
                functools.partial(self._call, "scene.context"),
                read_only=True,
                destructive=False,
                idempotent=True,
            ),
            Tool(
                "scene.create_cube",
                "Create and select a Unity cube with Undo; refuses Play mode",
                {
                    "type": "object",
                    "properties": {
                        "name": {"type": "string", "minLength": 1, "maxLength": 80}
                    },
                    "additionalProperties": False,
                },
                functools.partial(self._call, "scene.create_cube"),
                destructive=False,
            ),
            Tool(
                "scene.select",
                "Select a live scene GameObject returned by the bound Unity session",
                {
                    "type": "object",
                    "properties": {"objectId": {"type": "integer"}},
                    "required": ["objectId"],
                    "additionalProperties": False,
                },
                functools.partial(self._call, "scene.select"),
                destructive=False,
                idempotent=True,
            ),
        ]
        if editor_owner is not None:
            if (
                not isinstance(editor_owner, dict)
                or set(editor_owner)
                != {"processId", "processCreationFileTime", "projectPath", "runId"}
                or type(editor_owner["processId"]) is not int
                or editor_owner["processId"] != pid
                or not isinstance(editor_owner["processCreationFileTime"], str)
                or not re.fullmatch(
                    r"[1-9][0-9]*", editor_owner["processCreationFileTime"]
                )
                or not isinstance(editor_owner["projectPath"], str)
                or not Path(editor_owner["projectPath"]).is_absolute()
                or not isinstance(editor_owner["runId"], str)
                or not re.fullmatch(r"[a-f0-9]{32}", editor_owner["runId"])
                or editor_owner != self.context.get("editorOwner")
            ):
                raise ContractError("Exact owned test Editor launch identity required")
            tools.append(
                Tool(
                    "editor.exit",
                    "Request exit of this owned test Editor only; refuses unsaved work. ACK is not process exit.",
                    {"type": "object", "properties": {}, "additionalProperties": False},
                    functools.partial(
                        self._call, "editor.exit", owner=dict(editor_owner)
                    ),
                    destructive=True,
                )
            )
        self.owner = ToolSet(
            "auroraview-unity-scene",
            tools,
            description="Explicit AuroraView Unity scene contracts over the host's current-user pipe",
            dcc="unity",
        )

    def _request(self, method, params):
        request = {
            "type": "call",
            "id": uuid.uuid4().hex,
            "method": method,
            "params": params,
        }
        if hasattr(self, "session_id"):
            request["sessionId"] = self.session_id
        response = self.transport(request)
        if not isinstance(response, dict) or response.get("id") != request["id"]:
            raise ContractError("Unity response does not match the request")
        if response.get("ok") is not True:
            error = response.get("error", {})
            if not isinstance(error, dict):
                raise ContractError("Unity returned an invalid error envelope")
            raise ContractError(
                str(error.get("message", "Unity refused the scene call"))
            )
        result = response.get("result")
        if not isinstance(result, dict) or result.get("processId") != self.pid:
            raise ContractError("Unity response belongs to a different process")
        if type(result.get("mainThreadId")) is not int or result["mainThreadId"] < 1:
            raise ContractError("Unity response lacks its main-thread identity")
        if (
            not isinstance(result.get("unityVersion"), str)
            or not result["unityVersion"]
        ):
            raise ContractError("Unity response lacks its Editor version")
        if hasattr(self, "session_id") and (
            result.get("sessionId") != self.session_id
            or result["mainThreadId"] != self.context["mainThreadId"]
            or result.get("unityVersion") != self.context.get("unityVersion")
        ):
            raise ContractError(
                "Unity session expired; explicitly attach a new tool owner"
            )
        return result

    def _call(self, method, **params):
        return self._request(method, params)

    def attach(self, server):
        """Return a borrowed binding; caller retains and closes this owner."""
        return self.owner.attach(server)

    def close(self):
        self.owner.close()


def main():
    parser = argparse.ArgumentParser(
        description="Request exit of one explicitly owned test Editor."
    )
    parser.add_argument("--owner-file", type=Path, required=True)
    parser.add_argument("--node", default="node")
    args = parser.parse_args()
    with args.owner_file.open("rb") as stream:
        data = stream.read(65537)
    if len(data) > 65536:
        parser.error("Editor owner file exceeds 64 KiB")
    owner = json.loads(data.decode("utf-8-sig"))
    if not isinstance(owner, dict):
        parser.error("Editor owner must be an object")
    with contextlib.closing(
        SceneTools(owner.get("processId"), node=args.node, editor_owner=owner)
    ) as tools:
        print(json.dumps(tools.owner.call("editor.exit")))


if __name__ == "__main__":
    main()
