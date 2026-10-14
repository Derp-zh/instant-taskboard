import argparse
import collections
import json
import os
from pathlib import Path
import re
import shutil
import sqlite3
import sys
import time
import tomllib

from .rpc import CodexRpc, RpcError
from .state import BoundTask
from .storage import HistoryReader


def write_report(directory: Path, name: str, value: dict):
    directory.mkdir(parents=True, exist_ok=True)
    target = directory / name
    temporary = target.with_suffix(target.suffix + ".pending")
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")
    temporary.replace(target)


def server_args(home: Path):
    # Invocation-only overrides: no global configuration or credentials are changed.
    args = ["app-server", "--listen", "stdio://", "-c", "features.hooks=false",
            "-c", "analytics.enabled=false", "-c", "mcp_servers={}", "-c", "plugins={}"]
    config = home / "config.toml"
    if config.exists():
        data = tomllib.loads(config.read_text(encoding="utf-8-sig"))
        # CLI dotted-key overrides do not reliably parse quoted key segments.
        # Bare keys supported by TOML cover this installation's integrations.
        for name in data.get("mcp_servers", {}):
            if re.fullmatch(r"[A-Za-z0-9_-]+", name):
                args += ["-c", f"mcp_servers.{name}.enabled=false"]
    return args


def metadata_probe(args):
    reader = HistoryReader(args.codex_home)
    report = {"probe": "current_desktop_metadata"}
    thread_id = args.thread_id or os.environ.get("CODEX_THREAD_ID")
    try:
        report["catalog"] = reader.catalog()
        if thread_id:
            report["exactThread"] = reader.snapshot(thread_id)
    except (sqlite3.Error, OSError) as error:
        report["metadataErrorType"] = type(error).__name__
    report["sharedProxy"] = {}
    try:
        with CodexRpc(args.codex, ["app-server", "proxy"]) as rpc:
            rpc.initialize()
            report["sharedProxy"] = {"connected": True,
                    "loadedThreadIds": rpc.call("thread/loaded/list").get("data", [])}
            if thread_id:
                thread = rpc.call("thread/read", {"threadId": thread_id, "includeTurns": False})["thread"]
                report["sharedProxy"]["exactThreadStatus"] = thread.get("status")
    except (RpcError, OSError) as error:
        report["sharedProxy"] = {"connected": False, "errorType": str(error) if isinstance(error, RpcError) else type(error).__name__}
    # A separate app-server may read history yet has no authority over desktop liveness.
    try:
        with CodexRpc(args.codex, server_args(args.codex_home)) as rpc:
            initialized = rpc.initialize()
            report["separateServer"] = {"connected": True, "serverVersion": initialized.get("userAgent"),
                                      "isDesktopServer": False}
            if thread_id:
                thread = rpc.call("thread/read", {"threadId": thread_id, "includeTurns": False})["thread"]
                report["separateServer"]["exactThreadStatus"] = thread.get("status")
    except (RpcError, OSError) as error:
        report["separateServer"] = {"connected": False, "isDesktopServer": False,
                                  "errorType": str(error) if isinstance(error, RpcError) else type(error).__name__}
    write_report(args.data_dir, "desktop-metadata.json", report)
    print(json.dumps(report, ensure_ascii=False))


def live_probe(args):
    timeline = []
    report = {"probe": "owned_ephemeral_threads", "cases": [], "desktopValidated": False}
    workspace = args.data_dir.resolve() / "isolated-workspace"
    workspace.mkdir(parents=True, exist_ok=True)
    try:
        with CodexRpc(args.codex, server_args(args.codex_home), owned=True) as rpc:
            initialized = rpc.initialize()
            report["serverConnected"] = True
            report["serverVersion"] = initialized.get("userAgent")
            threads = [rpc.call("thread/start", {"cwd": str(workspace), "ephemeral": True,
                       "sandbox": "read-only", "approvalPolicy": "on-request",
                       "approvalsReviewer": "user", "developerInstructions":
                       "This is an isolated protocol test. Do only what the test prompt requests. "
                       "Do not inspect files, use external services, or delegate to agents."}, timeout=30)
                       for _ in range(2)]
            tasks = [BoundTask("probe-task-" + str(i + 1), t["thread"]["id"])
                     for i, t in enumerate(threads)]
            report["threads"] = [t.summary() for t in tasks]
            cursor = 0

            def drain():
                nonlocal cursor
                while cursor < len(rpc.events):
                    event = rpc.events[cursor]
                    cursor += 1
                    for task in tasks:
                        if task.apply(event, cursor):
                            timeline.append({"sequence": cursor, "method": event.get("method"), **task.summary()})

            def run_case(name, task_index, prompt, mode=None, answer_questions=False, decline_approval=False):
                task = tasks[task_index]
                start = len(rpc.events)
                params = {"threadId": task.thread_id, "input": [{"type": "text", "text": prompt}]}
                if mode:
                    params["collaborationMode"] = {"mode": mode, "settings": {
                        "model": threads[task_index]["model"], "reasoning_effort": "low",
                        "developer_instructions": None}}
                result = rpc.call("turn/start", params, timeout=30)
                turn_id = result["turn"]["id"]
                answered = False
                approval_seen = False
                observed_snapshots = {}
                deadline = time.monotonic() + args.timeout
                completed = False
                print(json.dumps({"caseStarted": name, "threadId": task.thread_id}), flush=True)
                while time.monotonic() < deadline:
                    drain()
                    for event in rpc.events[start:]:
                        params = event.get("params") or {}
                        if params.get("threadId") != task.thread_id:
                            continue
                        if event.get("method") in {"item/tool/requestUserInput", "tool/requestUserInput"} and answer_questions and not answered:
                            snapshot = rpc.call("thread/read", {"threadId": task.thread_id, "includeTurns": False})["thread"]
                            observed_snapshots["lateAssociationStatus"] = snapshot["status"]
                            late_task = BoundTask("late-associated-task", task.thread_id)
                            late_task.apply({"method": "probe/snapshot", "params": {
                                "threadId": task.thread_id, "status": snapshot["status"]}}, 1)
                            observed_snapshots["lateAssociatedTask"] = late_task.summary()
                            # While chat B waits, exercise chat A through the same source.
                            # B must remain pending and its lane must not follow A's completion.
                            run_case("other_thread_while_question_waits", 0,
                                     "Reply with PROBE_PARALLEL. Do not use any tools.")
                            still_waiting = rpc.call("thread/read", {"threadId": task.thread_id,
                                                     "includeTurns": False})["thread"]["status"]
                            observed_snapshots["stillWaitingAfterOtherThreadCompleted"] = still_waiting
                            # Answer only the probe's explicitly owned test request.
                            questions = params.get("questions") or []
                            answers = {q["id"]: {"answers": [(q.get("options") or [{"label": "A"}])[0]["label"]]} for q in questions}
                            rpc.answer_owned(event, {"answers": answers})
                            answered = True
                        if "requestApproval" in event.get("method", "") and decline_approval and not approval_seen:
                            approval_seen = True
                            decision = {"permissions": {}} if event["method"] == "item/permissions/requestApproval" else {"decision": "decline"}
                            rpc.answer_owned(event, decision)
                        if event.get("method") == "turn/completed" and (params.get("turn") or {}).get("id") == turn_id:
                            completed = True
                    if completed:
                        break
                    rpc.pump(0.2)
                if not completed:
                    rpc.call("turn/interrupt", {"threadId": task.thread_id, "turnId": turn_id})
                    end = time.monotonic() + 5
                    while time.monotonic() < end:
                        rpc.pump(0.2)
                        if any(e.get("method") == "turn/completed" and (e.get("params", {}).get("turn") or {}).get("id") == turn_id for e in rpc.events[start:]):
                            break
                drain()
                methods = collections.Counter(e.get("method") for e in rpc.events[start:] if e.get("params", {}).get("threadId") == task.thread_id)
                case = {"name": name, "threadId": task.thread_id, "turnId": turn_id,
                        "completedBeforeTimeout": completed, "questionAnswered": answered,
                        "approvalObservedAndDeclined": approval_seen, "methods": dict(methods),
                        "observedSnapshots": observed_snapshots, "finalTask": task.summary()}
                report["cases"].append(case)
                write_report(args.data_dir, "live-probe.json", {**report, "timeline": timeline})
                print(json.dumps({"caseFinished": name, "questionObserved": answered,
                                  "approvalObserved": approval_seen, "phase": task.phase}), flush=True)

            run_case("completion", 0, "Reply with the single word PROBE_OK. Do not use any tools.")
            run_case("question_and_resume", 1,
                     "Protocol test: call request_user_input with one question asking whether to choose A or B, "
                     "with two options. Wait for the tool response. After the response, say PROBE_ANSWERED. "
                     "Do not ask only in plain text. Do not use any other tools.", mode="plan", answer_questions=True)
            run_case("permission_request", 0,
                     "Protocol test: use the command execution tool to request sandbox_permissions=require_escalated "
                     "for the harmless command whoami. The justification is 'isolated permission signal test'. "
                     "Do not run it without asking for that permission. If the approval is declined, "
                     "say PROBE_DECLINED and do not retry or use another tool.", mode="default", decline_approval=True)
            report["timeline"] = timeline
    except (RpcError, OSError, KeyError) as error:
        report["errorType"] = str(error) if isinstance(error, RpcError) else type(error).__name__
        report["timeline"] = timeline
    write_report(args.data_dir, "live-probe.json", report)
    print(json.dumps({"report": str((args.data_dir / "live-probe.json").resolve()),
                      "caseCount": len(report["cases"]), "errorType": report.get("errorType")}, ensure_ascii=False))
    return 1 if "errorType" in report else 0


def main():
    parser = argparse.ArgumentParser(description="Codex independent feasibility probe (no Taskboard data access)")
    parser.add_argument("mode", choices=["inspect", "live"])
    parser.add_argument("--data-dir", type=Path, required=True, help="Isolated output directory")
    inherited_home = Path(os.environ.get("CODEX_HOME", Path.home() / ".codex"))
    parser.add_argument("--codex-home", type=Path, default=inherited_home)
    parser.add_argument("--codex", default=shutil.which("codex"))
    parser.add_argument("--thread-id")
    parser.add_argument("--timeout", type=float, default=90)
    args = parser.parse_args()
    if not args.codex:
        parser.error("Codex executable not found")
    if args.timeout <= 0:
        parser.error("Timeout must be positive")
    if args.codex_home.resolve() != inherited_home.resolve():
        parser.error("This probe uses the existing Codex profile; another profile requires a separate probe process")
    if args.data_dir.resolve() == args.codex_home.resolve() or args.codex_home.resolve() in args.data_dir.resolve().parents:
        parser.error("Output must not be inside Codex's data directory")
    return live_probe(args) if args.mode == "live" else metadata_probe(args)


if __name__ == "__main__":
    sys.exit(main() or 0)
