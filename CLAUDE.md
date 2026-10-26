# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

See `AGENTS.md` for all project conventions, architecture, build commands, code style, debugging workflow, and working-with-AI-agents rules. Everything there applies here too.

## Claude-Specific Notes

- **Presenting options**: Always use the `AskUserQuestion` popup tool when presenting 2+ choices. Never list options in plain text.
- **Attribution**: Do not add `🤖 Generated with Claude Code` or similar footers to PR descriptions, issue bodies, or commit messages.
- **Hooks** (`.claude/settings.json`, scripts in `.claude/hooks/`): when a reply ends with changed `.cs` or `.xaml` files, `build-on-stop.ps1` builds what the change can affect before handing back, and a build that is not clean — errors *or* warnings, since zero warnings is the standard — blocks the stop once with the diagnostics. The test project plus Core always builds (seconds, and it is the one that gets missed: building the app project does not compile `tests/`, and `dotnet test` filtered for failures hides warnings entirely); the Android TFM builds when something under `src/AniSprinkles/` changed; the `-p:CiBuild=true` variant builds when a fixture or a `#if CI` file changed, because nothing else compiles those blocks. It skips in about half a second when the sources are unchanged since the last clean run (fingerprint in the git-ignored `.claude/hooks/.last-build`), and with no fingerprint yet — a fresh worktree — it asks git what differs from `HEAD` rather than building all three blind, which costs four and a half minutes against three seconds for a Core-only change. It lets the second stop through so a build it cannot fix is reported rather than looped on. It runs no tests — that is `/ani-review`'s job. Switch it off with `/hooks`.
