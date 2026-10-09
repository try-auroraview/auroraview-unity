"""Explicit numeric-loopback Core smoke with a fake Unity transport."""

import json
import sys
import tempfile
import threading
import unittest
from pathlib import Path
from urllib.request import ProxyHandler, Request, build_opener

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "agent"))
from auroraview_dcc_mcp import CleanupError
from core_server import CoreService
from test_core import Host


class CoreHttpTests(unittest.TestCase):
    def test_owned_cleanup_failure_retains_lane_for_retry(self):
        for component, method in (("tools", "close"), ("server", "stop")):
            with self.subTest(component=component), tempfile.TemporaryDirectory(
                prefix="unity-core-retry-"
            ) as state:
                service = CoreService(1234, state, transport=Host(), gateway_port=0)
                service.start()
                target = getattr(service, component)
                close = getattr(target, method)
                attempts = []

                def fail_once():
                    attempts.append(True)
                    if len(attempts) == 1:
                        raise RuntimeError("Injected cleanup failure")
                    close()

                setattr(target, method, fail_once)
                try:
                    with self.assertRaisesRegex(
                        RuntimeError, "Injected cleanup failure"
                    ):
                        service.close()
                    self.assertTrue(service.driver.is_running)
                    self.assertEqual(service.server.is_running, component == "server")
                    service.close()
                    self.assertTrue(service.tools.owner.closed)
                    self.assertFalse(service.server.is_running)
                    self.assertFalse(service.driver.is_running)
                finally:
                    service.close()

    def test_published_core_discovery_call_and_borrowed_cleanup(self):
        from dcc_mcp_core import __version__

        requirements = (
            Path(__file__).resolve().parents[1] / "agent" / "requirements-core.in"
        )
        expected = next(
            line.partition("==")[2].strip()
            for line in requirements.read_text(encoding="utf-8-sig").splitlines()
            if line.startswith("dcc-mcp-core==")
        )
        self.assertEqual(__version__, expected)
        host = Host()
        caller_threads = []

        def transport(request):
            caller_threads.append(threading.get_ident())
            return host(request)

        opener = build_opener(ProxyHandler({}))

        def rpc(url, method, params=None):
            request = Request(
                url,
                data=json.dumps(
                    {
                        "jsonrpc": "2.0",
                        "id": 1,
                        "method": method,
                        "params": params or {},
                    }
                ).encode(),
                headers={
                    "Content-Type": "application/json",
                    "Accept": "application/json, text/event-stream",
                },
            )
            with opener.open(request, timeout=25) as response:
                body = response.read().decode()
                if response.headers.get("Content-Type", "").startswith(
                    "text/event-stream"
                ):
                    body = next(
                        line[6:]
                        for line in body.splitlines()
                        if line.startswith("data: ")
                    )
                return json.loads(body)

        with tempfile.TemporaryDirectory(prefix="unity-core-test-") as state:
            service = CoreService(1234, state, transport=transport, gateway_port=0)
            try:
                service.start()
                url = service.handle.mcp_url()
                self.assertTrue(url.startswith("http://127.0.0.1:"), url)
                names = []
                cursor = None
                for _ in range(100):
                    result = rpc(
                        url, "tools/list", {"cursor": cursor} if cursor else {}
                    )["result"]
                    names.extend(item["name"] for item in result["tools"])
                    cursor = result.get("nextCursor")
                    if not cursor:
                        break
                self.assertTrue(set(service.binding.tool_names).issubset(names))
                initialized = rpc(
                    url,
                    "initialize",
                    {
                        "protocolVersion": "2025-03-26",
                        "capabilities": {},
                        "clientInfo": {"name": "unity-core-test", "version": "1"},
                    },
                )
                self.assertIn("serverInfo", initialized["result"])
                method = service.binding.method_names["scene.create_cube"]
                response = rpc(
                    url,
                    "tools/call",
                    {"name": method, "arguments": {"params": {"name": "Core Cube"}}},
                )
                self.assertNotIn("error", response, response)
                self.assertFalse(response["result"].get("isError", False), response)
                self.assertEqual(host.created, ["Core Cube"])
                self.assertEqual(len(set(caller_threads)), 1)
                self.assertNotEqual(caller_threads[0], threading.get_ident())
                # Failed borrowed cleanup retains the owner for retry and
                # revokes calls without stopping its service.
                unload = service.server.unload_skill
                service.server.unload_skill = lambda *_: False
                try:
                    with self.assertRaises(CleanupError):
                        service.queue.post(service.tools.close).wait(timeout=5)
                finally:
                    service.server.unload_skill = unload
                self.assertTrue(service.server.is_running)
                stale = rpc(
                    url,
                    "tools/call",
                    {"name": method, "arguments": {"params": {"name": "Stale"}}},
                )
                self.assertTrue(
                    "error" in stale or stale["result"].get("isError", False), stale
                )
                self.assertEqual(host.created, ["Core Cube"])
                # Retrying releases only the borrowed binding's actions.
                service.queue.post(service.tools.close).wait(timeout=5)
                self.assertTrue(service.server.is_running)
                stale = rpc(
                    url,
                    "tools/call",
                    {"name": method, "arguments": {"params": {"name": "Stale"}}},
                )
                self.assertTrue(
                    "error" in stale or stale["result"].get("isError", False), stale
                )
                self.assertEqual(host.created, ["Core Cube"])
            finally:
                service.close()
            self.assertFalse(service.driver.is_running)
            self.assertFalse(service.server.is_running)


if __name__ == "__main__":
    unittest.main()
