# Development Setup

Codirva is at the design stage. This guide prepares a Windows machine for the first feasibility test, which will attach to a tmux session running in WSL2. You can verify the setup below before any Codirva code is available to build.

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

## Tools for later milestones

- **Avalonia GUI.** When GUI development begins, install the project templates from Windows PowerShell with `dotnet new install Avalonia.Templates`. The optional Avalonia for VSCode extension adds `.axaml` editing support. [Avalonia template setup](https://docs.avaloniaui.net/docs/get-started/install-avalonia) · [VS Code setup](https://docs.avaloniaui.net/docs/get-started/set-up-your-ide)
- **Remote Linux over SSH.** Check for the Windows OpenSSH Client with `ssh.exe -V` in PowerShell. If it is missing, enable **OpenSSH Client** in Windows Optional Features. The remote Linux host needs a reachable SSH server, tmux, and a working agent CLI. [Windows OpenSSH setup](https://learn.microsoft.com/en-us/windows-server/administration/openssh/openssh_install_firstuse)
