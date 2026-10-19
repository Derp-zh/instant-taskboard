"""Control only the explicit isolated lifecycle harness and its A/B test chats."""
import argparse
import json
import os
import time
import uuid
from pathlib import Path
from codex_desktop_pipe import DesktopPipe

def state(root):
    for _ in range(10):
        try: return json.loads((root/'lifecycle-state.json').read_text(encoding='utf-8-sig'))
        except (FileNotFoundError,json.JSONDecodeError): time.sleep(.05)
    raise RuntimeError('No lifecycle state')

def command(root, kind, label=None, alias='A', expect=None):
    value={'id':uuid.uuid4().hex,'kind':kind,'label':label or kind,'alias':alias}
    if expect is not None:value['expect']=expect
    temporary=root/'lifecycle-command.tmp';temporary.write_text(json.dumps(value),encoding='utf-8')
    os.replace(temporary,root/'lifecycle-command.json')
    end=time.monotonic()+15
    while time.monotonic()<end:
        error=root/'startup-error.txt'
        if error.exists():raise RuntimeError(error.read_text(encoding='utf-8-sig'))
        try:
            done=json.loads((root/'lifecycle-command-done.json').read_text(encoding='utf-8-sig'))
            if done.get('id')==value['id']:return state(root)
        except (FileNotFoundError,json.JSONDecodeError):pass
        time.sleep(.1)
    raise TimeoutError('Harness command not acknowledged')

def prompt(alias, text):
    if alias not in {'A','B'}:raise RuntimeError('Only owned A/B test chats are allowed')
    record=Path('test-output/codex0916-desktop-lifecycle')/('thread-'+alias+'.json')
    target=json.loads(record.read_text(encoding='utf-8'))['threadId']
    client=DesktopPipe()
    try:
        caller=os.environ.get('CODEX_THREAD_ID')
        if not caller:raise RuntimeError('Missing explicitly authorized calling chat context')
        return client.tool('send_message_to_thread',{'threadId':target,'hostId':'local','prompt':text},caller=caller)
    finally:client.close()

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--root',default='test-output/codex0916-original-desktop-run2');p.add_argument('--kind');p.add_argument('--alias',default='A');p.add_argument('--label');p.add_argument('--expect');p.add_argument('--prompt-file');p.add_argument('--state',action='store_true');args=p.parse_args();root=Path(args.root)
    if not root.resolve().is_relative_to((Path.cwd()/'test-output').resolve()):raise RuntimeError('Only isolated test-output roots')
    if args.state:result=state(root)
    elif args.prompt_file:result=prompt(args.alias,Path(args.prompt_file).read_text(encoding='utf-8-sig'))
    else:result=command(root,args.kind,args.label,args.alias,json.loads(args.expect) if args.expect else None)
    print(json.dumps(result,ensure_ascii=False))
