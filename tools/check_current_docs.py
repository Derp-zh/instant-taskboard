"""Check active documentation links, migration coverage and immutable snapshots.

No application startup, task-data access, network or desktop input.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
from datetime import datetime, timezone
from pathlib import Path
from urllib.parse import unquote, urlsplit

ROOT = Path(__file__).resolve().parents[1]
HISTORY = ROOT / 'docs/history/20261021-before-current'


def read(path: Path) -> str:
    return path.read_text(encoding='utf-8-sig')


def requirement_ids(text: str) -> set[str]:
    found = set(re.findall(r'^- (R\d+[a-z]?)：', text, re.M))
    for cell in re.findall(r'^\|\s*([^|]+)\|', text, re.M):
        found.update(re.findall(r'\b(?:U\d+|D\d+|A\d+[a-z]?)\b', cell))
    return found


def heading_ids(text: str) -> set[str]:
    ids, counts = set(), {}
    for title in re.findall(r'^#{1,6}\s+(.+?)\s*#*$', text, re.M):
        title = re.sub(r'[`*_~]', '', title).strip().lower()
        slug = ''.join(c for c in title if c.isalnum() or c in '-_ ')
        slug = slug.replace(' ', '-')
        count = counts.get(slug, 0)
        counts[slug] = count + 1
        ids.add(slug + (f'-{count}' if count else ''))
    return ids


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--report', help='Optional JSON report, relative to repository root')
    args = parser.parse_args()
    errors, warnings = [], []
    active = [ROOT / name for name in (
        'AGENTS.md', 'docs/HANDOFF.md', 'docs/INDEX.md', 'docs/PRD.md',
        'docs/TASKS.md', 'docs/IMPLEMENTATION.md', 'docs/DOCS-MAINTENANCE.md',
        'docs/INDEX-STATUS.md',
    )]
    active += sorted((ROOT / 'docs/current').glob('*.md'))
    active += sorted((ROOT / 'docs/architecture').glob('*.md'))
    linked = 0
    for path in active:
        if not path.is_file():
            errors.append(f'Missing active document: {path.relative_to(ROOT)}')
            continue
        text = read(path)
        # Inline Markdown links; code spans and prose paths are intentionally not
        # treated as links. Archived snapshots retain their original relative base.
        for raw in re.findall(r'(?<!!)\[[^\]\n]+\]\(([^)\n]+)\)', text):
            target = raw.strip().strip('<>')
            if target.startswith(('http:', 'https:', 'mailto:', 'app:', 'codex:')):
                continue
            parsed = urlsplit(target)
            if parsed.scheme:
                errors.append(f'Unsupported local URI: {path.name}: {target}')
                continue
            dest = (path.parent / unquote(parsed.path)).resolve() if parsed.path else path
            if not dest.is_relative_to(ROOT):
                errors.append(f'Link escapes project: {path.name}: {target}')
            elif not dest.exists():
                errors.append(f'Broken link: {path.relative_to(ROOT)} -> {target}')
            elif parsed.fragment and dest.suffix == '.md' and unquote(parsed.fragment) not in heading_ids(read(dest)):
                errors.append(f'Broken heading: {path.relative_to(ROOT)} -> {target}')
            linked += 1

    manifest = json.loads(read(HISTORY / 'manifest.json'))
    for row in manifest['files']:
        file = (ROOT / row['snapshot']).resolve()
        if not file.is_relative_to(HISTORY):
            errors.append('Snapshot escapes history: ' + row['snapshot'])
        elif not file.is_file() or hashlib.sha256(file.read_bytes()).hexdigest() != row['sha256']:
            errors.append('Snapshot SHA256 mismatch: ' + row['snapshot'])

    coverage = json.loads(read(HISTORY / 'coverage.json'))
    expected = requirement_ids(read(ROOT / coverage['source']))
    actual = [row['source_id'] for row in coverage['rules']]
    if len(actual) != len(set(actual)):
        errors.append('Duplicate source requirement mappings')
    if set(actual) != expected:
        errors.append('Unmapped or invented IDs: ' + repr(sorted(expected ^ set(actual))))
    if coverage['rule_count'] != len(actual):
        errors.append('Coverage count does not match entries')
    for row in coverage['rules']:
        target = ROOT / row['current_page']
        if not target.is_file() or not target.resolve().is_relative_to(ROOT / 'docs/current'):
            errors.append('Missing current rule target: ' + row['source_id'])

    banned = {
        'docs/current/M05-阶段与卡片.md': [r'6至10个英文M字符宽', r'最多显示四个', r'前一个阶段、后两个阶段'],
        'docs/current/M04-新建编辑与草稿.md': [r'窗外编辑窗口不关闭', r'只有点击保存时修改才生效'],
        'docs/current/M07-卡片操作与归档.md': [r'历史最多20条', r'第21条永久替换'],
    }
    for name, patterns in banned.items():
        for pattern in patterns:
            if re.search(pattern, read(ROOT / name)):
                errors.append(f'Retired rule in effective page: {name}: {pattern}')
    for name, phrases in {
        'docs/current/M04-新建编辑与草稿.md': ['与保存按钮共用验证和落盘', '2/5', '最后1/3'],
        'docs/current/M05-阶段与卡片.md': ['3至5个', '全部运行阶段', '至少一个', '24DIP/秒'],
        'docs/current/C01-Codex接入.md': ['跨断线/重连保持', '整卡回进行中', 'failed/interrupted'],
        'docs/current/SURFACE-背景动画与输入.md': ['真实控件', '58至370', '快速重开取消旧清理'],
        'docs/architecture/SHARED-CODE.md': ['先落盘', '列表空白右键', '真实系统+内部命中+操作'],
    }.items():
        for phrase in phrases:
            if phrase not in read(ROOT / name):
                errors.append(f'Missing key contract: {name}: {phrase}')
    if 'docs/history/' not in read(ROOT / '.cbmignore').splitlines():
        errors.append('History snapshots not excluded by .cbmignore')

    sync = json.loads(read(ROOT / 'docs/CODEBASE-MEMORY-SYNC.json'))
    payload = sync['update_payload']
    if payload.get('project') != 'Instant-taskboard' or payload.get('mode') != 'update':
        errors.append('Incorrect Codebase Memory sync identity/mode')
    for phrase in ('0.9.20-widthfix', 'docs/current/README.md', 'docs/architecture/SHARED-CODE.md'):
        if phrase not in payload['content']:
            errors.append('Memory summary lacks authoritative pointer: ' + phrase)
    if sync['status'] != 'synced':
        warnings.append('Codebase Memory summary not yet synced: ' + sync['status'])
    if '现行' not in read(ROOT / 'docs/HANDOFF.md') or 'docs/current/' not in read(ROOT / 'AGENTS.md'):
        errors.append('Missing root route to current rules')

    report = {
        'checked_at_utc': datetime.now(timezone.utc).isoformat(),
        'baseline': '0.9.30 Codex cleanup and settings redesign; strict passive monitoring incomplete; public 0.9.23 retained', 'passed': not errors,
        'active_markdown_files': len(active), 'local_links_checked': linked,
        'immutable_snapshots_checked': len(manifest['files']),
        'source_ids_mapped': len(actual), 'memory_summary_status': sync['status'],
        'errors': errors, 'warnings': warnings,
        'scope': 'Documentation integrity only; no product tests, application startup, desktop input, task data or GitHub operations.',
        'semantic_limit': 'ID coverage and selected contracts do not prove complete natural-language equivalence; consolidated rules were manually reviewed.',
        'active_document_sha256': {p.relative_to(ROOT).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest() for p in active if p.is_file()},
    }
    if args.report:
        output = (ROOT / args.report).resolve()
        if not output.is_relative_to(ROOT) or output in active or output.is_relative_to(HISTORY):
            parser.error('Report must stay in project and cannot overwrite active docs or snapshots')
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({k:v for k,v in report.items() if k != 'active_document_sha256'}, ensure_ascii=True, indent=2))
    return 0 if not errors else 1


if __name__ == '__main__':
    raise SystemExit(main())
