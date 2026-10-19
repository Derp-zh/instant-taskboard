"""Test-only native client for the installed desktop's advertised app tools."""
import ctypes
import json
import os
import struct
import uuid
from pathlib import Path

class DesktopPipe:
    def __init__(self):
        names = [p for p in os.listdir('\\\\.\\pipe\\') if p.startswith('codex-app-tools-')]
        if len(names) != 1:
            raise RuntimeError('Ambiguous or unavailable desktop tool pipe')
        self.pipe = open('\\\\.\\pipe\\' + names[0], 'r+b', buffering=0)
        self.next_id = 0
    def close(self):
        self.pipe.close()
    def request(self, method, parameters):
        self.next_id += 1
        body = json.dumps({'id':self.next_id,'jsonrpc':'2.0','method':method,'params':parameters},ensure_ascii=False).encode('utf-8')
        self.pipe.write(struct.pack('<I',len(body))+body)
        def exact(size):
            data = b''
            while len(data)<size:
                part=self.pipe.read(size-len(data))
                if not part: raise EOFError('Desktop connection closed')
                data+=part
            return data
        size=struct.unpack('<I',exact(4))[0]
        if size>8*1024*1024: raise RuntimeError('Oversized desktop response')
        result=json.loads(exact(size))
        if 'error' in result: raise RuntimeError(str(result['error']))
        return result['result']
    def tool(self, name, arguments, caller=None, turn_id=None):
        caller = caller or arguments.get('threadId')
        if not caller: raise RuntimeError('An explicitly authorized test-thread context is required')
        value=self.request('tools/call',{'namespace':'codex_app','tool':name,'arguments':arguments,'callerSource':'codex','threadId':caller,'turnId':turn_id or 'taskboard-test-'+uuid.uuid4().hex,'callId':uuid.uuid4().hex})
        text='\n'.join(item.get('text','') for item in value.get('contentItems',[]) if item.get('type')=='inputText')
        try: return json.loads(text)
        except json.JSONDecodeError: return {'success':value.get('success'),'text':text}

def shape(value, key=''):
    """Print field structure and state values, excluding chat text and identifiers."""
    if isinstance(value,dict): return {k:shape(v,k) for k,v in value.items()}
    if isinstance(value,list): return [shape(v,key) for v in value[:2]]+([{'omitted':len(value)-2}] if len(value)>2 else [])
    if isinstance(value,str):
        if key.lower() in {'status','state','phase','type','kind','reason','role','backingkind','stopreason'}: return value
        return '<string:'+str(len(value))+'>'
    return value

if __name__=='__main__':
    import argparse
    arguments=argparse.ArgumentParser();arguments.add_argument('--thread-id',required=True);selected=arguments.parse_args()
    client=DesktopPipe()
    try:
        value=client.tool('read_thread',{'threadId':selected.thread_id,'turnLimit':1,'includeOutputs':False,'maxOutputCharsPerItem':0})
        print(json.dumps(shape(value),ensure_ascii=False,indent=2))
    finally: client.close()
