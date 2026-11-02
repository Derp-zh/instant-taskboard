"""Run one 0.9.30 offscreen check with fresh data/profile/Codex directories."""
import json
import os
from pathlib import Path
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[1]
MODES = {"--settings-0930-verify", "--ai-init-verify", "--ux0922-verify", "--integration-verify", "--codex-reconnect-verify", "--startup-check"}

def main():
    if len(sys.argv) != 4 or sys.argv[2] not in MODES:
        raise SystemExit("usage: Verify-0930.py <entry> <check-mode> <new-test-output-dir>")
    entry = (ROOT / sys.argv[1]).resolve()
    output = (ROOT / sys.argv[3]).resolve()
    if not entry.is_relative_to(ROOT) or not entry.is_file():
        raise SystemExit("entry must exist inside workspace")
    if not output.is_relative_to(ROOT / "test-output") or output.exists():
        raise SystemExit("choose a fresh test-output directory")
    profile = output.with_name(output.name + "-profile")
    if profile.exists():
        raise SystemExit("profile must also be new")
    env = os.environ.copy()
    for name in list(env):
        if any(part in name.upper() for part in ("API_KEY", "TOKEN", "AUTH", "PASSWORD", "SECRET")):
            env.pop(name)
    paths = {"CODEX_HOME": "codex", "USERPROFILE": "user", "HOME": "user", "APPDATA": "user/AppData/Roaming",
             "LOCALAPPDATA": "user/AppData/Local", "TMP": "tmp", "TEMP": "tmp", "DOTNET_CLI_HOME": "cli"}
    for key, relative in paths.items():
        target = profile / relative
        target.mkdir(parents=True, exist_ok=True)
        env[key] = str(target)
    env["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
    env["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1"
    command = [str(entry), "--data-dir", str(output), "--no-integrations", sys.argv[2]]
    started = time.monotonic()
    proc = subprocess.Popen(command, cwd=ROOT, env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    try:
        stdout, stderr = proc.communicate(timeout=120)
    except subprocess.TimeoutExpired:
        proc.kill()  # Only the isolated child started immediately above.
        stdout, stderr = proc.communicate()
        output.mkdir(parents=True, exist_ok=True)
        (output / "timeout.txt").write_text("isolated child exceeded 120s", encoding="utf-8")
    output.mkdir(parents=True, exist_ok=True)
    (output / "stdout.txt").write_bytes(stdout)
    (output / "stderr.txt").write_bytes(stderr)
    result = {"mode": sys.argv[2], "exitCode": proc.returncode, "elapsedSeconds": round(time.monotonic() - started, 3),
              "dataDir": str(output), "freshProfile": str(profile), "integrationsDisabled": True,
              "scope": "isolated offscreen product checks; no real Codex lifecycle or complete network measurement"}
    (output / "runner.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(result, ensure_ascii=False))
    return 0 if proc.returncode == 0 else 1

if __name__ == "__main__":
    raise SystemExit(main())
