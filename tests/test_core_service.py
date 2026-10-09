"""Owned bootstrap options only; no service, native task or Editor is started."""

import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import Mock, patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "agent"))
from core_server import CoreService
from dcc_mcp_core import DccServerOptions


class CoreServiceOptionsTests(unittest.TestCase):
    def prepare(self, state, **kwargs):
        queue = patch("core_server.QueueDispatcher", return_value=Mock())
        driver = patch("core_server.StandaloneHost", return_value=Mock())
        tools = patch(
            "core_server.SceneTools",
            return_value=Mock(context={"unityVersion": "test"}),
        )
        server = patch("core_server.DccServerBase")
        for item in (queue, driver, tools, server):
            item.start()
            self.addCleanup(item.stop)
        return CoreService(1234, state, **kwargs)

    def test_default_none_omits_new_keyword_for_published_core(self):
        for kwargs in ({}, {"ui_control": None}):
            with self.subTest(kwargs=kwargs), tempfile.TemporaryDirectory() as state:
                service = self.prepare(state, **kwargs)
                with patch(
                    "core_server.DccServerOptions.from_env",
                    wraps=DccServerOptions.from_env,
                ) as from_env:
                    service._start()
                self.assertNotIn("ui_control", from_env.call_args.kwargs)
                self.assertIsNone(service.ui_control)
                self.assertEqual(
                    from_env.call_args.args[1], Path(state).resolve() / "skills"
                )
                service.driver.start.assert_not_called()

    def test_explicit_runtime_options_forward_identity_and_existing_scope(self):
        runtime_options = object()
        transport = object()
        with tempfile.TemporaryDirectory() as state:
            service = self.prepare(
                state,
                ui_control=runtime_options,
                transport=transport,
                node="owner-selected-node",
                gateway_port=0,
            )
            with patch("core_server.DccServerOptions.from_env") as from_env:
                service._start()
            options = from_env.call_args.kwargs
            self.assertIs(options["ui_control"], runtime_options)
            self.assertEqual(options["dcc_pid"], 1234)
            self.assertEqual(options["gateway_port"], 0)
            self.assertEqual(
                Path(options["registry_dir"]), Path(state).resolve() / "registry"
            )
            self.assertFalse(options["enable_telemetry"])
            self.assertIs(service.transport, transport)
            self.assertEqual(service.node, "owner-selected-node")
            service.driver.start.assert_not_called()

    def test_owner_selected_skill_root_uses_core_builtin_discovery_argument(self):
        with tempfile.TemporaryDirectory() as state:
            root = Path(state) / "canonical-skills"
            root.mkdir()
            service = self.prepare(state, skill_root=root)
            with patch("core_server.DccServerOptions.from_env") as from_env:
                service._start()
            self.assertEqual(from_env.call_args.args[1], root.resolve())
            self.assertFalse((Path(state) / "skills").exists())

    def test_missing_owner_skill_root_is_rejected(self):
        with tempfile.TemporaryDirectory() as state:
            service = self.prepare(state, skill_root=Path(state) / "missing")
            with patch("core_server.DccServerOptions.from_env") as from_env:
                with self.assertRaisesRegex(ValueError, "skill root must exist"):
                    service._start()
            from_env.assert_not_called()

    def test_editor_exit_owner_is_forwarded_only_by_bootstrap(self):
        owner = {"runId": "a" * 32}
        with tempfile.TemporaryDirectory() as state:
            service = self.prepare(state, editor_owner=owner)
            with patch("core_server.SceneTools") as tools:
                service._start()
            self.assertIs(tools.call_args.kwargs["editor_owner"], owner)
            service.driver.start.assert_not_called()


if __name__ == "__main__":
    unittest.main()
