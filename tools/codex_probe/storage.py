"""Metadata-only fallback. Historical inProgress is NEVER authoritative liveness."""
import sqlite3
from pathlib import Path


def readonly(path: Path):
    connection = sqlite3.connect(path.resolve().as_uri() + "?mode=ro", uri=True, timeout=2)
    connection.execute("pragma query_only=ON")
    return connection


class HistoryReader:
    def __init__(self, codex_home: Path):
        self.home = codex_home

    def catalog(self, limit: int = 12):
        db = readonly(self.home / "state_5.sqlite")
        try:
            return [{"threadId": row[0], "source": row[1], "historyMode": row[2],
                     "updatedAt": row[3]} for row in db.execute(
                        "select id,source,history_mode,updated_at from threads "
                        "where archived=0 and source in ('cli','vscode','appServer','exec','unknown') "
                        "order by updated_at desc limit ?", (limit,))]
        finally:
            db.close()

    def snapshot(self, thread_id: str):
        db = readonly(self.home / "state_5.sqlite")
        try:
            row = db.execute("select id,source,history_mode from threads where id=?", (thread_id,)).fetchone()
        finally:
            db.close()
        if not row:
            return {"threadId": thread_id, "found": False, "authoritativeLiveState": False}
        db = readonly(self.home / "thread_history_1.sqlite")
        try:
            turn = db.execute("select turn_id,status,started_at,completed_at from thread_turns "
                              "where thread_id=? order by rollout_ordinal desc limit 1", (thread_id,)).fetchone()
        finally:
            db.close()
        return {"threadId": thread_id, "found": True, "source": row[1], "historyMode": row[2],
                "latestTurn": None if not turn else {"id": turn[0], "status": turn[1],
                    "startedAt": turn[2], "completedAt": turn[3]},
                "authoritativeLiveState": False, "waitingInputSupported": False,
                "waitingApprovalSupported": False,
                "limitation": "persisted_history_does_not_prove_liveness_or_pending_requests"}
