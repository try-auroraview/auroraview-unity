"""Offline consumer contracts; no Editor, service or network is started."""

import json
import io
import subprocess
import sys
import unittest
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "agent"))
from auroraview_dcc_mcp import ClosedError, ContractError
from core import PipeTransport, SceneTools
import core


class Host:
    def __init__(self):
        self.session = "a" * 32
        self.calls = []
        self.created = []

    def __call__(self, request):
        self.calls.append(request)
        if request.get("sessionId", self.session) != self.session:
            return {
                "id": request["id"],
                "ok": False,
                "error": {"message": "Unity session expired"},
            }
        if request["method"] == "scene.create_cube":
            self.created.append(request["params"].get("name", "Cube"))
        return {
            "id": request["id"],
            "ok": True,
            "result": {
                "processId": 1234,
                "sessionId": self.session,
                "mainThreadId": 1,
                "unityVersion": "2022.3.test",
                "selection": [],
                "created": None,
            },
        }


class ConsumerTests(unittest.TestCase):
    def setUp(self):
        self.dns = patch(
            "socket.getaddrinfo",
            side_effect=AssertionError("Offline test attempted DNS"),
        )
        self.connect = patch(
            "socket.socket.connect",
            side_effect=AssertionError("Offline test attempted network"),
        )
        self.dns.start()
        self.connect.start()
        self.addCleanup(self.dns.stop)
        self.addCleanup(self.connect.stop)
        self.host = Host()
        self.tools = SceneTools(1234, self.host)
        self.addCleanup(self.tools.close)

    def test_exact_three_contracts_share_host_methods_and_session(self):
        names = [item["name"] for item in self.tools.owner.list_tools()]
        self.assertEqual(names, ["scene.context", "scene.create_cube", "scene.select"])
        self.tools.owner.call("scene.context")
        self.tools.owner.call("scene.create_cube", {"name": "Cube"})
        self.tools.owner.call("scene.select", {"objectId": 7})
        self.assertEqual([item["method"] for item in self.host.calls[1:]], names)
        self.assertTrue(
            all(item["sessionId"] == "a" * 32 for item in self.host.calls[1:])
        )

    def test_invalid_or_unregistered_arguments_never_reach_unity(self):
        for method, arguments in [
            ("execute_code", {}),
            ("scene.create_cube", {"name": ""}),
            ("scene.create_cube", {"script": "bad"}),
            ("scene.select", {"objectId": "7"}),
        ]:
            with (
                self.subTest(method=method, arguments=arguments),
                self.assertRaises(ContractError),
            ):
                self.tools.owner.call(method, arguments)
        self.assertEqual(len(self.host.calls), 1)

    def test_reloaded_session_refuses_mutation_before_host_execution(self):
        self.host.session = "b" * 32
        with self.assertRaisesRegex(ContractError, "session expired"):
            self.tools.owner.call("scene.create_cube", {"name": "Stale"})
        self.assertEqual(self.host.created, [])

    def test_closed_owner_does_not_dispatch(self):
        self.tools.close()
        with self.assertRaises(ClosedError):
            self.tools.owner.call("scene.context")
        self.assertEqual(len(self.host.calls), 1)

    def test_missing_session_and_foreign_process_are_rejected(self):
        for change in ({"sessionId": None}, {"processId": 55}):

            def transport(request):
                response = self.host(request)
                response["result"].update(change)
                return response

            with self.subTest(change=change), self.assertRaises(ContractError):
                SceneTools(1234, transport)

    def test_mismatched_response_id_is_rejected(self):
        def transport(request):
            response = self.host(request)
            response["id"] = "foreign"
            return response

        with self.assertRaisesRegex(ContractError, "match the request"):
            SceneTools(1234, transport)

    def test_pid_is_explicit_positive_integer(self):
        for pid in (None, True, 0, -1, "1234"):
            with self.subTest(pid=pid), self.assertRaises(ContractError):
                SceneTools(pid, self.host)


class EditorExitTests(unittest.TestCase):
    def setUp(self):
        self.host = Host()
        self.owner = {
            "processId": 1234,
            "processCreationFileTime": "134000000000000000",
            "projectPath": str(Path.cwd()),
            "runId": "a" * 32,
        }

    def transport(self, request):
        response = self.host(request)
        response["result"]["editorOwner"] = dict(self.owner)
        response["result"]["exitRequested"] = request["method"] == "editor.exit"
        return response

    def test_exit_is_opt_in_and_bound_to_owner_session(self):
        tools = SceneTools(1234, self.transport, editor_owner=dict(self.owner))
        self.addCleanup(tools.close)
        self.assertEqual(tools.owner.list_tools()[-1]["name"], "editor.exit")
        result = tools.owner.call("editor.exit")
        self.assertTrue(result["exitRequested"])
        self.assertEqual(self.host.calls[-1]["params"], {"owner": self.owner})
        self.assertEqual(self.host.calls[-1]["sessionId"], self.host.session)
        with self.assertRaises(ContractError):
            tools.owner.call("editor.exit", {"owner": self.owner})

    def test_foreign_or_incomplete_launch_identity_cannot_attach_exit(self):
        for change in (
            {"processId": True},
            {"processCreationFileTime": 134000000000000000},
            {"processCreationFileTime": "1e17"},
            {"projectPath": "relative"},
            {"runId": None},
            {"runId": "b" * 32},
            {"extra": "script"},
        ):
            with self.subTest(change=change), self.assertRaises(ContractError):
                SceneTools(1234, self.transport, editor_owner={**self.owner, **change})

    def test_ordinary_owner_does_not_expose_exit(self):
        tools = SceneTools(1234, self.transport)
        self.addCleanup(tools.close)
        with self.assertRaises(ContractError):
            tools.owner.call("editor.exit")
        self.assertEqual(len(self.host.calls), 1)

    def test_one_shot_cli_calls_only_the_owned_exit_and_closes_owner(self):
        with (
            patch("core.argparse.ArgumentParser.parse_args") as args,
            patch("core.SceneTools") as tools,
            patch("sys.stdout", new_callable=io.StringIO),
        ):
            args.return_value.owner_file.open.return_value.__enter__.return_value.read.return_value = json.dumps(
                self.owner
            ).encode()
            args.return_value.node = "owned-node"
            tools.return_value.owner.call.return_value = {"exitRequested": True}
            core.main()
        tools.assert_called_once_with(1234, node="owned-node", editor_owner=self.owner)
        tools.return_value.owner.call.assert_called_once_with("editor.exit")
        tools.return_value.close.assert_called_once_with()

    def test_one_shot_cli_closes_owner_when_exit_is_refused(self):
        with (
            patch("core.argparse.ArgumentParser.parse_args") as args,
            patch("core.SceneTools") as tools,
        ):
            args.return_value.owner_file.open.return_value.__enter__.return_value.read.return_value = json.dumps(
                self.owner
            ).encode()
            tools.return_value.owner.call.side_effect = ContractError("unsaved work")
            with self.assertRaisesRegex(ContractError, "unsaved work"):
                core.main()
        tools.return_value.close.assert_called_once_with()

    def test_one_shot_cli_rejects_unbounded_or_nonobject_owner_before_probe(self):
        for data in (b"x" * 65537, b"[]"):
            with (
                self.subTest(size=len(data)),
                patch("core.argparse.ArgumentParser.parse_args") as args,
                patch("core.SceneTools") as tools,
                patch("sys.stderr", new_callable=io.StringIO),
            ):
                args.return_value.owner_file.open.return_value.__enter__.return_value.read.return_value = data
                with self.assertRaises(SystemExit):
                    core.main()
            tools.assert_not_called()


class TransportTests(unittest.TestCase):
    def test_timeout_stops_owned_one_shot_client(self):
        with patch(
            "core.subprocess.run", side_effect=subprocess.TimeoutExpired("node", 14)
        ) as run:
            with self.assertRaisesRegex(ContractError, "timed out"):
                PipeTransport(1234)({"type": "call"})
        self.assertEqual(run.call_args.kwargs["timeout"], 14)
        self.assertTrue(run.call_args.kwargs["check"])

    def test_fixed_bridge_and_utf8_json(self):
        response = {"id": "one", "ok": True}
        with patch(
            "core.subprocess.run",
            return_value=subprocess.CompletedProcess([], 0, json.dumps(response), ""),
        ) as run:
            self.assertEqual(
                PipeTransport(1234, "verified-node.exe")({"name": "立方体"}), response
            )
        command = run.call_args.args[0]
        self.assertEqual(command[0], "verified-node.exe")
        self.assertTrue(command[1].endswith("pipe-call.mjs"))
        self.assertEqual(command[2:], ["--pid", "1234"])
        self.assertIn("立方体", run.call_args.kwargs["input"])

    def test_oversize_request_is_rejected_before_process_creation(self):
        with (
            patch("core.subprocess.run") as run,
            self.assertRaisesRegex(ContractError, "64 KiB"),
        ):
            PipeTransport(1234)({"value": "立" * 30000})
        run.assert_not_called()

    def test_bad_or_oversize_response_is_rejected(self):
        for data in ("invalid", "x" * 65537):
            with patch(
                "core.subprocess.run",
                return_value=subprocess.CompletedProcess([], 0, data, ""),
            ):
                with (
                    self.subTest(data_length=len(data)),
                    self.assertRaises(ContractError),
                ):
                    PipeTransport(1234)({})


if __name__ == "__main__":
    unittest.main()
