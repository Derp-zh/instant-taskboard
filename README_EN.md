<!-- README_SYNC: source=working-tree; updated=2026-10-22 -->
[简体中文](README.md) · English

# Instant Taskboard

A Windows desktop task board for capturing ideas, organizing work, and linking individual task stages to Codex conversations.

Built for people juggling several tasks and AI conversations: keep your ideas, ongoing work, and tasks that need your attention visible on the desktop, ready to pick up again.

Find it useful? [Star the project](https://github.com/Derp-zh/instant-taskboard) to keep it handy.

[Download for Windows](https://github.com/Derp-zh/instant-taskboard/releases/latest) · [Report an issue](https://github.com/Derp-zh/instant-taskboard/issues)

Current version **0.9.23** adds connection-first Codex checks, read-only tests for linked chats, Store upgrade path repair, and a new observation adapter. See the [release notes](docs/RELEASE-NOTES-0.9.23.md).

## What is it for?

Suppose you have several projects underway. Some are still ideas, some are being worked on by AI, and others need an answer or a review from you. Put them on the board, record the steps and related conversations, and find the right task when you need to step in.

The default board has six lists. Each is a separate window you can move and resize:

| List | What goes here |
| --- | --- |
| Ideas | Quick notes and things you might do later |
| Refining | Tasks whose requirements or approach still need work |
| In progress | Work currently underway |
| Needs action | Tasks waiting for your answer, approval, review, or next step |
| On hold | Work you want to keep but are not pursuing right now |
| Done | Tasks you have personally confirmed as complete |

Rename, add, or hide lists. All six built-in lists are protected from deletion; empty custom lists can still be deleted. Drag tasks between lists or reorder them within a list.

## Features

| Feature | What you can do |
| --- | --- |
| Quick capture | Type directly into the Ideas list, or open the full editor to add a name, details, and categories |
| Multiple drafts | Keep several unfinished entries in a stack, switch between them, and resume drafts after restarting |
| Task stages | Split a task into named stages, each with its own notes, running flag, and conversation link; delete with X while retaining at least one stage and one running stage |
| Labels and priority | Assign multiple AI platform labels and one priority; customize option names and colors |
| Search | Search task names and all stage details, then jump straight to the matching card |
| Scheduled reminders | Set a one-time, daily, or weekly reminder; when due, the task moves to Needs action and you receive a notification |
| Needs-action preview | See the pending count on the floating button; hover while the board is collapsed to preview tasks, then click to locate one |
| Undo and redo | Reverse or reapply the last 20 committed edits in the current session, including accidental deletions, moves, and field changes |
| Task archive | Manually archive Done or On hold tasks, or automatically archive Done tasks after a configurable number of days; archived tasks can be restored |
| Floating controls | Toggle the board from the floating button, tray, or global shortcut; dock the floating button to the right edge of the desktop with auto-hide |
| Desktop layout | Position and resize lists independently, scale the group, remember positions, and adjust transparency separately for lists, editors, and the floating button |

Collapsing the board keeps the app running, with reminders and unsubmitted input preserved. Choose Exit from the system tray to quit completely.

## Link Codex conversations

**Each stage can link to an existing Codex conversation**, with its name and status shown on the card. Different stages of the same task can use different conversations.

With optional local status observation enabled, tasks can move automatically between In progress and Needs action:

- Waiting for your answer or approval, or finishing a run that needs review, moves the task to Needs action.
- Actual execution resuming moves it back to In progress. If several stages are active, a stage still waiting for attention takes priority.
- A completed AI run never marks the task Done automatically. You decide when the task is finished.

**Initialize or recheck from Settings → AI platform integration, then run a read-only test of linked chats. No scripts are required. An app update alone does not block connection: chat search, the live connection, and question/approval observation are checked separately.** The installed 26.1016.8120.0 passed catalog and original-desktop transport checks. A new observation adapter is included; its endpoint is not enabled, so real question/approval/recovery lifecycle validation remains pending. No messages, answers or approvals are sent. See the [Codex observation guide](docs/CODEX-PENDING-BRIDGE-PUBLIC.md).

AI platform labels can also serve as ordinary categories; choosing one does not connect that platform. Feishu two-way sync and live DSH or WorkBuddy integration are not implemented.

## Custom notification sounds

The notification-listening settings let you configure a Windows notification sound for each application, using the built-in sound or your own audio, with playback previews. This module is separate from Codex status observation; notifications do not move tasks between lists.

Listening is off by default. Actual notification capture, replacement of the original system sound, and restoration still need separate validation for the target application and Windows environment.

## Download and get started

For 64-bit Windows 10 (version 2004 or later) and Windows 11.

1. Open the [latest release](https://github.com/Derp-zh/instant-taskboard/releases/latest) and download the Windows x64 ZIP.
2. Extract the entire archive and run `即时任务看板.exe`. The package includes .NET, so no separate runtime installation is needed. Keep the extracted folder intact.
3. Click the floating button to open the board. Capture an idea directly in the Ideas list, or create a task from any list.
4. Add reminders to individual tasks. Adjust windows, the shortcut, and startup at sign-in in Settings. The default global shortcut is `Ctrl+~`.

Before upgrading, exit the old version from its tray menu, then extract the new version in full. See the [user guide](docs/USER-GUIDE-0.9.23.md) for more.

## Local data

Tasks, stages, and drafts are stored locally. The default task directory is `%LOCALAPPDATA%/InstantTaskboard/data`; configuration is in `%LOCALAPPDATA%/InstantTaskboard/config`. Both locations can be changed independently in Settings.

App files and data are stored separately. When moving to another computer, take the actual data and configuration directories with you. The configuration-backup feature saves settings only: **it does not include tasks or drafts**.

Report problems or suggestions through [Issues](https://github.com/Derp-zh/instant-taskboard/issues), or [star the project](https://github.com/Derp-zh/instant-taskboard) to find it again later.

The repository currently has no LICENSE file. Terms for using and redistributing the source have not been declared.
