"""Package the independent local 0.9.30 build without touching a running app.

Run from the repository: python tools/Package-0930.py
The build must already exist at release/即时任务看板-0.9.30. This script
refreshes only this version's five delivery documents in that new directory,
rejects data and obsolete launchers, creates a new ZIP, and verifies every byte.
It never launches Codex/the board, inspects user data, or publishes anything.
"""

from __future__ import annotations

import argparse
import ctypes
from ctypes import wintypes
import hashlib
import json
import os
from pathlib import Path
import shutil
import sys
import xml.etree.ElementTree as ET
import zipfile


VERSION = "0.9.30"
BUILD_DATE = "2026-11-02"
ROOT = Path(__file__).resolve().parents[1]
DEFAULT_DIRECTORY = Path("release") / f"即时任务看板-{VERSION}"
DELIVERY_DOCS = {
    f"USER-GUIDE-{VERSION}.md": "使用说明.md",
    f"RELEASE-NOTES-{VERSION}.md": "版本说明.md",
    f"ACCEPTANCE-{VERSION}.md": f"ACCEPTANCE-{VERSION}.md",
    f"VERIFICATION-{VERSION}.json": f"VERIFICATION-{VERSION}.json",
    "THIRD-PARTY-NOTICES.txt": "THIRD-PARTY-NOTICES.txt",
}
BLOCKED_PARTS = {
    "data", "configuration", "config", "preserved-data", "sessions",
    "archived_sessions", ".git", ".codex", ".aws", ".codebase-memory",
    "__pycache__", "test-output", "output",
}
BLOCKED_NAMES = {
    "board.json", "settings.json", "launch-settings.json", "auth.json",
    "credentials.json", "config.toml", "endpoint.json", ".env",
}
BLOCKED_SUFFIXES = {
    ".sqlite", ".sqlite3", ".db", ".jsonl", ".log", ".pem", ".key",
    ".ps1", ".cmd", ".bat", ".sh", ".zip", ".pdb",
}
SOURCE_SUFFIXES = {".cs", ".csproj", ".xaml", ".manifest", ".ico", ".png", ".svg", ".resx"}
CHECK_SUFFIXES = {".cs", ".csproj", ".py", ".ps1", ".mjs"}


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def under(path: Path, parent: Path) -> bool:
    try:
        path.relative_to(parent)
        return True
    except ValueError:
        return False


def no_reparse(path: Path) -> None:
    # Avoid following either symbolic links or Windows directory junctions.
    attributes = getattr(path.lstat(), "st_file_attributes", 0)
    require(not path.is_symlink() and not attributes & 0x400,
            f"拒绝链接或重解析点：{path}")


def files_beneath(folder: Path):
    no_reparse(folder)
    for current, directories, files in os.walk(folder, followlinks=False):
        for name in directories:
            child = Path(current) / name
            no_reparse(child)
            require(not any(part.casefold() in BLOCKED_PARTS for part in child.relative_to(folder).parts),
                    f"包内含数据或私有目录：{child.relative_to(folder)}")
        for name in files:
            path = Path(current) / name
            no_reparse(path)
            require(under(path.resolve(), folder), f"文件越出包目录：{path}")
            yield path


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest().upper()


def executable_version(path: Path) -> str:
    """Read the PE fixed version resource; do not execute the binary."""
    require(os.name == "nt", "必须在Windows上核对0.9.30实际程序版本。")
    api = ctypes.WinDLL("version", use_last_error=True)
    api.GetFileVersionInfoSizeW.argtypes = [wintypes.LPCWSTR, ctypes.POINTER(wintypes.DWORD)]
    api.GetFileVersionInfoSizeW.restype = wintypes.DWORD
    api.GetFileVersionInfoW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, wintypes.LPVOID]
    api.GetFileVersionInfoW.restype = wintypes.BOOL
    api.VerQueryValueW.argtypes = [wintypes.LPCVOID, wintypes.LPCWSTR,
                                  ctypes.POINTER(ctypes.c_void_p), ctypes.POINTER(wintypes.UINT)]
    api.VerQueryValueW.restype = wintypes.BOOL
    handle = wintypes.DWORD()
    length = api.GetFileVersionInfoSizeW(str(path), ctypes.byref(handle))
    require(length > 0, "程序缺少Windows版本资源。")
    buffer = ctypes.create_string_buffer(length)
    require(bool(api.GetFileVersionInfoW(str(path), 0, length, buffer)), "读取程序版本资源失败。")
    pointer = ctypes.c_void_p()
    size = wintypes.UINT()
    require(bool(api.VerQueryValueW(buffer, "\\", ctypes.byref(pointer), ctypes.byref(size)))
            and size.value >= 13 * 4, "程序固定版本资源无效。")
    fields = ctypes.cast(pointer, ctypes.POINTER(wintypes.DWORD))
    require(fields[0] == 0xFEEF04BD, "程序固定版本签名无效。")
    major_minor, build_revision = fields[4], fields[5]
    require(build_revision & 0xFFFF == 0, "程序修订号不符合0.9.30构建。")
    return f"{major_minor >> 16}.{major_minor & 0xFFFF}.{build_revision >> 16}"


def check_package_file(path: Path, folder: Path) -> None:
    relative = path.relative_to(folder)
    parts = [part.casefold() for part in relative.parts]
    basename = parts[-1]
    require(not any(part in BLOCKED_PARTS for part in parts), f"包内含数据或私有目录：{relative}")
    require(basename not in BLOCKED_NAMES and not basename.startswith(".env."),
            f"包内含配置或凭据文件：{relative}")
    require(path.suffix.casefold() not in BLOCKED_SUFFIXES and not basename.endswith(("-wal", "-shm")),
            f"包内含数据、调试文件或旧启动器：{relative}")
    if path.suffix.casefold() == ".md":
        require(path.name in DELIVERY_DOCS.values(), f"包内含非本轮使用说明或旧文档：{relative}")
    if path.suffix.casefold() == ".json":
        require(path.name in {"Taskboard.deps.json", "Taskboard.runtimeconfig.json", f"VERIFICATION-{VERSION}.json"},
                f"包内含非运行时或非本轮验收JSON：{relative}")
    if path.suffix.casefold() == ".txt":
        require(path.name == "THIRD-PARTY-NOTICES.txt", f"包内含未知文本文件：{relative}")


def source_inventory() -> tuple[list[dict], list[dict]]:
    paths: set[Path] = set()
    excluded = [{"Path": "src/Taskboard.Desktop/Assets/Alarm04.wav",
                 "Reason": "Windows本机提供的默认声音，不纳入可再分发源码/资源清单；产品中为既有嵌入资源。"}]
    for path in (ROOT / "src").rglob("*"):
        if path.is_file() and not any(part in {"bin", "obj"} for part in path.parts) and path.suffix.casefold() in SOURCE_SUFFIXES:
            paths.add(path)
    for directory in (ROOT / "tests", ROOT / "tools" / "CodexObserverChecks"):
        for path in directory.rglob("*"):
            if path.is_file() and not any(part in {"bin", "obj", "__pycache__"} for part in path.parts) and path.suffix.casefold() in CHECK_SUFFIXES:
                paths.add(path)
    for name in ("Build-Portable.ps1", "Verify-LocalCatalog.py", "Verify-FileProjection.py", "check_current_docs.py", "Package-0930.py"):
        path = ROOT / "tools" / name
        require(path.is_file(), f"缺少相关工具：{path}")
        paths.add(path)
    for path in (ROOT / "tools").glob("*0930*"):
        if path.is_file() and path.suffix.casefold() in CHECK_SUFFIXES:
            paths.add(path)
    inventory = []
    for path in sorted(paths, key=lambda p: p.relative_to(ROOT).as_posix()):
        no_reparse(path)
        require(under(path.resolve(), ROOT), f"源码清单越界：{path}")
        inventory.append({"Path": path.relative_to(ROOT).as_posix(), "Bytes": path.stat().st_size, "SHA256": sha256(path)})
    return inventory, excluded


def package(directory: Path) -> dict:
    no_reparse(ROOT / "release")
    release = (ROOT / "release").resolve()
    folder = (ROOT / directory).resolve()
    require(under(folder, release) and folder != release, "包目录必须位于项目release内。")
    require(folder.name == f"即时任务看板-{VERSION}", "只处理独立的即时任务看板-0.9.30目录。")
    require(folder.is_dir(), "请先构建0.9.30独立便携包。")
    no_reparse(folder)
    entry = folder / "即时任务看板.exe"
    require(entry.is_file(), "缺少即时任务看板.exe。")
    require(executable_version(entry) == VERSION, "实际程序版本不是0.9.30。")
    require(ET.parse(ROOT / "src/Taskboard.Desktop/Taskboard.Desktop.csproj").findtext("PropertyGroup/Version") == VERSION,
            "当前源码版本不是0.9.30。")
    archive = release / f"instant-taskboard-{VERSION}-win-x64.zip"
    sidecar = Path(str(archive) + ".sha256")
    manifest = ROOT / "docs" / f"BUILD-INFO-{VERSION}.json"
    for path in (archive, sidecar, manifest):
        require(not path.exists(), f"不覆盖既有交付文件：{path}")
    for source_name in DELIVERY_DOCS:
        require((ROOT / "docs" / source_name).is_file(), f"缺少本轮交付文档：docs/{source_name}")
    verification = json.loads((ROOT / "docs" / f"VERIFICATION-{VERSION}.json").read_text(encoding="utf-8-sig"))
    require(verification.get("version", verification.get("Version")) == VERSION, "验收机器摘要版本不符。")
    # Refuse bad contents before changing even the delivery documents.
    for path in files_beneath(folder):
        check_package_file(path, folder)
    sources, excluded = source_inventory()
    for source_name, destination_name in DELIVERY_DOCS.items():
        destination = folder / destination_name
        if destination.exists():
            no_reparse(destination)
        shutil.copyfile(ROOT / "docs" / source_name, destination)
    files = []
    for path in sorted(files_beneath(folder), key=lambda p: p.relative_to(folder).as_posix()):
        check_package_file(path, folder)
        files.append({"Path": path.relative_to(folder).as_posix(), "Bytes": path.stat().st_size, "SHA256": sha256(path)})
    with zipfile.ZipFile(archive, "x", compression=zipfile.ZIP_DEFLATED, compresslevel=9, allowZip64=True) as zipped:
        for item in files:
            zipped.write(folder / item["Path"], item["Path"])
    expected = {item["Path"]: item for item in files}
    with zipfile.ZipFile(archive, "r") as zipped:
        entries = zipped.infolist()
        require(len(entries) == len(expected) and len({entry.filename for entry in entries}) == len(entries), "ZIP文件数量或路径唯一性不符。")
        for item in entries:
            require(item.filename in expected, f"ZIP出现额外文件：{item.filename}")
            digest = hashlib.sha256()
            with zipped.open(item) as stream:
                for block in iter(lambda: stream.read(1024 * 1024), b""):
                    digest.update(block)
            want = expected[item.filename]
            require(item.file_size == want["Bytes"] and digest.hexdigest().upper() == want["SHA256"], f"ZIP内容不符：{item.filename}")
    # Ensure the independent build did not change during compression.
    require({p.relative_to(folder).as_posix() for p in files_beneath(folder)} == set(expected), "打包期间目录文件集合变化。")
    require(all((folder / item["Path"]).stat().st_size == item["Bytes"] and sha256(folder / item["Path"]) == item["SHA256"] for item in files),
            "打包期间目录内容变化。")
    require(all(sha256(ROOT / item["Path"]) == item["SHA256"] for item in sources), "打包期间源码发生变化。")
    digest = sha256(archive)
    information = {
        "Version": VERSION, "BuildDate": BUILD_DATE, "Runtime": "self-contained win-x64", "Published": False,
        "Entry": entry.relative_to(ROOT).as_posix(), "Archive": archive.relative_to(ROOT).as_posix(),
        "ArchiveSHA256": digest, "Bytes": archive.stat().st_size, "FileCount": len(files), "Files": files,
        "Sources": sources, "SourceScope": "all src source/project/redistributable resources and related tests/tools; bin/obj and local Windows sound excluded",
        "SourceExclusions": excluded, "ZipAllFilesVerified": True, "ContainsData": False,
        "GitHeadIsNotCurrentSourceBaseline": True,
        "ValidationScope": "Package integrity only; not proof of desktop lifecycle, zero idle side effect or system-wide traffic reduction.",
    }
    with manifest.open("x", encoding="utf-8", newline="\n") as stream:
        json.dump(information, stream, ensure_ascii=False, indent=2)
        stream.write("\n")
    with sidecar.open("x", encoding="utf-8", newline="\n") as stream:
        stream.write(f"{digest}  {archive.name}\n")
    return {"Version": VERSION, "Archive": archive.relative_to(ROOT).as_posix(), "FileCount": len(files),
            "SourceCount": len(sources), "Bytes": information["Bytes"], "SHA256": digest,
            "ZipAllFilesVerified": True, "Published": False}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--directory", type=Path, default=DEFAULT_DIRECTORY)
    args = parser.parse_args()
    try:
        print(json.dumps(package(args.directory), ensure_ascii=False, indent=2))
        return 0
    except (OSError, ValueError, ET.ParseError, zipfile.BadZipFile) as error:
        print(f"0.9.30打包停止：{error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
