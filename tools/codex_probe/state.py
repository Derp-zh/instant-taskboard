"""Source-independent rules for a single explicitly bound thread."""
from dataclasses import dataclass, field


@dataclass
class BoundTask:
    task_id: str
    thread_id: str
    lane: str = "running"
    generation: int = 1
    phase: str = "unknown"
    turn_id: str | None = None
    timer_unread: bool = False
    last_sequence: int = -1
    requests: set[str] = field(default_factory=set)

    def rebind(self, thread_id: str) -> None:
        self.thread_id = thread_id
        self.generation += 1
        self.phase = "unknown"
        self.turn_id = None
        self.last_sequence = -1
        self.requests.clear()

    def user_action(self) -> None:
        self.timer_unread = False

    def summary(self) -> dict:
        return {"taskId": self.task_id, "threadId": self.thread_id,
                "lane": self.lane, "phase": self.phase, "turnId": self.turn_id,
                "timerUnread": self.timer_unread, "generation": self.generation,
                "pendingRequestCount": len(self.requests)}

    def apply(self, event: dict, sequence: int, generation: int | None = None) -> bool:
        method = event.get("method", "")
        params = event.get("params") or {}
        thread_id = params.get("threadId") or (params.get("thread") or {}).get("id")
        if thread_id != self.thread_id or sequence <= self.last_sequence:
            return False
        if generation is not None and generation != self.generation:
            return False
        # Delayed events from a previous turn must not override the new active turn.
        turn = params.get("turn") or {}
        turn_id = params.get("turnId") or turn.get("id")
        if method != "turn/started" and self.turn_id and turn_id and turn_id != self.turn_id:
            return False
        if method == "turn/started" and turn_id and turn_id == self.turn_id:
            return False  # A replayed start cannot erase a live pending request.
        before = self.summary()
        self.last_sequence = sequence
        if method == "probe/disconnected":
            self.phase = "unknown"
        elif method == "turn/started":
            self.turn_id = turn_id
            self.requests.clear()
            self.phase = "running"
        elif method in {"thread/status/changed", "probe/snapshot"}:
            status = params.get("status") or {}
            flags = status.get("activeFlags") or []
            if status.get("type") == "active":
                if "waitingOnUserInput" in flags:
                    self.phase = "waiting_for_input"
                elif "waitingOnApproval" in flags:
                    self.phase = "waiting_for_approval"
                elif flags:
                    self.phase = "unknown"  # Future flags must fail closed.
                else:
                    self.requests.clear()
                    self.phase = "running"
            elif status.get("type") in {"notLoaded", "systemError"}:
                self.phase = "unknown"
            # idle alone says nothing about whether the last turn succeeded.
        elif method in {"item/tool/requestUserInput", "tool/requestUserInput",
                        "mcpServer/elicitation/request"}:
            self.requests.add(str(event.get("id")))
            self.phase = "waiting_for_input"
        elif method in {"item/commandExecution/requestApproval",
                        "item/fileChange/requestApproval", "item/permissions/requestApproval"}:
            self.requests.add(str(event.get("id")))
            self.phase = "waiting_for_approval"
        elif method == "serverRequest/resolved":
            self.requests.discard(str(params.get("requestId")))
            # No automatic return: resolution may mean cancellation or denial.
        elif method == "turn/completed":
            self.turn_id = turn_id or self.turn_id
            self.requests.clear()
            self.phase = {"completed": "ready_for_review", "failed": "failed",
                          "interrupted": "interrupted"}.get(turn.get("status"), "unknown")
        if self.lane in {"running", "action"}:
            if self.phase == "running":
                self.lane = "running"
            elif self.phase in {"waiting_for_input", "waiting_for_approval", "ready_for_review"}:
                self.lane = "action"
            # Failure/cancellation movement is still a product choice; record only.
        return before != self.summary()
