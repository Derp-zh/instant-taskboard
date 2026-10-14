"""Bounded stdio JSON RPC. Observation mode cannot change any thread."""
import json
import queue
import subprocess
import threading
import time

READ_METHODS = {"initialize", "thread/list", "thread/read", "thread/loaded/list",
                "thread/turns/list", "thread/items/list", "model/list"}
OWNED_METHODS = {"thread/start", "turn/start", "turn/interrupt", "thread/unsubscribe"}


class RpcError(RuntimeError):
    pass


class CodexRpc:
    def __init__(self, executable: str, arguments: list[str], owned: bool = False):
        self.owned = owned
        self.owned_threads: set[str] = set()
        self.pending_server: dict[str, dict] = {}
        self.messages: queue.Queue = queue.Queue()
        self.events: list[dict] = []
        self.responses: dict[str, dict] = {}
        self.stderr_lines: list[str] = []
        self.next_id = 0
        self.closed = False
        self.process = subprocess.Popen([executable, *arguments], stdin=subprocess.PIPE,
                                        stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                        text=True, encoding="utf-8", errors="replace",
                                        creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
        self.readers = [threading.Thread(target=self._read, daemon=True),
                        threading.Thread(target=self._errors, daemon=True)]
        for reader in self.readers:
            reader.start()

    def _read(self):
        try:
            for line in self.process.stdout:
                try:
                    self.messages.put(json.loads(line))
                except json.JSONDecodeError:
                    self.messages.put({"probeError": "non_json_stdout"})
        finally:
            self.messages.put({"probeError": "transport_closed"})

    def _errors(self):
        for line in self.process.stderr:
            self.stderr_lines.append(line.rstrip())
            self.stderr_lines = self.stderr_lines[-30:]

    def _send(self, message: dict):
        if self.closed or self.process.poll() is not None:
            raise RpcError("transport_closed")
        try:
            self.process.stdin.write(json.dumps(message, ensure_ascii=False) + "\n")
            self.process.stdin.flush()
        except (BrokenPipeError, OSError) as error:
            raise RpcError("transport_write_failed") from error

    def pump(self, timeout: float = 0.1):
        try:
            message = self.messages.get(timeout=timeout)
        except queue.Empty:
            return None
        if "probeError" in message:
            raise RpcError(message["probeError"])
        if "method" in message:
            self.events.append(message)
            if "id" in message:
                self.pending_server[str(message["id"])] = message
        elif "id" in message:
            self.responses[str(message["id"])] = message
        return message

    def call(self, method: str, params: dict | None = None, timeout: float = 15):
        if method not in READ_METHODS:
            if not self.owned or method not in OWNED_METHODS:
                raise RpcError("mutation_not_allowed")
            if method != "thread/start" and (params or {}).get("threadId") not in self.owned_threads:
                raise RpcError("thread_not_owned")
        self.next_id += 1
        request_id = self.next_id
        message = {"id": request_id, "method": method}
        if params is not None:
            message["params"] = params
        self._send(message)
        deadline = time.monotonic() + timeout
        while str(request_id) not in self.responses:
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                raise RpcError("request_timeout:" + method)
            self.pump(min(remaining, 0.2))
        response = self.responses.pop(str(request_id))
        if "error" in response:
            error = response["error"]
            # Do not expose arbitrary returned messages, paths, or conversation text.
            raise RpcError("rpc_error:" + method + ":" + str(error.get("code")))
        result = response.get("result") or {}
        if method == "thread/start":
            self.owned_threads.add(result["thread"]["id"])
        return result

    def initialize(self):
        result = self.call("initialize", {"clientInfo": {"name": "taskboard_probe",
                            "title": "Taskboard isolated feasibility probe", "version": "0.1.0"},
                            "capabilities": {"experimentalApi": True}})
        self._send({"method": "initialized"})
        return result

    def answer_owned(self, request: dict, result: dict):
        params = request.get("params") or {}
        if not self.owned or params.get("threadId") not in self.owned_threads:
            raise RpcError("cannot_answer_existing_thread")
        if self.pending_server.pop(str(request["id"]), None) is None:
            raise RpcError("server_request_not_pending")
        self._send({"id": request["id"], "result": result})

    def close(self):
        if self.closed:
            return
        self.closed = True
        # This process belongs to the probe; never terminate any existing Codex process.
        try:
            self.process.stdin.close()
            self.process.wait(timeout=3)
        except (OSError, subprocess.TimeoutExpired):
            self.process.terminate()
            try:
                self.process.wait(timeout=3)
            except subprocess.TimeoutExpired:
                self.process.kill()
                self.process.wait(timeout=3)
        for reader in self.readers:
            reader.join(timeout=1)
        for stream in [self.process.stdout, self.process.stderr]:
            stream.close()

    def __enter__(self):
        return self

    def __exit__(self, *_):
        self.close()
