"""Explicitly owned Core service sample; no Python is loaded inside Unity."""

import argparse
import json
import signal
import threading
from pathlib import Path

from core import SceneTools
from dcc_mcp_core import DccServerOptions, HostExecutionBridge
from dcc_mcp_core.host import QueueDispatcher, StandaloneHost
from dcc_mcp_core.server_base import DccServerBase


class InlineDispatcher:
    """Core's HTTP queue already selected the external Python execution lane."""

    def dispatch_callable(self, func, *args, **kwargs):
        for key in (
            "affinity",
            "context",
            "action_name",
            "skill_name",
            "execution",
            "timeout_hint_secs",
        ):
            kwargs.pop(key, None)
        return func(*args, **kwargs)


class CoreService:
    """Own only the explicitly created Python service and Core queue driver."""

    def __init__(
        self,
        pid,
        state_dir,
        *,
        transport=None,
        node="node",
        gateway_port=None,
        ui_control=None,
        skill_root=None,
    ):
        self.pid = pid
        self.state_dir = Path(state_dir).resolve()
        self.transport = transport
        self.node = node
        self.gateway_port = gateway_port
        self.ui_control = ui_control
        self.skill_root = Path(skill_root).resolve() if skill_root is not None else None
        self.queue = QueueDispatcher()
        self.driver = StandaloneHost(self.queue, thread_name="auroraview-unity-core")
        self.server = None
        self.tools = None
        self.binding = None
        self.handle = None
        self._resources_closed = False

    def start(self):
        self.state_dir.mkdir(parents=True, exist_ok=True)
        self.driver.start()
        try:
            self.queue.post(self._start).wait(timeout=25)
        except BaseException:
            self.close()
            raise
        return self

    def _start(self):
        skills = self.skill_root
        if skills is None:
            skills = self.state_dir / "skills"
            skills.mkdir(exist_ok=True)
        elif not skills.is_dir():
            raise ValueError("The owner-selected skill root must exist")
        self.tools = SceneTools(self.pid, self.transport, node=self.node)
        # Runtime selection and authority belong to trusted bootstrap. Omitting
        # this keyword preserves the published Core 0.20.41 constructor path.
        options = {} if self.ui_control is None else {"ui_control": self.ui_control}
        self.server = DccServerBase(
            DccServerOptions.from_env(
                "unity",
                skills,
                port=0,
                server_name="auroraview-unity-core",
                dcc_pid=self.pid,
                dcc_version=self.tools.context.get("unityVersion"),
                registry_dir=str(self.state_dir / "registry"),
                gateway_port=self.gateway_port,
                enable_file_logging=False,
                enable_job_persistence=False,
                enable_telemetry=False,
                enable_checkpoint_persistence=False,
                execution_bridge=HostExecutionBridge(
                    dispatcher=InlineDispatcher(),
                    host_dispatcher=self.queue,
                    default_thread_affinity="main",
                    default_timeout_hint_secs=20,
                ),
                **options,
            )
        )
        self.binding = self.tools.attach(self.server)
        self.handle = self.server.start(install_atexit_hook=False)

    def close(self):
        if not self._resources_closed:
            if self.driver.is_running:
                # Keep this execution lane alive if owned cleanup fails. The
                # owner retains its resources and close() can retry here.
                self.queue.post(self._close).wait(timeout=25)
            elif self.tools is not None or self.server is not None:
                raise RuntimeError("Core cleanup requires its original execution lane")
            self._resources_closed = True
        self.driver.stop(timeout=25)

    def _close(self):
        try:
            if self.tools is not None:
                self.tools.close()
        finally:
            if self.server is not None:
                self.server.stop()

    def __enter__(self):
        return self.start()

    def __exit__(self, *args):
        self.close()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--pid", type=int, required=True)
    parser.add_argument("--state-dir", type=Path, required=True)
    parser.add_argument("--node", default="node")
    args = parser.parse_args()
    stopped = threading.Event()
    signal.signal(signal.SIGINT, lambda *_: stopped.set())
    signal.signal(signal.SIGTERM, lambda *_: stopped.set())
    with CoreService(args.pid, args.state_dir, node=args.node) as service:
        print(
            json.dumps(
                {
                    "pid": args.pid,
                    "sessionId": service.tools.session_id,
                    "mcp_url": service.handle.mcp_url(),
                    "tools": service.binding.method_names,
                }
            ),
            flush=True,
        )
        stopped.wait()


if __name__ == "__main__":
    main()
