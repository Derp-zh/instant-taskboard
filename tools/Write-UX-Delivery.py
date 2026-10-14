import json, hashlib
from pathlib import Path
def read(path): return json.loads(Path(path).read_text(encoding='utf-8-sig'))
base='test-output/ux08-desktop-final-14'
ui=read(base+'/ui-report.json')
motion=read(base+'/motion-report.json')
pixels=read(base+'/animation-pixels.json')
performance=read(base+'/performance-report.json')
mouse=read('test-output/ux08-mouse-final-07/desktop-report.json')
core=read('test-output/ux08-core-final-01/report.json')
codex=read('test-output/ux08-codex-final-bytes/codex-report.json')
assert ui['passed']>=77 and mouse['Passed']==42 and core['passed']==87
assert all(x['passed'] for x in motion['reports']) and pixels['passed'] and performance['passed'] and codex['passed']
folder=Path('release/即时任务看板-0.8.1')
report={
 'Version':'0.8.1','Date':'2026-10-14','Scope':'U32-U37 / A50-A55 / T52-T58',
 'Product':'即时任务看板',
 'FinalProgramHashes':{f:hashlib.sha256((folder/f).read_bytes()).hexdigest().upper() for f in ['即时任务看板.exe','Taskboard.dll','Taskboard.Core.dll']},
 'Core':core,'UI':ui,'Mouse':mouse,'DynamicWindows':motion,'ActualAnimationPixels':pixels,'Performance':performance,'CodexReadOnly':codex,
 'EvidenceDirectories':{'core':'test-output/ux08-core-final-01','uiAndDesktop':base,'mouse':'test-output/ux08-mouse-final-07','codex':'test-output/ux08-codex-final-bytes'},
 'EvidenceBoundary':'Mouse evidence was collected before the last changes to MotionChecks only; interactive runtime code is unchanged. Final UI/pixel evidence uses the delivered runtime bytes.',
 'Limits':['120Hz is supported by independent composition, not inferred from an 8ms timer. Monitor/system load limit actual presentation; pixel sampling does not enumerate every display refresh.',
           'Existing Codex history and IDs are readable. The current desktop live bridge is not connected; history never drives automatic lane changes.',
           'Windows 11 on this device tested; mixed-DPI multiple displays, other Windows builds, first-time autostart and machine migration are not covered.',
           'Parallel notification listening module retains its separate real-notification/audio acceptance boundary; this UI report does not certify it.'],
 'FailuresPreserved':'Earlier isolated runs remain unchanged. Corrected checks wait for actual background colors, sample away from moving edges, read current input bounds, use native mouse hit messages and place sorting drops at the list bottom.'
}
Path('docs/VERIFICATION-0.8.1-ux.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({'core':core['passed'],'ui':ui['passed'],'mouse':mouse['Passed'],'monitorHz':performance['displayRefreshHz'],'feedbackMs':performance['toggleSynchronousFeedbackMs']},ensure_ascii=False))
