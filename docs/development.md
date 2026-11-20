# Development Setup

This guide prepares a Windows machine to run the initial Codirva prototype and verify the WSL2 tmux setup.

## Prerequisites on Windows

- **Windows 11 with WSL2.** Ubuntu is used in the examples below. [Microsoft’s WSL installation guide](https://learn.microsoft.com/en-us/windows/wsl/install) lists supported Windows versions.
- **WinGet**, provided by Microsoft’s App Installer. Run `winget --version` in Windows PowerShell to check that it is available. If the command is missing, install or update **App Installer** from the Microsoft Store. [WinGet setup](https://learn.microsoft.com/en-us/windows/package-manager/winget/)

Run the commands for any tools you still need in Windows PowerShell. The .NET 10 SDK (LTS) includes the C# compiler and MSBuild. [WinGet install reference](https://learn.microsoft.com/en-us/windows/package-manager/winget/install) · [.NET installation on Windows](https://learn.microsoft.com/en-us/dotnet/core/install/windows) · [VS Code package manifest](https://github.com/microsoft/winget-pkgs/tree/master/manifests/m/Microsoft/VisualStudioCode)

```powershell
winget install --id Microsoft.DotNet.SDK.10 --exact --source winget
winget install --id Git.Git --exact --source winget
winget install --id Microsoft.VisualStudioCode --exact --source winget
```

Open a new PowerShell window so it picks up any changes to `PATH`. Check that Git, a 10.0-series SDK, and VS Code are available, then install the Microsoft C# extension:

```powershell
git --version
dotnet --list-sdks
code --version
code --install-extension ms-dotnettools.csharp
```

Open the repository in a local Windows VS Code window so the build and debugger use the Windows SDK. C# Dev Kit is optional. [C# in VS Code](https://code.visualstudio.com/docs/csharp/get-started) · [VS Code command-line reference](https://code.visualstudio.com/docs/configure/command-line)

## Set up WSL2 and tmux

1. On a machine without WSL, run `wsl --install` in an administrator PowerShell window. Restart Windows, open Ubuntu, and complete the first-run account setup. If you already have a WSL2 distribution, you can use it instead. [WSL setup instructions](https://learn.microsoft.com/en-us/windows/wsl/install)
2. In Windows PowerShell, run `wsl -l -v` and confirm that the distribution you plan to use shows `2` in the `VERSION` column. [WSL command reference](https://learn.microsoft.com/en-us/windows/wsl/basic-commands)
3. In the Ubuntu shell, run the following commands. For another distribution, use its package manager. [tmux installation guide](https://github.com/tmux/tmux/wiki/Installing)

   ```bash
   sudo apt update
   sudo apt install tmux
   tmux -V
   ```

4. Install and sign in to one terminal-based coding agent inside that Linux distribution. Use whichever agent you already work with and confirm that it opens an interactive session there.

## Verify session reattachment

From the Linux shell, create a named session:

```bash
tmux new-session -s codirva-dev
```

Start your agent inside tmux. With the agent idle, press **Ctrl+B**, release it, then press **D** to detach. Back in the Linux shell, list sessions and reattach:

```bash
tmux list-sessions
tmux attach-session -t codirva-dev
```

The same running agent should still be there. Try typing a line of Japanese text in the prompt editor to verify that it displays correctly. This checks the Linux test environment and gives you a baseline for the Windows connection test.

## Configure connections

Copy `config.example.json` from the repository root to your user configuration directory as `config.json`, then edit its `targets` array. Each `wsl` target names a distribution; each `ssh` target names an OpenSSH host alias or `user@host`. Do not put passwords or private keys in this file. The user configuration is outside the repository and is not committed.

On Windows, from the repository root in PowerShell:

```powershell
New-Item -ItemType Directory -Force -Path "$env:APPDATA\Codirva"
Copy-Item config.example.json "$env:APPDATA\Codirva\config.json"
```

For a future native Linux or macOS build, the corresponding locations are `$XDG_CONFIG_HOME/codirva/config.json` (default `~/.config/codirva/config.json`) and `~/Library/Application Support/Codirva/config.json`. These locations describe the configuration format, not current platform support. Copy with `mkdir -p ~/.config/codirva && cp config.example.json ~/.config/codirva/config.json` on Linux (adjust for `XDG_CONFIG_HOME`), or `mkdir -p "$HOME/Library/Application Support/Codirva" && cp config.example.json "$HOME/Library/Application Support/Codirva/config.json"` on macOS. These commands overwrite an existing configuration.

## Run the prototype

From the repository root in Windows PowerShell, run:

```powershell
dotnet run --project src/Codirva/Codirva.csproj
```

All configured connections are queried together. **Projects → ⋯ → Refresh** reloads the configuration file and queries all targets again. Sessions from different targets with the same project name appear under one project, with their connection shown beside each session. A connection failure is reported while successful targets remain visible. For SSH, the Windows OpenSSH client must be installed, the host key must already be trusted, and authentication must work without an interactive password or passphrase prompt (for example, with an SSH agent). SSH configuration can provide a non-default port or identity. Selecting a listed session opens an interactive terminal; the prompt-composer restrictions described below apply over SSH too.

The left pane groups sessions named `project/session` under their project name. The name must contain exactly one slash, with a nonempty name on each side. For example, `machi-match/coder` appears as `coder` under `machi-match`. Select a session to see its full tmux name. Use **Refresh** after creating or closing sessions in WSL.

Selecting a session attaches the right-hand terminal to tmux. Use the terminal for interactive menus and keys such as **Ctrl+C**. Switching sessions or closing Codirva detaches its terminal client and leaves the tmux session running.

Hover over **Projects** or focus its buttons to show **+** and **⋯**. Choose **+** to select a connection and enter a group name and session name. **Add** opens a shell in the connection's home directory and remembers the session. An existing session with the same name on the same connection is opened as well. **Refresh** and **Exit** are in the **Projects** menu.

When a saved session has stopped, hover over its row or focus its actions to show **Start** and **⋯**. **Start** opens a new shell in that session. The row menu's **Forget** action removes the saved entry after checking that the session is still absent. If the connection is unavailable, the entry stays visible until its status can be checked.

To send a prompt, launch Codex CLI or Claude Code in a tmux session with one window and one pane. When the agent is ready for input, type in Codirva’s prompt box. **Enter** inserts a newline. **Ctrl+Enter** or **Send** pastes and submits the prompt to the selected pane. The pane must have bracketed paste enabled and must not be in copy mode. Drafts are held per session while Codirva is open. Verify multiline and Japanese input with your agent before relying on it.

## Tools for later milestones

- **Avalonia editing tools.** The optional Avalonia for VSCode extension adds `.axaml` editing support. Install project templates with `dotnet new install Avalonia.Templates` if you need to create a new Avalonia project. [Avalonia template setup](https://docs.avaloniaui.net/docs/get-started/install-avalonia) · [VS Code setup](https://docs.avaloniaui.net/docs/get-started/set-up-your-ide)
- **Remote Linux over SSH.** Check for the Windows OpenSSH Client with `ssh.exe -V` in PowerShell. If it is missing, enable **OpenSSH Client** in Windows Optional Features. The remote Linux host needs a reachable SSH server, tmux, and a working agent CLI. [Windows OpenSSH setup](https://learn.microsoft.com/en-us/windows-server/administration/openssh/openssh_install_firstuse)
