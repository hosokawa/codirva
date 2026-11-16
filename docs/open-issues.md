# Open Implementation Issues

## Input behavior in the interactive terminal

**Status** — Open

The right pane displays the live tmux terminal and accepts keyboard input. Because it looks and behaves like a standalone terminal, users can start typing there by habit even when they intended to use the prompt composer below.

Decide when the terminal should receive keyboard focus and forward keystrokes. The design should keep terminal output selectable and copyable and give users a way to respond to Codex CLI confirmation menus or interrupt a running operation with Ctrl+C.

One option is to start in a view-only mode that supports scrolling, selection, and copying. An explicit **Control terminal** action would grant keyboard focus, with a visible indicator and an action to leave that mode. Selecting another session would return the terminal to view-only mode.

An always-view-only terminal is also possible. Confirmation menus and interrupts would then need dedicated controls or access through an external terminal.
