"""Production native catalog against synthetic DB; keep WAL live. No real Codex/account/pipe."""
import hashlib, http.server, json, os, pathlib, sqlite3, subprocess, sys, threading, time
import psutil
root = pathlib.Path(__file__).resolve().parents[1]
output = (root / sys.argv[1]).resolve()
assert output.is_relative_to(root / 'test-output') and not output.exists()
home = output / 'codex-home'; home.mkdir(parents=True)
def database(directory, rows, name=True):
    directory.mkdir(parents=True, exist_ok=True)
    db = sqlite3.connect(directory / 'state_5.sqlite')
    db.execute('create table threads(id text primary key,title text,updated_at integer,source text,archived integer' + (',name text' if name else '') + ')')
    db.executemany('insert into threads values(' + ','.join('?' for _ in range(6 if name else 5)) + ')', rows)
    db.commit(); return db
tid = lambda n: f'11111111-1111-4111-8111-{n:012d}'
rows = [(tid(n), '标题回退' if n == 1 else f'fixture {n}', 1000-n, 'vscode', 0, '手动命名 · 中文😀' if n == 0 else '') for n in range(205)]
rows += [(tid(901), 'archived', 9999, 'vscode', 1, ''), (tid(902), 'subagent', 9998, '{"subAgent":"threadSpawn"}', 0, ''), ('invalid-id','bad',9997,'cli',0,'')]
database(home, rows).close()
database(home / 'legacy', [(tid(1), '旧结构标题', 1, 'cli', 0)], False).close()
database(home / 'empty', []).close(); database(home / 'future', []).close()
(home / 'future/state_6.sqlite').write_bytes(b'new version must not fall back to stale v5')
(home / 'schema').mkdir(); db=sqlite3.connect(home / 'schema/state_5.sqlite'); db.execute('create table threads(foo text)'); db.close()
(home / 'corrupt').mkdir(); (home / 'corrupt/state_5.sqlite').write_bytes(b'not a database')
database(home / '自定义库',[(tid(1),'自定义本机库',1,'cli',0,None)]).close()
database(home / 'custom',[]).close()
(home / 'custom/config.toml').write_text('sqlite_home = '+json.dumps(str(home / '自定义库'),ensure_ascii=False)+' # local override\n',encoding='utf-8')
(home / 'custom-unsupported').mkdir(); (home / 'custom-unsupported/config.toml').write_text('sqlite_home = 123\n',encoding='utf-8')
(home / 'custom-network').mkdir(); (home / 'custom-network/config.toml').write_text("sqlite_home = '\\\\127.0.0.1\\never-touch\\db'\n",encoding='utf-8')
wal = database(home / 'wal', [])
wal.execute('PRAGMA journal_mode=WAL'); wal.execute('PRAGMA wal_autocheckpoint=0')
wal.execute('insert into threads values(?,?,?,?,?,?)', (tid(1), '已提交仍在WAL', 1, 'appServer', 0, None)); wal.commit()
def hashes():
    return {str(p.relative_to(home)): hashlib.sha256(p.read_bytes()).hexdigest() for p in home.rglob('*') if p.is_file() and not p.name.endswith('-shm')}
before = hashes(); env = os.environ.copy()
for name in list(env):
    if any(k in name.upper() for k in ('API_KEY','TOKEN','AUTH','PASSWORD','SECRET')): env.pop(name)
env.update(CODEX_HOME=str(home), HOME=str(output / 'user'), USERPROFILE=str(output / 'user'), TMP=str(output / 'tmp'), TEMP=str(output / 'tmp'), TASKBOARD_PREPARED_CATALOG_FIXTURE=str(output))
env.update(APPDATA=str(output / 'user/AppData/Roaming'),LOCALAPPDATA=str(output / 'user/AppData/Local'))
if len(sys.argv)>2:
    assembly=(root / sys.argv[2]).resolve()
    assert assembly.is_relative_to(root / 'release') and assembly.name=='Taskboard.dll'
    env['TASKBOARD_PACKAGE_ASSEMBLY']=str(assembly)
attempts=[]
class Proxy(http.server.BaseHTTPRequestHandler):
    def log_message(self,*args): pass
    def do_CONNECT(self):
        attempts.append(dict(method='CONNECT',target=self.path)); self.send_error(403)
    def do_GET(self):
        attempts.append(dict(method='GET',target=self.path)); self.send_error(403)
proxy=http.server.ThreadingHTTPServer(('127.0.0.1',0),Proxy)
threading.Thread(target=proxy.serve_forever,daemon=True).start()
for key in ('HTTP_PROXY','HTTPS_PROXY','ALL_PROXY','http_proxy','https_proxy','all_proxy'): env[key]=f'http://127.0.0.1:{proxy.server_port}'
env['NO_PROXY']=env['no_proxy']='localhost,127.0.0.1'
for name in ('user','tmp'): (output / name).mkdir()
cmd = ['dotnet', str(root / 'tools/CodexObserverChecks/bin/Debug/net8.0-windows10.0.19041.0/CodexObserverChecks.dll'), '--data-dir', str(output), '--local-catalog']
proc=None
try:
    env['TASKBOARD_CATALOG_IDLE_PROBE']='1'
    proc=subprocess.Popen(cmd,cwd=root,env=env,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True,encoding='utf-8')
    tree={proc.pid:'production-source-check-runner'}; clock=time.monotonic()
    while proc.poll() is None:
        try:
            for child in psutil.Process(proc.pid).children(recursive=True): tree[child.pid]=child.name()
        except psutil.Error: pass
        assert time.monotonic()-clock < 45, 'isolated runner timed out'
        time.sleep(.01)
    stdout,stderr=proc.communicate(timeout=3)
    (output / 'process-tree.json').write_text(json.dumps(dict(runnerPid=proc.pid,processes=tree,pollIntervalMs=10,lifetimeSeconds=time.monotonic()-clock),indent=2),encoding='utf-8')
    (output / 'stdout.txt').write_text(stdout, encoding='utf-8'); (output / 'stderr.txt').write_text(stderr, encoding='utf-8')
    assert proc.returncode == 0, stderr + stdout
    assert len(tree)==1, 'unexpected auxiliary process'
    after = hashes(); assert before == after, 'reader changed DB, WAL or directory contents'
    assert not attempts, 'unexpected proxy request'
    report = dict(passed=True, dataDir=str(output), fixtureHome=str(home), productionSqliteReader=True, dbAndWalHashesUnchanged=True, before=before, after=after, noRealAccount=True, noRealDesktop=True, proxyRequests=attempts, networkCapturePerformed=False, proxyLimit='Only proxy-aware requests; not a system TCP/UDP trace', scope='synthetic local catalog, not complete WPF or desktop lifecycle')
    (output / 'fixture-integrity.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(dict(passed=True, report=str(output / 'local-catalog-report.json'), hashesUnchanged=True)))
finally:
    if proc is not None and proc.poll() is None: proc.kill(); proc.wait(timeout=5)
    proxy.shutdown(); proxy.server_close(); wal.close()
