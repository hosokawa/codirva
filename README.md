# Codirva

*A unified desktop console for coding agents.*

Codirva (Japanese IPA: /kodiɾɯba/) is a planned Windows desktop app for developers who work with several terminal-based coding agents. It aims to bring those sessions into one window so you can see which session you’re controlling, switch between sessions, and compose long prompts in a dedicated editor.

Codirva is a human-operated console. You choose the session, write the prompt, and decide when to send it. Autonomous task planning and agent-team orchestration are outside the initial scope. Codirva will work with existing interactive CLIs, initially targeting Codex CLI, Claude Code, and Pi, with DSH and other agents as possible additions. The design aims to stay independent of any one agent, model, or vendor.

## Development status

The Windows prototype lists tmux sessions from all configured WSL2 and SSH targets together by project and attaches to the selected session in an interactive terminal. Each session shows its connection. The prompt composer supports multiline input and submission to Codex CLI and Claude Code.

See [Development setup](docs/development.md) to set up and run the prototype.

Copy [config.example.json](config.example.json) to your user configuration directory as `config.json`, then edit the connection targets for your environment. On Windows this is `%APPDATA%\Codirva\config.json`; on Linux it is `~/.config/codirva/config.json` (or `$XDG_CONFIG_HOME/codirva/config.json`); on macOS it is `~/Library/Application Support/Codirva/config.json`. See the development guide for copy commands. The current application is a Windows prototype; these paths do not imply Linux or macOS runtime support.

## Planned architecture

| Area | Initial plan |
| --- | --- |
| Desktop app | C# / .NET with Avalonia UI on Windows |
| Local execution | WSL2 Linux through `wsl.exe` or an equivalent WSL connection |
| Remote execution | Linux over Windows OpenSSH (`ssh.exe`), with a pseudo-terminal (PTY) for interactive attachment |
| Session management | `tmux` on each Linux target for session discovery and reattachment |
| Terminal rendering | A terminal emulator that supports full-screen interfaces, ANSI sequences, resizing, and control keys |

The desktop UI, connection transport, terminal rendering, and session management will be separate so the same UI can work with WSL2 and remote Linux. The Windows-side PTY approach, terminal component, and use of tmux control mode still need validation. Native Windows agent execution is deferred.

An agent running in tmux can stay active after the app closes or the SSH connection drops, as long as the tmux server and its host keep running. A tmux session holds the live terminal and process. After a reboot or tmux server exit, conversation recovery depends on the agent’s own history and resume features. A conventional terminal can attach to the same session, and simultaneous input from multiple clients can conflict.

## Planned MVP

- Register and select WSL distributions and remote Linux SSH hosts. List tmux sessions on the selected target.
- Attach to a session in an interactive terminal, switch between sessions without stopping their agents, and reconnect to a running session after disconnecting.
- Compose and edit multiline prompts in a dedicated editor. **Enter** inserts a newline and **Ctrl+Enter** sends the composed text to the explicitly selected session. Validate multiline paste, Unicode (including Japanese), and submission behavior with each agent.
- Show clear feedback for connection failures, dropped connections, missing sessions, and agent exits.

The SSH connection will use existing authentication. The app should safely handle commands and paths, keep secrets and unnecessary terminal output out of logs, and leave remote control APIs disabled by default.

## Proposed first milestone

Use a small C# program on Windows to attach to one existing tmux session on WSL2 or a remote Linux host. With one agent, verify terminal display, resizing, Ctrl+C, reconnection, and Unicode input before choosing a terminal component and building the Avalonia interface.

## Contributing and license

Feedback, bug reports, suggestions, and pull requests are welcome. Contributing back is entirely optional. The [MIT License](LICENSE) permits commercial use, modification, redistribution, and use in proprietary products under its terms.
