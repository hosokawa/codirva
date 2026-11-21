# Codirva — Project Brief & Initial Implementation Instructions

> This document is intended to be given to Codex as the initial project brief for the public GitHub repository `codirva`.
> **Project status:** New repository. The `main` branch currently contains an initial commit and an existing `LICENSE` file (MIT License). Do not assume any application functionality has been implemented.

## 1. First task for Codex

1. Inspect the repository before making changes, including the existing `LICENSE`, files, and project configuration.
2. Create the initial **`README.md`** in clear, approachable English. Describe the project's vision, planned architecture, MVP scope, supported environments, and development status, using the decisions below.
3. Clearly label features as **planned** until they are implemented and tested. Do not invent installation steps, screenshots, release binaries, CLI flags, or working features.
4. Include a concise note that feedback, issues, and pull requests are welcome but **not required**. The MIT License permits commercial and proprietary reuse under its conditions.
5. In the README, distinguish **Codirva's role as a human-operated multi-agent console** from autonomous multi-agent orchestration frameworks.
6. If useful, link to this document as the design brief. Leave the existing MIT `LICENSE` file intact; do not replace it or add extra license conditions.
7. Report what was changed and suggest the smallest practical next implementation step. **Do not start a large implementation without a separate request.**

## 2. Identity and motivation

- **Project name:** Codirva (Japanese IPA: /kodiɾɯba/).
- **GitHub repository name:** `codirva`.
- **Working tagline:** *A unified desktop console for coding agents.*
- **Core idea:** Let a human developer directly operate several independent coding-agent sessions from one comfortable desktop UI.
- **Initial targets:** Codex CLI, Claude Code, Pi, and potentially DSH and other terminal-based coding agents.
- **Problem being solved:** Running different agents across many Windows Terminal tabs/panes makes it difficult to switch contexts, compose long prompts, and understand which agent/session is being controlled. An earlier idea was a simple “scratchpad” to compose and paste prompts; the scope has evolved into a unified agent console.
- **Design stance:** Reuse existing coding-agent CLIs instead of rewriting their harnesses. Do not lock the architecture to one model, vendor, or coding agent.

### What Codirva is not (initially)

- Not a replacement for Codex CLI, Claude Code, Pi, or DSH.
- Not an autonomous task planner, supervisor, or agent-team orchestrator in its first version.
- Not a multi-model chatbot API client. The focus is **existing interactive agent processes and their terminal sessions**.
- Not initially a Windows-native process/session manager.

## 3. Technology and deployment decisions

| Area | Initial decision |
| --- | --- |
| Primary language | **C# / .NET** |
| Desktop UI | **Avalonia UI** |
| Target host for the first GUI | **Windows** |
| Initially supported execution environments | **WSL2 Linux** and **remote Linux via SSH** |
| Session persistence / reattachment on Linux | **tmux** |
| WSL transport | `wsl.exe` (or an equivalent supported WSL connection mechanism) |
| Remote Linux transport | Windows OpenSSH `ssh.exe`, with a PTY for interactive attachment |
| Agent integration, v1 | Existing terminal-based CLIs; agent-agnostic where practical |
| Native Windows CLI support | **Deferred**; consider ConPTY and a persistent host process later |
| License | **MIT** (already in the repository) |
| Repository visibility | **Public**; develop openly from the beginning |

**Guiding architectural principle:** The desktop UI, connection transport, terminal rendering, and session management should be distinct concerns. Support multiple target environments without making the user manage Windows Terminal windows/tabs.

### tmux rationale

- The agent runs inside a tmux-managed terminal session on Linux.
- The agent can keep running after the GUI disconnects or the SSH connection drops.
- A GUI client or a conventional terminal client can reconnect to the same existing session.
- tmux exposes session discovery and manipulation commands and has a control mode (`tmux -C`) worth evaluating.
- tmux only preserves processes while its server/host remains running. Machine reboot or tmux server termination is **not** persistence across restarts; resuming an agent conversation after that depends on the agent's own capabilities.

### Interactive terminal handling

- SSH command execution without a PTY is insufficient for many interactive agent CLIs. A working manual example is `ssh -tt <host> 'cd <directory> && claude --resume'` (replace host/directory as appropriate).
- A simple `stdin`/`stdout` pipe is **not equivalent** to a correctly functioning terminal. Interactive agents may need terminal resizing, ANSI escape handling, cursor movement, control keys, and an allocated PTY.
- Evaluate a proper terminal emulator component (for example xterm.js via a web view, or a suitable Avalonia-native implementation); do **not** assume `tmux capture-pane` provides a faithful live TUI display.
- The host-side Windows application may need a Windows pseudo-console or another PTY-capable approach for interactive `ssh.exe`/`wsl.exe` processes. Validate this in a focused spike before committing to a specific terminal library.
- For long prompts, investigate tmux buffer/paste mechanisms rather than naively injecting text keystroke by keystroke. Verify actual behavior with Codex/Claude/Pi, especially multiline inputs and paste semantics.

## 4. Proposed minimum viable product (MVP)

1. Register and select WSL distributions and remote Linux SSH targets.
2. Discover and list tmux sessions on the selected target.
3. Attach to a session and display its interactive terminal accurately.
4. Switch between multiple independent agent sessions without terminating them.
5. Compose and edit multiline prompts safely in a GUI text editor.
6. Default to **Enter = newline** and **Ctrl+Enter = send** for the prompt composer (configurability can come later).
7. Send text to the **explicitly selected** tmux session, with precautions against sending to the wrong agent.
8. Disconnect and reconnect to an existing tmux session without restarting the running agent.
9. Handle connection errors, disconnects, missing tmux sessions, and agent exits gracefully.

It is fine to implement only one target/environment and one agent for an initial proof of concept. **Favor a small, testable vertical slice over implementing all MVP items at once.**

## 5. Important distinctions and constraints

- **tmux session vs. agent conversation:** tmux manages a live terminal/process session. Codex/Claude/Pi each have their own conversation/history mechanisms. They are not interchangeable.
- **Sharing vs. concurrent control:** Multiple clients can attach to a tmux session, but simultaneous input from different clients can conflict. Avoid accidental double-control.
- **Discovery vs. state:** A tmux session name can indicate intended role (e.g., `codex-impl`, `claude-review`, `pi-local`), but is not proof of the actual running agent or its current state. Reliable status detection may require process inspection or agent-specific APIs later.
- **Long prompts:** Preserve UTF-8/Japanese text, line breaks, and input intent; do not blindly append Enter if sending via paste would make prompt submission ambiguous.
- **Security:** A remote agent terminal effectively provides shell access. Respect existing SSH authentication, validate and safely quote commands and paths, avoid logging secrets or unnecessary terminal content, and do not expose a remotely accessible control API by default.
- **Repository hygiene:** Do not commit credentials, local machine paths, tokens, logs containing secrets, or environment-specific settings. Store target connections in appropriately scoped configuration later.
- **License compatibility:** Keep MIT for project-owned code, and check third-party dependencies' license obligations before packaging/distribution.

## 6. A reasonable staged roadmap (proposal, not an implementation mandate)

**Phase 0 — Repository foundation**
- Initial README, architecture notes, sensible `.gitignore`, and basic project organization after approval.

**Phase 1 — Technical feasibility spike**
- From a simple C# program on Windows, connect to one WSL or SSH target and attach to an existing tmux session through a correct interactive terminal interface.
- Confirm display fidelity, terminal resize, Ctrl+C, reconnection, and Unicode/Japanese handling.

**Phase 2 — Minimal Avalonia GUI**
- One connection, session list, terminal view, prompt composer, controlled sending, and reattachment.

**Phase 3 — Multi-target and usability**
- WSL + multiple SSH hosts, tabs or panes, session labels, clearer lifecycle/error handling, settings, and keyboard shortcuts.

**Later (out of initial scope)**
- Native Windows Codex/Claude via ConPTY and a persistent session host.
- Structured agent integrations such as Codex App Server or Pi RPC, when they materially improve state visibility and control.
- Cross-platform GUI distribution where practical.
- Optional coordination/workflow features, without losing the human-directed console focus.

## 7. Open questions to validate experimentally

- Which Avalonia-compatible terminal emulator or WebView approach provides reliable full-screen TUI behavior?
- How should a Windows-hosted app maintain a PTY-backed interactive SSH/WSL transport?
- Should the implementation use tmux attach, tmux control mode, tmux external commands, or a combination?
- What is the safest, most predictable way to paste a multiline prompt and explicitly submit it across different agents?
- What is the right model for session identity across host, tmux session, tmux window/pane, and agent conversation?
- How should GUI sessions coexist with conventional Windows Terminal clients?

Avoid claiming these questions have already been resolved.

## 8. Open-source collaboration philosophy

Codirva is licensed under the **MIT License**. Commercial use, modifications, redistribution, and use in proprietary products are welcome, subject to the license terms. Contributions and feedback are warmly encouraged, but **sharing modifications upstream is voluntary and never a condition of using the software**.

Suggested README note:

> Contributions, bug reports, and suggestions are welcome. If you improve Codirva, please consider sharing your ideas or patches through an issue or pull request. Contributing back is entirely optional.

---

**Reminder to Codex:** For the initial task, **write `README.md` first**, accurately reflect the early-stage status, inspect the actual repository state, and refrain from implementing the whole application until asked.
