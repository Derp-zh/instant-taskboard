<!-- README_SYNC: source=v0.9.20; updated=2026-10-21 -->
<p align="center"><a href="./README.md">简体中文</a> · English</p>

# Instant Taskboard

A Windows desktop board for people juggling several projects: move work through six independent lists and keep each task stage beside its associated conversation.

If it fits your workflow, a Star is an easy way to bookmark the project and help others find it too.<br>
<a href="https://github.com/Derp-zh/instant-taskboard/stargazers"><img alt="GitHub stars" src="https://img.shields.io/github/stars/Derp-zh/instant-taskboard?style=flat-square&label=Stars"></a>
<a href="https://github.com/Derp-zh/instant-taskboard/releases/tag/v0.9.20"><img alt="Latest release" src="https://img.shields.io/github/v/release/Derp-zh/instant-taskboard?style=flat-square&label=release"></a>
<a href="https://github.com/Derp-zh/instant-taskboard/issues"><img alt="GitHub issues" src="https://img.shields.io/github/issues/Derp-zh/instant-taskboard?style=flat-square&label=issues"></a>
<a href="https://github.com/Derp-zh/instant-taskboard"><img alt="Windows x64" src="https://img.shields.io/badge/platform-Windows%20x64-blue?style=flat-square"></a>

## Get the app

Download the [0.9.20 Windows x64 portable package](https://github.com/Derp-zh/instant-taskboard/releases/tag/v0.9.20) and its SHA256 file from the release page. Extract the full archive and run `即时任务看板.exe`. The package is self-contained; no separate .NET installation is needed. Exit an older version from the tray before upgrading, and keep the extracted folder intact.

Tasks and settings are stored under the current Windows user profile by default. You can inspect or change the location in settings. The app includes backup and restore controls; it does not require uploading your task board to a cloud service.

## What it helps you do

- **Keep six work areas separate:** Ideas, Refining, In Progress, Needs Attention, Paused, and Done each have an independent window you can arrange around your workspace.
- **Track work by stage:** A task can contain multiple stages, each with its own conversation binding. Running stages show their status, while actionable items receive priority.
- **Move ideas into a workflow:** Use the shared draft pool, quick idea capture, search, reminders, archiving, and undo as work changes.
- **Keep the board near your desktop:** Use list windows, a floating orb, or the tray. Window transitions reveal real controls through clipping; heavy UI activity can drop animation frames.

## Optional Codex observation

The optional Codex observation mode is off by default. It reads a limited set of session state through a local observation endpoint. It does not answer prompts or approve actions, and it does not infer task state from chat text. It depends on a compatible Codex version and manual startup steps; see the [observation guide](docs/CODEX-PENDING-BRIDGE-PUBLIC.md) for scope and unverified cases. This is not a general integration for every AI app.

Feishu cloud sync is not implemented. Task data is stored locally by default; protect it with your usual device backup practices.

## Build from source

You need Windows, the .NET 8 SDK, PowerShell 7, and network access to NuGet. On Windows, the build script copies the system-provided default alert sound into the output folder; that system audio file is not included in the source repository.

```powershell
pwsh -NoProfile -File tools/Build-Portable.ps1 -Output release/portable
```

See the [user guide](docs/USER-GUIDE-0.9.20.md), [product requirements](docs/PRD.md), and [task list](docs/TASKS.md). Third-party notices are in [NOTICE](docs/THIRD-PARTY-NOTICES.txt).

## License and feedback

This repository currently has no `LICENSE` file, so terms for reusing, modifying, and redistributing the source have not been declared. Publicly visible source code does not by itself establish a license. Third-party components in the app remain subject to their respective notices.

For questions or proposed improvements, open an [Issue](https://github.com/Derp-zh/instant-taskboard/issues). If the board works for you, the Star link above is a good way to bookmark it.
