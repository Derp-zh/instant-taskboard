"""File-projection route against a synthetic local Codex home.

The production reader (CodexFileActivity) only opens state_5.sqlite and
thread_history_1.sqlite read-only plus the rollout file's stat data. This driver
proves the fixture is byte-identical afterwards, no WAL/SHM file appears, no
proxy request is attempted, and no auxiliary process is started.
"""
import hashlib, http.server, json, os, pathlib, sqlite3, subprocess, sys, threading, time
import psutil

root = pathlib.Path(__file__).resolve().parents[1]
output = (root / sys.argv[1]).resolve()
assert output.is_relative_to(root / 'test-output') and not output.exists()
# Everything synthetic lives beside the data dir: the check runner itself refuses
# a --data-dir that already exists, so it must create the output directory.
sandbox = output.parent / (output.name + '-fixture')
assert not sandbox.exists()
fixture = sandbox / 'fixture'; (fixture / 'rollouts').mkdir(parents=True)

A = '11111111-1111-4111-8111-111111111111'
B = '22222222-2222-4222-8222-222222222222'
C = '33333333-3333-4333-8333-333333333333'
D = '44444444-4444-4444-8444-444444444444'
E = '55555555-5555-4555-8555-555555555555'
F = '66666666-6666-4666-8666-666666666666'
Z = '77777777-7777-4777-8777-777777777777'

def w(path, size): path.write_bytes((b'{"type":"event"}\n' * (size // 17 + 1))[:size])
for name, size in (('a', 200), ('b', 100), ('c', 50), ('d', 300), ('e', 10), ('f', 10)):
    w(fixture / 'rollouts' / (name + '.jsonl'), size)

def state(directory, rows):
    directory.mkdir(parents=True, exist_ok=True)
    db = sqlite3.connect(directory / 'state_5.sqlite')
    db.execute('create table threads(id text primary key,title text,updated_at integer,source text,archived integer,name text,rollout_path text)')
    db.executemany('insert into threads values(?,?,?,?,?,?,?)', rows)
    db.commit(); db.close()

def history(directory, turns, offsets, schema='full'):
    directory.mkdir(parents=True, exist_ok=True)
    db = sqlite3.connect(directory / 'thread_history_1.sqlite')
    if schema == 'full':
        db.execute('create table thread_turns(thread_id text,turn_id text,status text,rollout_ordinal integer,started_at integer,completed_at integer,error_json text)')
        db.executemany('insert into thread_turns values(?,?,?,?,?,?,null)', turns)
        db.execute('create table thread_items(thread_id text,turn_id text,item_id text,rollout_ordinal integer,created_at_ms integer,item_type text,item_json text)')
        db.execute('create table thread_history_projection_state(thread_id text primary key,next_rollout_byte_offset integer,next_rollout_ordinal integer)')
        db.executemany('insert into thread_history_projection_state values(?,?,0)', offsets)
    else:
        db.execute('create table thread_turns(thread_id text,turn_id text,status text)')
        db.executemany('insert into thread_turns values(?,?,?)', [(t[0], t[1], t[2]) for t in turns])
        db.execute('create table thread_history_projection_state(thread_id text primary key,next_rollout_byte_offset integer)')
        db.executemany('insert into thread_history_projection_state values(?,?)', offsets)
    db.commit(); db.close()

def rollout(n): return str(fixture / 'rollouts' / (n + '.jsonl'))
state(fixture, [
    (A, '进行中', 100, 'vscode', 0, '', rollout('a')),
    (B, '已完成', 90, 'cli', 0, '', rollout('b')),
    (C, '旧终态', 80, 'appServer', 0, '', rollout('c')),
    (D, '投影落后', 70, 'vscode', 0, '', rollout('d')),
    (E, '未知状态', 60, 'vscode', 0, '', rollout('e')),
    (F, '缺轮次', 50, 'vscode', 0, '', rollout('f')),
])
history(fixture,
    [(A, 't-a', 'inProgress', 5, 1, None), (B, 't-b', 'completed', 2, 1, 2),
     (C, 't-c-old', 'completed', 1, 1, 2), (D, 't-d', 'inProgress', 3, 1, None),
     (E, 't-e', 'running', 1, 1, None), (Z, 't-z', 'inProgress', 4, 1, None)],
    [(A, 200), (B, 100), (C, 50), (D, 100), (E, 10), (Z, 10)])
state(fixture / 'missing-history', [(A, '缺轮次库', 1, 'vscode', 0, '', rollout('a'))])
(history(fixture / 'unknown-schema', [], [], schema='old'),)
state(fixture / 'unknown-schema', [(A, '旧结构', 1, 'vscode', 0, '', rollout('a'))])

def hashes():
    return {str(p.relative_to(fixture)): hashlib.sha256(p.read_bytes()).hexdigest() for p in fixture.rglob('*') if p.is_file()}
before = hashes()
claims = []
class Proxy(http.server.BaseHTTPRequestHandler):
    def log_message(self, *args): pass
    def do_CONNECT(self): claims.append({'method': 'CONNECT', 'target': self.path}); self.send_error(403)
    def do_GET(self): claims.append({'method': 'GET', 'target': self.path}); self.send_error(403)
proxy = http.server.ThreadingHTTPServer(('127.0.0.1', 0), Proxy)
threading.Thread(target=proxy.serve_forever, daemon=True).start()
env = os.environ.copy()
for name in list(env):
    if any(k in name.upper() for k in ('API_KEY', 'TOKEN', 'AUTH', 'PASSWORD', 'SECRET')): env.pop(name)
for name in ('user', 'tmp'): (sandbox / name).mkdir()
env.update(CODEX_HOME=str(fixture), HOME=str(sandbox / 'user'), USERPROFILE=str(sandbox / 'user'),
           TMP=str(sandbox / 'tmp'), TEMP=str(sandbox / 'tmp'),
           APPDATA=str(sandbox / 'user/AppData/Roaming'), LOCALAPPDATA=str(sandbox / 'user/AppData/Local'),
           TASKBOARD_FILE_PROJECTION_FIXTURE=str(fixture))
for key in ('HTTP_PROXY', 'HTTPS_PROXY', 'ALL_PROXY', 'http_proxy', 'https_proxy', 'all_proxy'):
    env[key] = f'http://127.0.0.1:{proxy.server_port}'
env['NO_PROXY'] = env['no_proxy'] = 'localhost,127.0.0.1'
cmd = ['dotnet', str(root / 'tools/CodexObserverChecks/bin/Debug/net8.0-windows10.0.19041.0/CodexObserverChecks.dll'),
       '--data-dir', str(output), '--activity']
proc = None
try:
    # The runner prints Chinese diagnostics in the console code page, so capture
    # bytes and decode leniently instead of failing the whole run on one byte.
    proc = subprocess.Popen(cmd, cwd=root, env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    tree = {proc.pid: 'file-projection-check-runner'}
    clock = time.monotonic()
    while proc.poll() is None:
        try:
            for child in psutil.Process(proc.pid).children(recursive=True): tree[child.pid] = child.name()
        except psutil.Error: pass
        assert time.monotonic() - clock < 60, 'isolated runner timed out'
        time.sleep(0.02)
    stdout, stderr = (text.decode('gbk', errors='replace') for text in proc.communicate(timeout=5))
    (output / 'stdout.txt').write_text(stdout, encoding='utf-8')
    (output / 'stderr.txt').write_text(stderr, encoding='utf-8')
    assert proc.returncode == 0, stderr + stdout
    assert len(tree) == 1, 'unexpected auxiliary process: ' + json.dumps(tree)
    after = hashes(); assert before == after, 'reader or checks changed fixture bytes'
    assert not claims, 'unexpected proxy request'
    report = json.loads((output / 'file-projection.json').read_text(encoding='utf-8'))
    assert report['passed'] >= 20, report
    assert report['chatQueries'] == 0 and report['turnQueries'] == 0, report
    result = dict(passed=True, checks=report['passed'], dataDir=str(output), fixtureHome=str(fixture),
                  fixtureHashesUnchanged=True, proxyRequests=claims, auxiliaryProcesses=len(tree),
                  chatQueries=0, turnQueries=0, syntheticHome=True, realCodexTouched=False,
                  scope='synthetic local Codex files; not a desktop lifecycle or public-network measurement')
    (output / 'file-projection-verify.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(dict(passed=True, checks=report['passed'], report=str(output / 'file-projection.json'))))
finally:
    if proc is not None and proc.poll() is None: proc.kill(); proc.wait(timeout=5)
    proxy.shutdown(); proxy.server_close()
