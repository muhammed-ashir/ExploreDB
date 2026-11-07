# Agent Guidelines & Rules

## Project Modification Policy
- **No External Scripts for Code Updates**: Never create or execute scripts (Node.js, PowerShell, Python, Bash, batch, etc.) to modify, replace, or update project source files.
- **Direct Agent Edits Only**: All code, markup, styles, and configuration modifications must be performed directly using the built-in file tools (`edit_file` and `write_file`).
- **Reasons & Guardrails**:
  - Updating files via scripts risks corrupting text encoding (BOM/UTF-8), newline formats (CRLF/LF), and binary/font assets (causing icon crashes).
  - Script modifications bypass the environment's change-tracking mechanism, preventing safe rollbacks, undo, and clean file diffs.
