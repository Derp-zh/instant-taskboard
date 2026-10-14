import json
from pathlib import Path
import sqlite3
import sys
import tempfile
import unittest

from tools.codex_probe.rpc import CodexRpc, RpcError
from tools.codex_probe.state import BoundTask
from tools.codex_probe.storage import HistoryReader


def event(method, thread="chat-a", **params):
    return {"method": method, "params": {"threadId": thread, **params}}


class FlowTests(unittest.TestCase):
    def test_two_chats_and_root_session_id_cannot_cross_tasks(self):
        a, b = BoundTask("a", "chat-a"), BoundTask("b", "chat-b")
        message = event("turn/started", "chat-b", turn={"id": "turn-b"}, sessionId="chat-a")
        self.assertFalse(a.apply(message, 1))
        self.assertTrue(b.apply(message, 1))
        self.assertIsNone(a.turn_id)

    def test_wait_resolve_is_not_resume(self):
        task = BoundTask("a", "chat-a", timer_unread=True)
        task.apply(event("turn/started", turn={"id": "turn-a"}), 1)
        request = {**event("item/tool/requestUserInput", turnId="turn-a"), "id": 9}
        task.apply(request, 2)
        self.assertEqual(task.lane, "action")
        task.apply(event("serverRequest/resolved", requestId=9), 3)
        self.assertEqual(task.lane, "action")
        task.apply(event("thread/status/changed", status={"type": "active", "activeFlags": []}), 4)
        self.assertEqual(task.lane, "running")
        self.assertTrue(task.timer_unread)
        task.user_action()
        self.assertFalse(task.timer_unread)

    def test_one_of_two_requests_resolved_still_waits(self):
        task = BoundTask("a", "chat-a")
        for i in [1, 2]:
            task.apply({**event("item/commandExecution/requestApproval"), "id": i}, i)
        task.apply(event("serverRequest/resolved", requestId=1), 3)
        self.assertEqual(task.requests, {"2"})
        self.assertEqual(task.lane, "action")

    def test_four_manual_lanes_are_protected(self):
        for lane in ["inspiration", "refining", "paused", "done"]:
            task = BoundTask("a", "chat-a", lane=lane)
            task.apply(event("turn/started", turn={"id": "t"}), 1)
            task.apply(event("turn/completed", turn={"id": "t", "status": "completed"}), 2)
            self.assertEqual(task.lane, lane)
            self.assertEqual(task.phase, "ready_for_review")

    def test_disconnect_and_not_loaded_do_not_report_success(self):
        for method, params in [("probe/disconnected", {}),
                               ("thread/status/changed", {"status": {"type": "notLoaded"}})]:
            task = BoundTask("a", "chat-a")
            task.apply(event(method, **params), 1)
            self.assertEqual(task.phase, "unknown")
            self.assertEqual(task.lane, "running")

    def test_old_turn_completion_and_old_binding_are_ignored(self):
        task = BoundTask("a", "chat-a")
        task.apply(event("turn/started", turn={"id": "new"}), 1)
        self.assertFalse(task.apply(event("turn/completed", turn={"id": "old", "status": "completed"}), 2))
        task.rebind("chat-b")
        self.assertFalse(task.apply(event("turn/started", "chat-b", turn={"id": "b"}), 3, generation=1))
        self.assertTrue(task.apply(event("turn/started", "chat-b", turn={"id": "b"}), 3, generation=2))

    def test_duplicate_and_out_of_order_events_do_not_reapply(self):
        task = BoundTask("a", "chat-a")
        task.apply(event("turn/completed", turn={"id": "t", "status": "completed"}), 4)
        self.assertFalse(task.apply(event("turn/started", turn={"id": "t"}), 3))
        self.assertFalse(task.apply(event("turn/started", turn={"id": "t"}), 4))
        self.assertEqual(task.lane, "action")

    def test_replayed_start_cannot_erase_a_pending_question(self):
        task = BoundTask("a", "chat-a")
        task.apply(event("turn/started", turn={"id": "t"}), 1)
        task.apply({**event("item/tool/requestUserInput", turnId="t"), "id": 1}, 2)
        self.assertFalse(task.apply(event("turn/started", turn={"id": "t"}), 3))
        self.assertEqual(task.phase, "waiting_for_input")
        self.assertEqual(task.requests, {"1"})

    def test_failed_interrupted_and_idle_remain_distinct(self):
        for status in ["failed", "interrupted"]:
            task = BoundTask("a", "chat-a")
            task.apply(event("turn/completed", turn={"id": "t", "status": status}), 1)
            self.assertEqual(task.phase, status)
            self.assertEqual(task.lane, "running")  # Exceptional movement not yet chosen.
        task = BoundTask("a", "chat-a")
        task.apply(event("thread/status/changed", status={"type": "idle"}), 1)
        self.assertEqual(task.phase, "unknown")

    def test_future_active_flag_does_not_guess_running(self):
        task = BoundTask("a", "chat-a", lane="action")
        task.apply(event("thread/status/changed", status={"type": "active", "activeFlags": ["newUnknownFlag"]}), 1)
        self.assertEqual(task.lane, "action")
        self.assertEqual(task.phase, "unknown")


class TransportTests(unittest.TestCase):
    def test_stdio_correlation_timeout_and_mutation_guard(self):
        with tempfile.TemporaryDirectory() as directory:
            script = Path(directory) / "server.py"
            script.write_text('''import json,sys
for line in sys.stdin:
    q=json.loads(line)
    if 'id' not in q: continue
    if q['method']=='thread/items/list': continue
    if q['method']=='thread/start': r={'thread':{'id':'owned'}}
    else: r={'method':q['method']}
    print(json.dumps({'method':'thread/status/changed','params':{'threadId':'existing','status':{'type':'active','activeFlags':[]}}}),flush=True)
    print(json.dumps({'id':q['id'],'result':r}),flush=True)
''', encoding="utf-8")
            with CodexRpc(sys.executable, [str(script)], owned=True) as rpc:
                self.assertEqual(rpc.call("thread/read", {"threadId": "existing"})["method"], "thread/read")
                with self.assertRaisesRegex(RpcError, "thread_not_owned"):
                    rpc.call("turn/interrupt", {"threadId": "existing", "turnId": "x"})
                rpc.call("thread/start", {})
                self.assertIn("owned", rpc.owned_threads)
                with self.assertRaisesRegex(RpcError, "request_timeout"):
                    rpc.call("thread/items/list", {"threadId": "existing"}, timeout=0.05)
                self.assertEqual(rpc.call("thread/read", {"threadId": "existing"})["method"], "thread/read")
                with self.assertRaisesRegex(RpcError, "cannot_answer_existing_thread"):
                    rpc.answer_owned({"id": 99, "params": {"threadId": "existing"}}, {})
            self.assertIsNotNone(rpc.process.poll())

    def test_observation_connection_cannot_start_or_respond(self):
        with CodexRpc(sys.executable, ["-c", "import time;time.sleep(10)"]) as rpc:
            with self.assertRaisesRegex(RpcError, "mutation_not_allowed"):
                rpc.call("thread/start", {})
            with self.assertRaisesRegex(RpcError, "cannot_answer_existing_thread"):
                rpc.answer_owned({"id": 1, "params": {"threadId": "a"}}, {})


class StorageTests(unittest.TestCase):
    def test_metadata_is_read_only_and_running_is_not_authoritative(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            c = sqlite3.connect(root / "state_5.sqlite")
            c.execute("create table threads(id,source,history_mode,updated_at,archived)")
            c.execute("insert into threads values('chat-a','appServer','paginated',5,0)")
            c.commit(); c.close()
            c = sqlite3.connect(root / "thread_history_1.sqlite")
            c.execute("create table thread_turns(thread_id,turn_id,status,started_at,completed_at,rollout_ordinal)")
            c.execute("insert into thread_turns values('chat-a','old-stale-turn','inProgress',1,NULL,1)")
            c.commit(); c.close()
            before = {p.name: p.read_bytes() for p in root.glob("*.sqlite")}
            reader = HistoryReader(root)
            self.assertEqual(reader.catalog()[0]["threadId"], "chat-a")
            snapshot = reader.snapshot("chat-a")
            self.assertEqual(snapshot["latestTurn"]["status"], "inProgress")
            self.assertFalse(snapshot["authoritativeLiveState"])
            self.assertFalse(snapshot["waitingApprovalSupported"])
            self.assertFalse(reader.snapshot("missing")["found"])
            self.assertEqual(before, {p.name: p.read_bytes() for p in root.glob("*.sqlite")})


if __name__ == "__main__":
    unittest.main()
