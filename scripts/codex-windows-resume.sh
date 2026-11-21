#!/bin/sh
set -eu

if ! command -v powershell.exe >/dev/null 2>&1; then
    printf '%s\n' 'powershell.exe is unavailable. Run this from a WSL shell with Windows interop enabled.' >&2
    exit 127
fi

if [ -n "${TMUX_PANE:-}" ]; then
    cleanup() {
        tmux set-option -p -u -t "$TMUX_PANE" @codirva_windows_codex_pid 2>/dev/null || :
    }
    trap cleanup EXIT
    trap 'exit 129' HUP
    trap 'exit 130' INT
    trap 'exit 143' TERM
    tmux set-option -p -t "$TMUX_PANE" @codirva_windows_codex_pid "$$"
fi

powershell.exe -NoLogo -NoProfile -Command '
    $codex = (Get-Command codex.exe -CommandType Application -ErrorAction SilentlyContinue).Source
    if (-not $codex) {
        $codex = Join-Path $env:LOCALAPPDATA "Programs/OpenAI/Codex/bin/codex.exe"
        if (-not (Test-Path -LiteralPath $codex -PathType Leaf)) {
            [Console]::Error.WriteLine("codex.exe was not found on the Windows PATH or at $codex.")
            exit 127
        }
    }
    & $codex resume
    exit $LASTEXITCODE
'
