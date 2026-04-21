# Security

- The model can only request structured tools.
- Host command execution is forbidden.
- Shell commands run inside Docker only.
- Workspace paths are normalized and checked to stay inside the session workspace.
- Shell commands pass a strict prefix allowlist and blocked-pattern policy.
- File sizes, output sizes, tool counts, shell counts, and run duration are capped.
- Session events are persisted for auditability.
